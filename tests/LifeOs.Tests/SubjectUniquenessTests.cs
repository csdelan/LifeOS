using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// The uniqueness backstop for reuse-by-title subjects: Problems are unique on
/// title (so resolve-or-create can never silently diverge), whitespace-only
/// differences resolve to the same Problem, and types that are NOT reuse-by-title
/// (e.g. Task) are deliberately left unconstrained.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SubjectUniquenessTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private IServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    [Fact]
    public async Task Titles_that_differ_only_by_whitespace_resolve_to_the_same_problem()
    {
        var service = Provider().GetRequiredService<SubjectService>();
        var ns = Guid.NewGuid().ToString("N");

        var first = await service.ResolveOrCreateAsync(SubjectTypes.Problem, $"How  do   I sleep {ns}", Ct);
        var second = await service.ResolveOrCreateAsync(SubjectTypes.Problem, $"   How do I sleep {ns}  ", Ct);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Subject.Id, second.Subject.Id);
        Assert.DoesNotContain("  ", first.Subject.Title);
    }

    [Fact]
    public async Task Creating_a_second_problem_with_the_same_title_is_rejected()
    {
        var repository = Provider().GetRequiredService<ISubjectRepository>();
        var g = Guid.NewGuid().ToString("N");
        var title = $"duplicate problem {g}";

        await repository.CreateAsync(new NewSubject($"urn:bsk:problem:a-{g}", SubjectTypes.Problem, title), Ct);

        await Assert.ThrowsAsync<DuplicateSubjectException>(async () =>
            await repository.CreateAsync(new NewSubject($"urn:bsk:problem:b-{g}", SubjectTypes.Problem, title), Ct));
    }

    [Fact]
    public async Task Non_reuse_types_may_share_a_title()
    {
        var provider = Provider();
        var repository = provider.GetRequiredService<ISubjectRepository>();
        var g = Guid.NewGuid().ToString("N");
        var title = $"email John {g}";

        // Two distinct tasks can share a title — the partial index must not forbid this.
        await repository.CreateAsync(new NewSubject($"urn:bsk:task:a-{g}", SubjectTypes.Task, title), Ct);
        await repository.CreateAsync(new NewSubject($"urn:bsk:task:b-{g}", SubjectTypes.Task, title), Ct);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.subject WHERE type = 'Task' AND title = @title;",
            new { title }, cancellationToken: Ct));

        Assert.Equal(2, count);
    }
}
