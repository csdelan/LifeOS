using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// GEN-2 / D1 (migration 0014): Area of Focus as a durable subject type. An Area is
/// created like any subject; items point to it with a soft `attributes.area`
/// reference (set via the existing `bsk set`), surfaced on v_subject and listed by
/// v_area. Areas are permanent — the single writer refuses to archive one.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AreaTypeTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return connection;
    }

    [Fact]
    public async Task Area_is_a_creatable_subject_type_listed_by_v_area()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var attributes = provider.GetRequiredService<AttributeService>();
        var name = $"Trading {Guid.NewGuid():N}";

        var area = await subjects.CreateAsync(SubjectTypes.Area, name, cancellationToken: Ct);
        await attributes.SetAsync(area.Urn,
            [new AttributeAssignment("description", "Everything about the trading business")], Ct);

        await using var connection = await OpenAsync();
        var row = await connection.QuerySingleAsync<(string Name, string Description)>(new CommandDefinition(
            "SELECT name, description FROM bsk.v_area WHERE id = @id;",
            new { id = area.Id }, cancellationToken: Ct));

        Assert.Equal(name, row.Name);
        Assert.Equal("Everything about the trading business", row.Description);
    }

    [Fact]
    public async Task An_item_points_to_its_area_and_it_surfaces_on_v_subject()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var attributes = provider.GetRequiredService<AttributeService>();

        var area = await subjects.CreateAsync(SubjectTypes.Area, $"Health {Guid.NewGuid():N}", cancellationToken: Ct);
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, $"Run a 10k {Guid.NewGuid():N}", cancellationToken: Ct);

        await attributes.SetAsync(goal.Urn, [new AttributeAssignment("area", area.Urn)], Ct);

        await using var connection = await OpenAsync();
        var storedArea = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
            "SELECT area FROM bsk.v_subject WHERE id = @id;",
            new { id = goal.Id }, cancellationToken: Ct));
        Assert.Equal(area.Urn, storedArea);

        // The Area's members are answerable by filtering items on that reference.
        var members = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.v_subject WHERE area = @area;",
            new { area = area.Urn }, cancellationToken: Ct));
        Assert.Equal(1, members);
    }

    [Fact]
    public async Task An_area_cannot_be_archived()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var archive = provider.GetRequiredService<ArchiveService>();

        var area = await subjects.CreateAsync(SubjectTypes.Area, $"Family {Guid.NewGuid():N}", cancellationToken: Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await archive.ArchiveAsync(area.Urn, Ct));
    }
}
