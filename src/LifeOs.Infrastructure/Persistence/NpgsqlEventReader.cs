using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Reads source events from <c>bsk.event</c>.</summary>
public sealed class NpgsqlEventReader(string connectionString) : IEventReader
{
    public async Task<SourceEvent?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // subject_id is read as text and parsed here; a payload need not carry one,
        // and when present it is a subject uuid (e.g. the Problem an idea_session names).
        var row = await connection.QuerySingleOrDefaultAsync<(Guid Id, string Kind, string? SubjectId)?>(
            new CommandDefinition(
                "SELECT id, kind, payload->>'subject_id' AS SubjectId FROM bsk.event WHERE id = @id;",
                new { id }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return null;
        }

        var subjectId = Guid.TryParse(row.Value.SubjectId, out var parsed) ? parsed : (Guid?)null;
        return new SourceEvent(row.Value.Id, row.Value.Kind, subjectId);
    }
}
