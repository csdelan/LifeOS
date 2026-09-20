using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk set</c>'s service: editing a subject's attributes in place. A patch merges
/// (existing keys survive), a blank value removes a key, and an unknown subject errors.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AttributeServiceTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Provider()
        => new ServiceCollection().AddLifeOsKernel(postgres.ConnectionString).BuildServiceProvider();

    private async Task<string?> ReadAttribute(Guid id, string key)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT attributes->>@key FROM bsk.subject WHERE id = @id;",
            new { id, key }, cancellationToken: Ct));
    }

    [Fact]
    public async Task Set_merges_attributes_and_preserves_existing()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var attributes = provider.GetRequiredService<AttributeService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Task, $"take out the garbage {Guid.NewGuid():N}",
            """{"expected_cadence":"weekly"}""", cancellationToken: Ct);

        var result = await attributes.SetAsync(
            subject.Urn, [new AttributeAssignment("due", "2026-09-01")], Ct);

        Assert.Equal(subject.Id, result.Subject.Id);
        Assert.Contains("due", result.SetKeys);
        Assert.Equal("2026-09-01", await ReadAttribute(subject.Id, "due"));
        // The pre-existing attribute is untouched by the merge.
        Assert.Equal("weekly", await ReadAttribute(subject.Id, "expected_cadence"));
    }

    [Fact]
    public async Task Set_with_a_blank_value_removes_the_key()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var attributes = provider.GetRequiredService<AttributeService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Task, $"clear due {Guid.NewGuid():N}",
            """{"due":"2026-09-01"}""", cancellationToken: Ct);

        var result = await attributes.SetAsync(subject.Urn, [new AttributeAssignment("due", null)], Ct);

        Assert.Contains("due", result.RemovedKeys);
        Assert.Null(await ReadAttribute(subject.Id, "due"));
    }

    [Fact]
    public async Task Set_rejects_title_and_points_at_rename()
    {
        await using var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var attributes = provider.GetRequiredService<AttributeService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Task, $"no shadow title {Guid.NewGuid():N}", cancellationToken: Ct);

        // Title is a column, not an attribute: `set title=` would write a shadow key
        // that is never read, so it is refused with a pointer to `bsk rename`.
        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await attributes.SetAsync(subject.Urn, [new AttributeAssignment("title", "sneaky")], Ct));
        Assert.Contains("rename", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Nothing was written under the shadow key.
        Assert.Null(await ReadAttribute(subject.Id, "title"));
    }

    [Fact]
    public async Task Set_on_an_unknown_subject_is_an_error()
    {
        await using var provider = Provider();
        var attributes = provider.GetRequiredService<AttributeService>();

        await Assert.ThrowsAsync<SubjectNotFoundException>(async () =>
            await attributes.SetAsync(
                "urn:bsk:task:does-not-exist-abcdef",
                [new AttributeAssignment("due", "2026-09-01")], Ct));
    }
}
