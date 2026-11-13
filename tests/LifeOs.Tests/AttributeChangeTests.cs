using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// A rename or an attribute change appends an <c>attribute_change</c> event in the same
/// transaction, linked to the subject, with what changed, from and to (code review CR24,
/// migration 0057). A key that was added has no "from"; one that was removed has no "to". A write
/// that changes nothing appends nothing, and the subject reads as it did before.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AttributeChangeTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The subject's attribute_change payloads, as JSON text.</summary>
    private static async Task<List<string>> ChangesAsync(DomainTestDatabase db, Guid subjectId)
    {
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT e.payload::text
            FROM bsk.event e
            JOIN bsk.subject_event se ON se.event_id = e.id
            WHERE e.kind = 'attribute_change' AND se.subject_id = @subjectId AND se.relation = 'concerns'
              AND e.provenance = 'declared';
            """,
            new { subjectId }, cancellationToken: Ct));
        return rows.ToList();
    }

    /// <summary>Runs <paramref name="act"/> and answers the one attribute_change it appended, or null for none.</summary>
    private static async Task<JsonElement?> AppendedAsync(DomainTestDatabase db, Guid subjectId, Func<Task> act)
    {
        var before = await ChangesAsync(db, subjectId);
        await act();
        var added = (await ChangesAsync(db, subjectId)).Except(before).ToList();
        Assert.True(added.Count <= 1, $"{added.Count} attribute_change events for one write");
        return added.Count == 0 ? null : JsonDocument.Parse(added[0]).RootElement.Clone();
    }

    [Fact]
    public async Task A_rename_records_the_title_from_and_to_and_the_same_title_records_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var habit = await db.HabitAsync(await db.TodayAsync(), startOffset: 3, healthXp: 5);
        var title = (await db.Get<ISubjectRepository>().FindByUrnAsync(habit.Urn, Ct))!.Title;
        var rename = db.Get<RenameService>();

        var renamed = await AppendedAsync(db, habit.Id, () => rename.RenameAsync(habit.Urn, "Run 5k", Ct));

        var change = Assert.IsType<JsonElement>(renamed);
        Assert.Equal(habit.Id.ToString(), change.GetProperty("subject_id").GetString());
        Assert.Equal(title, change.GetProperty("title").GetProperty("from").GetString());
        Assert.Equal("Run 5k", change.GetProperty("title").GetProperty("to").GetString());
        Assert.Equal("Run 5k", (await db.Get<ISubjectRepository>().FindByUrnAsync(habit.Urn, Ct))!.Title);

        Assert.Null(await AppendedAsync(db, habit.Id, () => rename.RenameAsync(habit.Urn, "Run 5k", Ct)));
    }

    [Fact]
    public async Task An_attribute_change_records_only_the_keys_that_changed()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var habit = await db.HabitAsync(await db.TodayAsync(), startOffset: 3, healthXp: 5);
        var repository = db.Get<ISubjectRepository>();

        var added = await AppendedAsync(db, habit.Id, () => repository.UpdateAttributesAsync(
            habit.Id, """{"cue": "after coffee", "start": "2020-01-01"}""", [], Ct));
        var attributes = Assert.IsType<JsonElement>(added).GetProperty("attributes");
        // The cue is new: no "from". The start was set when the habit was made: from and to.
        Assert.False(attributes.GetProperty("cue").TryGetProperty("from", out _));
        Assert.Equal("after coffee", attributes.GetProperty("cue").GetProperty("to").GetString());
        Assert.Equal("2020-01-01", attributes.GetProperty("start").GetProperty("to").GetString());
        Assert.NotEqual("2020-01-01", attributes.GetProperty("start").GetProperty("from").GetString());

        // The same values again change nothing, so nothing is recorded.
        Assert.Null(await AppendedAsync(db, habit.Id, () => repository.UpdateAttributesAsync(
            habit.Id, """{"cue": "after coffee"}""", [], Ct)));

        // A removed key keeps its "from" and has no "to"; a key that was never there is no change.
        var removed = await AppendedAsync(db, habit.Id, () => repository.UpdateAttributesAsync(
            habit.Id, "{}", ["cue", "never_set"], Ct));
        var gone = Assert.IsType<JsonElement>(removed).GetProperty("attributes");
        Assert.Equal(["cue"], gone.EnumerateObject().Select(p => p.Name));
        Assert.Equal("after coffee", gone.GetProperty("cue").GetProperty("from").GetString());
        Assert.False(gone.GetProperty("cue").TryGetProperty("to", out _));

        Assert.Null(await repository.GetAttributeValueAsync(habit.Id, "cue", Ct));
        Assert.Equal("2020-01-01", await repository.GetAttributeValueAsync(habit.Id, "start", Ct));
    }

    [Fact]
    public async Task A_subject_that_isnt_there_changes_nothing()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        var repository = db.Get<ISubjectRepository>();

        Assert.False(await repository.RenameAsync(Guid.NewGuid(), "Nobody", Ct));
        Assert.False(await repository.UpdateAttributesAsync(Guid.NewGuid(), """{"a": 1}""", [], Ct));
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        Assert.Equal(0, await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.event WHERE kind = @kind;", new { kind = EventKinds.AttributeChange },
            cancellationToken: Ct)));
    }
}
