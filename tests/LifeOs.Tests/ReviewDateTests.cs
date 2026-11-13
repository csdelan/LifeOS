using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A Decision's review date is free text (migration 0055). One that isn't a date reads as no
/// date, and every other item still reads.
/// </summary>
public sealed class ReviewDateTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_review_date_that_isnt_a_date_reads_as_none_and_breaks_nothing()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var dated = await DecisionAsync(database, "2026-09-15");
        var vague = await DecisionAsync(database, "soon-ish");
        var impossible = await DecisionAsync(database, "2026-02-30");

        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, DomainTestDatabase.ApiZone);

        var decisions = await host.Client.GetFromJsonAsync<JsonElement>("/api/subjects?type=Decision", Ct);

        string? ReviewOf(Guid id) => decisions.EnumerateArray()
            .Single(d => d.GetProperty("id").GetGuid() == id)
            .GetProperty("nextReviewAt").GetString();
        Assert.StartsWith("2026-09-15", ReviewOf(dated));
        Assert.Null(ReviewOf(vague));
        Assert.Null(ReviewOf(impossible));
    }

    private async Task<Guid> DecisionAsync(DomainTestDatabase database, string reviewAt)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<Guid>(new CommandDefinition(
            """
            INSERT INTO bsk.subject (urn, type, title, attributes)
            VALUES (@urn, 'Decision', @title, jsonb_build_object('next_review_at', @reviewAt))
            RETURNING id;
            """,
            new { urn = $"urn:bsk:decision:{Guid.NewGuid():N}", title = $"review {reviewAt}", reviewAt },
            cancellationToken: Ct));
    }
}
