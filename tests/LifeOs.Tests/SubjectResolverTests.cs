using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The shared subject resolver (M3.1): naming an existing subject by URN, by short
/// id, or by a unique fuzzy title, and reporting candidates rather than guessing
/// when a title is ambiguous. Also covers <c>bsk new</c>'s create-with-attributes.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SubjectResolverTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private SubjectService Service()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString)
            .BuildServiceProvider().GetRequiredService<SubjectService>();

    private static string ShortIdOf(string urn) => urn[(urn.LastIndexOfAny(['-', ':']) + 1)..];

    [Fact]
    public async Task Resolves_by_urn_short_id_and_unique_title()
    {
        var service = Service();
        var title = $"finish the resolver {Guid.NewGuid():N}";
        var created = await service.CreateAsync(SubjectTypes.Task, title, cancellationToken: Ct);

        var byUrn = await service.ResolveAsync(created.Urn, Ct);
        var byShortId = await service.ResolveAsync(ShortIdOf(created.Urn), Ct);
        var byTitle = await service.ResolveAsync(title, Ct);

        Assert.Equal(created.Id, byUrn.Id);
        Assert.Equal(created.Id, byShortId.Id);
        Assert.Equal(created.Id, byTitle.Id);
    }

    [Fact]
    public async Task Resolves_by_a_case_insensitive_title_fragment()
    {
        var service = Service();
        var marker = Guid.NewGuid().ToString("N");
        var created = await service.CreateAsync(SubjectTypes.Goal, $"Ship the Kernel {marker}", cancellationToken: Ct);

        var resolved = await service.ResolveAsync($"ship the kernel {marker}", Ct);

        Assert.Equal(created.Id, resolved.Id);
    }

    [Fact]
    public async Task An_ambiguous_title_returns_candidates_rather_than_guessing()
    {
        var service = Service();
        var marker = Guid.NewGuid().ToString("N");
        var a = await service.CreateAsync(SubjectTypes.Task, $"review {marker} alpha", cancellationToken: Ct);
        var b = await service.CreateAsync(SubjectTypes.Task, $"review {marker} beta", cancellationToken: Ct);

        var ex = await Assert.ThrowsAsync<AmbiguousSubjectException>(
            async () => await service.ResolveAsync($"review {marker}", Ct));

        var ids = ex.Candidates.Select(c => c.Id).ToList();
        Assert.Contains(a.Id, ids);
        Assert.Contains(b.Id, ids);
    }

    [Fact]
    public async Task An_exact_title_wins_over_broader_substring_matches()
    {
        var service = Service();
        var marker = Guid.NewGuid().ToString("N");
        var exact = await service.CreateAsync(SubjectTypes.Task, $"plan {marker}", cancellationToken: Ct);
        await service.CreateAsync(SubjectTypes.Task, $"plan {marker} in detail", cancellationToken: Ct);

        var resolved = await service.ResolveAsync($"plan {marker}", Ct);

        Assert.Equal(exact.Id, resolved.Id);
    }

    [Fact]
    public async Task An_unknown_reference_is_not_found()
    {
        var service = Service();

        await Assert.ThrowsAsync<SubjectNotFoundException>(
            async () => await service.ResolveAsync($"nothing matches {Guid.NewGuid():N}", Ct));
        await Assert.ThrowsAsync<SubjectNotFoundException>(
            async () => await service.ResolveAsync("urn:bsk:task:does-not-exist", Ct));
    }

    [Fact]
    public async Task New_creates_a_constraint_with_scope_and_limit()
    {
        var service = Service();
        var attributes = """{"scope":"capacity","limit":"2 open projects"}""";
        var created = await service.CreateAsync(
            SubjectTypes.Constraint, $"cap projects {Guid.NewGuid():N}", attributes, cancellationToken: Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(string Scope, string Limit, string Type)>(
            new CommandDefinition(
                "SELECT attributes->>'scope', attributes->>'limit', type FROM bsk.subject WHERE id = @id;",
                new { id = created.Id }, cancellationToken: Ct));

        Assert.Equal("capacity", row.Scope);
        Assert.Equal("2 open projects", row.Limit);
        Assert.Equal(SubjectTypes.Constraint, row.Type);
        Assert.StartsWith("urn:bsk:constraint:", created.Urn);
    }

    [Fact]
    public async Task New_creates_a_season_with_focus_and_end_date()
    {
        var service = Service();
        var attributes = """{"focus":"deep work","ends":"2026-12-31"}""";
        var created = await service.CreateAsync(
            SubjectTypes.Season, $"winter season {Guid.NewGuid():N}", attributes, cancellationToken: Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var row = await connection.QuerySingleAsync<(string Focus, string Ends)>(
            new CommandDefinition(
                "SELECT attributes->>'focus', attributes->>'ends' FROM bsk.subject WHERE id = @id;",
                new { id = created.Id }, cancellationToken: Ct));

        Assert.Equal("deep work", row.Focus);
        Assert.Equal("2026-12-31", row.Ends);
    }
}
