using System.Text.Json;
using Dapper;
using Npgsql;

namespace LifeOs.Infrastructure.Diagnostics;

/// <summary>
/// Runs the diagnostics behind <c>bsk check</c>: discovers the embedded SQL
/// diagnostics, executes each inside one shared <b>read-only</b> transaction (so
/// a diagnostic can never mutate, and every diagnostic sees the same snapshot),
/// and returns the findings grouped by diagnostic. The runner is deliberately
/// dumb about what any diagnostic means — it only enforces the result contract
/// (see <c>db/diagnostics/README.md</c>) so that every finding is explained.
/// </summary>
public sealed class DiagnosticRunner
{
    // The columns every diagnostic query must return, one row per finding.
    private const string SubjectIdColumn = "subject_id";
    private const string SubjectUrnColumn = "subject_urn";
    private const string SubjectTypeColumn = "subject_type";
    private const string SubjectTitleColumn = "subject_title";
    private const string SummaryColumn = "summary";
    private const string EvidenceColumn = "evidence";

    private readonly string _connectionString;
    private readonly EmbeddedDiagnosticSource _source;

    public DiagnosticRunner(string connectionString)
        : this(connectionString, new EmbeddedDiagnosticSource())
    {
    }

    public DiagnosticRunner(string connectionString, EmbeddedDiagnosticSource source)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
        _source = source;
    }

    /// <summary>The names of every discovered diagnostic, in report order.</summary>
    public IReadOnlyList<string> DiagnosticNames()
        => _source.Load().Select(d => d.Name).ToList();

    /// <summary>
    /// Runs every diagnostic, or just the one named by <paramref name="only"/>.
    /// </summary>
    /// <param name="only">
    /// When set, run only the diagnostic with this slug. An unknown slug is an
    /// error naming the diagnostics that do exist.
    /// </param>
    public Task<DiagnosticReport> RunAsync(string? only = null, CancellationToken cancellationToken = default)
    {
        var diagnostics = _source.Load();

        if (only is not null)
        {
            var selected = diagnostics.FirstOrDefault(d => string.Equals(d.Name, only, StringComparison.Ordinal));
            if (selected is null)
            {
                var available = diagnostics.Count == 0
                    ? "none are configured"
                    : string.Join(", ", diagnostics.Select(d => d.Name));
                throw new InvalidOperationException(
                    $"Unknown diagnostic '{only}'. Available: {available}.");
            }

            diagnostics = [selected];
        }

        return ExecuteAsync(diagnostics, cancellationToken);
    }

    /// <summary>
    /// Runs an explicit set of diagnostics, bypassing discovery. Used by
    /// <see cref="RunAsync"/> after it has loaded and filtered the embedded set,
    /// and directly by tests that supply their own SQL.
    /// </summary>
    public async Task<DiagnosticReport> ExecuteAsync(
        IReadOnlyList<Diagnostic> diagnostics, CancellationToken cancellationToken = default)
    {
        var results = new List<DiagnosticResult>(diagnostics.Count);

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Structurally guarantee no diagnostic can write, whatever its SQL says.
        await connection.ExecuteAsync(new CommandDefinition(
            "SET TRANSACTION READ ONLY;", transaction: transaction, cancellationToken: cancellationToken));

        foreach (var diagnostic in diagnostics)
        {
            var findings = await RunOneAsync(connection, transaction, diagnostic, cancellationToken);
            results.Add(new DiagnosticResult(diagnostic.Name, diagnostic.Title, findings));
        }

        await transaction.RollbackAsync(cancellationToken);
        return new DiagnosticReport(results);
    }

    private static async Task<IReadOnlyList<Finding>> RunOneAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IDictionary<string, object?>> rows;
        try
        {
            var queried = await connection.QueryAsync(new CommandDefinition(
                diagnostic.Sql, transaction: transaction, cancellationToken: cancellationToken));
            rows = queried.Cast<IDictionary<string, object?>>().ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Diagnostic '{diagnostic.Name}' failed to execute: {ex.Message}", ex);
        }

        var findings = new List<Finding>(rows.Count);
        foreach (var row in rows)
        {
            findings.Add(ToFinding(diagnostic.Name, row));
        }

        return findings;
    }

    private static Finding ToFinding(string diagnosticName, IDictionary<string, object?> row)
    {
        var subject = new FindingSubject(
            Id: Required<Guid>(diagnosticName, row, SubjectIdColumn),
            Urn: Required<string>(diagnosticName, row, SubjectUrnColumn),
            Type: Required<string>(diagnosticName, row, SubjectTypeColumn),
            Title: Required<string>(diagnosticName, row, SubjectTitleColumn));

        var summary = Required<string>(diagnosticName, row, SummaryColumn);
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException(
                $"Diagnostic '{diagnosticName}' returned a finding with a blank '{SummaryColumn}'. " +
                "Every finding must explain itself.");
        }

        return new Finding(subject, summary, ParseEvidence(diagnosticName, row));
    }

    private static JsonElement ParseEvidence(string diagnosticName, IDictionary<string, object?> row)
    {
        var raw = Required<string>(diagnosticName, row, EvidenceColumn);

        JsonElement evidence;
        try
        {
            using var document = JsonDocument.Parse(raw);
            evidence = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Diagnostic '{diagnosticName}' returned '{EvidenceColumn}' that is not valid JSON: {ex.Message}", ex);
        }

        if (evidence.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"Diagnostic '{diagnosticName}' must return '{EvidenceColumn}' as a JSON array, " +
                $"got {evidence.ValueKind}.");
        }

        return evidence;
    }

    private static T Required<T>(string diagnosticName, IDictionary<string, object?> row, string column)
    {
        if (!row.TryGetValue(column, out var value) || value is null)
        {
            throw new InvalidOperationException(
                row.ContainsKey(column)
                    ? $"Diagnostic '{diagnosticName}' returned a null '{column}'."
                    : $"Diagnostic '{diagnosticName}' is missing the required '{column}' column.");
        }

        return (T)value;
    }
}
