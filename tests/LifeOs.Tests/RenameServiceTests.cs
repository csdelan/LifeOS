using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk rename</c>'s service: changing a subject's title after creation. The title
/// is a first-class column (not settable via <c>bsk set</c>); a rename changes only
/// it and leaves the URN — the stable handle every reference keys on — untouched.
/// A reuse-by-title conflict (two Problems, one title) is rejected; other types are free.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RenameServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<string?> ReadTitle(Guid id)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT title FROM bsk.subject WHERE id = @id;", new { id }, cancellationToken: Ct));
    }

    [Fact]
    public async Task Rename_changes_the_title_and_keeps_the_urn()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var rename = provider.GetRequiredService<RenameService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Task, $"buy groceries {Guid.NewGuid():N}", cancellationToken: Ct);

        var result = await rename.RenameAsync(subject.Urn, "weekly shopping run", Ct);

        Assert.True(result.Changed);
        Assert.Equal(subject.Id, result.Subject.Id);
        Assert.Equal("weekly shopping run", result.Subject.Title);
        Assert.Equal(subject.Urn, result.Subject.Urn); // URN is immutable across a rename.
        Assert.Equal("weekly shopping run", await ReadTitle(subject.Id));

        // The stable handle still resolves the same subject after the rename.
        var repository = provider.GetRequiredService<ISubjectRepository>();
        var found = await repository.FindByUrnAsync(subject.Urn, Ct);
        Assert.NotNull(found);
        Assert.Equal(subject.Id, found!.Id);
        Assert.Equal("weekly shopping run", found.Title);
    }

    [Fact]
    public async Task Rename_normalizes_whitespace()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var rename = provider.GetRequiredService<RenameService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Task, $"draft {Guid.NewGuid():N}", cancellationToken: Ct);

        var result = await rename.RenameAsync(subject.Urn, "  call   the   dentist  ", Ct);

        Assert.True(result.Changed);
        Assert.Equal("call the dentist", result.Subject.Title);
        Assert.Equal("call the dentist", await ReadTitle(subject.Id));
    }

    [Fact]
    public async Task Rename_to_the_current_title_is_a_no_op()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var rename = provider.GetRequiredService<RenameService>();

        var title = $"unchanged {Guid.NewGuid():N}";
        var subject = await subjects.CreateAsync(SubjectTypes.Task, title, cancellationToken: Ct);

        // Same title, only differing by surrounding whitespace: normalized, it equals
        // the current title, so nothing is written.
        var result = await rename.RenameAsync(subject.Urn, $"  {title}  ", Ct);

        Assert.False(result.Changed);
        Assert.Equal(title, result.Subject.Title);
    }

    [Fact]
    public async Task Renaming_a_problem_onto_an_existing_problem_title_is_rejected()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var rename = provider.GetRequiredService<RenameService>();
        var ns = Guid.NewGuid().ToString("N");

        var taken = $"how do I sleep {ns}";
        await subjects.ResolveOrCreateAsync(SubjectTypes.Problem, taken, Ct);
        var other = (await subjects.ResolveOrCreateAsync(SubjectTypes.Problem, $"why am I tired {ns}", Ct)).Subject;

        var ex = await Assert.ThrowsAsync<DuplicateSubjectException>(async () =>
            await rename.RenameAsync(other.Urn, taken, Ct));

        Assert.Equal(SubjectTypes.Problem, ex.SubjectType);
        // The clashing Problem keeps its own title — the rejected rename wrote nothing.
        Assert.Equal("why am I tired " + ns, await ReadTitle(other.Id));
    }

    [Fact]
    public async Task Two_tasks_may_share_a_title_after_a_rename()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var rename = provider.GetRequiredService<RenameService>();
        var ns = Guid.NewGuid().ToString("N");

        var shared = $"follow up {ns}";
        await subjects.CreateAsync(SubjectTypes.Task, shared, cancellationToken: Ct);
        var second = await subjects.CreateAsync(SubjectTypes.Task, $"temp {ns}", cancellationToken: Ct);

        // Tasks are not reuse-by-title, so a collision on title is allowed.
        var result = await rename.RenameAsync(second.Urn, shared, Ct);

        Assert.True(result.Changed);
        Assert.Equal(shared, await ReadTitle(second.Id));
    }

    [Fact]
    public async Task Rename_of_an_unknown_subject_is_an_error()
    {
        await using var provider = Provider();
        var rename = provider.GetRequiredService<RenameService>();

        await Assert.ThrowsAsync<SubjectNotFoundException>(async () =>
            await rename.RenameAsync("urn:bsk:task:does-not-exist-abcdef", "anything", Ct));
    }

    [Fact]
    public async Task Rename_to_a_blank_title_is_rejected()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var rename = provider.GetRequiredService<RenameService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Task, $"keep me {Guid.NewGuid():N}", cancellationToken: Ct);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await rename.RenameAsync(subject.Urn, "   ", Ct));
    }
}
