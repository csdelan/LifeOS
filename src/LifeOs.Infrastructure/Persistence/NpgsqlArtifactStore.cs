using Dapper;
using LifeOs.Application.Abstractions;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>Stores raw text content in <c>bsk.artifact</c>.</summary>
public sealed class NpgsqlArtifactStore(string connectionString) : IArtifactStore
{
    public async Task<Guid> AddAsync(string content, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            "INSERT INTO bsk.artifact (content) VALUES (@content) RETURNING id;",
            new { content }, cancellationToken: cancellationToken));
    }
}
