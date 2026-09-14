using Dapper;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// GEN-1 (migration 0013): universal tags on items. Tags classify subjects and
/// events, are normalized (lowercased/trimmed), de-duped per item, and live in a
/// store separate from relations. These tests cover the write path (add/remove,
/// idempotent, case-folded), tagging a raw capture event, and the tag-universe read.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TagServiceTests(PostgresFixture postgres)
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

    private async Task<List<string>> TagsForSubjectAsync(NpgsqlConnection connection, Guid id)
        => (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT tag FROM bsk.item_tag WHERE subject_id = @id ORDER BY tag;",
            new { id }, cancellationToken: Ct))).ToList();

    [Fact]
    public async Task Add_normalizes_and_dedupes_then_remove_takes_it_away()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var tags = provider.GetRequiredService<TagService>();

        var subject = await subjects.CreateAsync(
            SubjectTypes.Project, $"tag project {Guid.NewGuid():N}", cancellationToken: Ct);

        // Mixed case + dupes + whitespace collapse to two normalized tags.
        var result = await tags.ApplyAsync(subject.Urn, ["Trading", " trading ", "Health"], [], Ct);
        Assert.Equal(2, result.AddedCount);

        await using var connection = await OpenAsync();
        Assert.Equal(["health", "trading"], await TagsForSubjectAsync(connection, subject.Id));

        // Re-adding an existing tag is idempotent.
        var again = await tags.ApplyAsync(subject.Urn, ["trading"], [], Ct);
        Assert.Equal(0, again.AddedCount);

        // Remove is case-insensitive too (input is normalized before matching).
        var removed = await tags.ApplyAsync(subject.Urn, [], ["TRADING"], Ct);
        Assert.Equal(1, removed.RemovedCount);
        Assert.Equal(["health"], await TagsForSubjectAsync(connection, subject.Id));
    }

    [Fact]
    public async Task A_raw_capture_event_can_be_tagged_before_it_is_a_subject()
    {
        var provider = Provider();
        var capture = provider.GetRequiredService<CaptureService>();
        var tags = provider.GetRequiredService<TagService>();

        var captured = await capture.CaptureNoteAsync($"an untriaged thought {Guid.NewGuid():N}", Ct);

        var result = await tags.ApplyAsync(captured.EventId.ToString(), ["inbox", "idea"], [], Ct);
        Assert.True(result.Item.IsEvent);
        Assert.Equal(2, result.AddedCount);

        await using var connection = await OpenAsync();
        var count = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM bsk.item_tag WHERE event_id = @id;",
            new { id = captured.EventId }, cancellationToken: Ct));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Tag_universe_counts_items_carrying_each_tag()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var tags = provider.GetRequiredService<TagService>();
        var uniq = Guid.NewGuid().ToString("N")[..8];
        var tag = $"universe-{uniq}";

        var a = await subjects.CreateAsync(SubjectTypes.Task, $"u a {uniq}", cancellationToken: Ct);
        var b = await subjects.CreateAsync(SubjectTypes.Task, $"u b {uniq}", cancellationToken: Ct);
        await tags.ApplyAsync(a.Urn, [tag], [], Ct);
        await tags.ApplyAsync(b.Urn, [tag], [], Ct);

        await using var connection = await OpenAsync();
        var itemCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT item_count FROM bsk.v_tag_universe WHERE tag = @tag;",
            new { tag }, cancellationToken: Ct));
        Assert.Equal(2, itemCount);

        // Once no item carries it, a tag leaves the universe entirely.
        await tags.ApplyAsync(a.Urn, [], [tag], Ct);
        await tags.ApplyAsync(b.Urn, [], [tag], Ct);
        var afterRemoval = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT item_count FROM bsk.v_tag_universe WHERE tag = @tag;",
            new { tag }, cancellationToken: Ct));
        Assert.Null(afterRemoval);
    }

    [Fact]
    public async Task Applying_no_tags_is_rejected()
    {
        var provider = Provider();
        var subjects = provider.GetRequiredService<SubjectService>();
        var tags = provider.GetRequiredService<TagService>();
        var subject = await subjects.CreateAsync(
            SubjectTypes.Idea, $"empty tag {Guid.NewGuid():N}", cancellationToken: Ct);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await tags.ApplyAsync(subject.Urn, ["   "], [], Ct));
    }

    [Fact]
    public void Domain_normalization_folds_case_and_dedupes()
    {
        Assert.Equal("trading", Tags.Normalize(" Trading "));
        Assert.Null(Tags.Normalize("   "));
        Assert.Equal(["trading", "health"], Tags.NormalizeAll(["Trading", "trading", " HEALTH "]));
    }
}
