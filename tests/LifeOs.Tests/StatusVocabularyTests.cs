using Dapper;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// D7 (migration 0011): the per-type status vocabularies and the terminal-status
/// predicate. The kernel owns the terminal predicate the diagnostics rest on; the
/// per-type map is validated app-side and mirrored in
/// <see cref="StatusVocabulary"/>. These tests lock the new terminal words and,
/// crucially, keep the code mirror and the SQL predicate agreeing word-for-word —
/// the same two-mirrored-places discipline the ontology applies to Vocabulary.cs.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class StatusVocabularyTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    private async Task<bool> SqlIsTerminalAsync(NpgsqlConnection connection, string? status)
        => await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT bsk.is_terminal_status(@status);", new { status }, cancellationToken: Ct));

    // ---- the new terminal words this migration adds (D7) ----

    [Theory]
    [InlineData("fulfilled")]   // Commitment
    [InlineData("missed")]      // Commitment / Appointment
    [InlineData("promoted")]    // Idea
    [InlineData("rejected")]    // Idea
    public async Task New_terminal_words_are_terminal(string status)
    {
        await using var connection = await OpenAsync();
        Assert.True(await SqlIsTerminalAsync(connection, status));
    }

    // ---- non-terminal statuses across the type map must NOT be terminal ----

    [Theory]
    [InlineData("Active")]      // Goal / Project
    [InlineData("Not started")] // Task
    [InlineData("In progress")] // Task
    [InlineData("Waiting")]     // Task
    [InlineData("Open")]        // Commitment / Decision / Problem
    [InlineData("Implementing")]// Decision
    [InlineData("Working")]     // Problem
    [InlineData("Scheduled")]   // Appointment
    [InlineData("New")]         // Idea
    public async Task Live_statuses_are_not_terminal(string status)
    {
        await using var connection = await OpenAsync();
        Assert.False(await SqlIsTerminalAsync(connection, status));
    }

    // ---- the code mirror agrees with the SQL predicate for every vocabulary word ----

    [Fact]
    public async Task Domain_mirror_matches_the_sql_predicate_for_every_status()
    {
        // Every status word that appears anywhere in the map, deduplicated.
        var words = StatusVocabulary.StatusBearingTypes
            .SelectMany(StatusVocabulary.For)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using var connection = await OpenAsync();
        foreach (var word in words)
        {
            var sql = await SqlIsTerminalAsync(connection, word);
            Assert.Equal(sql, StatusVocabulary.IsTerminal(word));
        }
    }

    // ---- pure-Domain shape checks (no DB) ----

    [Fact]
    public void Every_status_bearing_type_has_at_least_one_terminal_status()
    {
        foreach (var type in StatusVocabulary.StatusBearingTypes)
        {
            Assert.Contains(StatusVocabulary.For(type), StatusVocabulary.IsTerminal);
        }
    }

    [Fact]
    public void Status_less_types_have_no_vocabulary()
    {
        Assert.Empty(StatusVocabulary.For(SubjectTypes.Value));
        Assert.Empty(StatusVocabulary.For(SubjectTypes.Person));
        Assert.Empty(StatusVocabulary.For(SubjectTypes.Season));
    }

    [Fact]
    public void Blank_and_null_statuses_are_active()
    {
        Assert.False(StatusVocabulary.IsTerminal(null));
        Assert.False(StatusVocabulary.IsTerminal(""));
        Assert.False(StatusVocabulary.IsTerminal("   "));
    }

    [Fact]
    public void Validity_is_case_insensitive_and_type_scoped()
    {
        Assert.True(StatusVocabulary.IsValid(SubjectTypes.Task, "in progress"));
        Assert.True(StatusVocabulary.IsValid(SubjectTypes.Idea, "PROMOTED"));
        Assert.False(StatusVocabulary.IsValid(SubjectTypes.Idea, "Fulfilled")); // wrong type
        Assert.False(StatusVocabulary.IsValid(SubjectTypes.Value, "Active"));   // status-less
    }
}
