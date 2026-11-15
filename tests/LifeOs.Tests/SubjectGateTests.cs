using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Capture;
using LifeOs.Application.Subjects;
using LifeOs.Cli;
using LifeOs.Domain;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR125 (fifth review, choice D): a Goal can't be Active without a target date
/// (GEN-8), and a Habit serves at least one Goal or Identity Statement (GEN-3). They were the
/// screens', so <c>bsk</c> and agents skipped them; the services every door shares now refuse
/// them, in the gate's words, with nothing written. Only the change that breaks a gate is refused,
/// so an item made before it is left alone.
/// </summary>
public sealed class SubjectGateTests(ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Unique(string name) => $"{name} {Guid.NewGuid():N}";

    private ServiceProvider Kernel() => classDb.Kernel();

    [Fact]
    public async Task A_Goal_becomes_Active_only_with_a_target_date_from_every_door()
    {
        await using var kernel = Kernel();
        var subjects = kernel.GetRequiredService<SubjectService>();
        var status = kernel.GetRequiredService<StatusService>();
        var viaService = await subjects.CreateAsync(SubjectTypes.Goal, Unique("service goal"), cancellationToken: Ct);
        var viaBsk = await subjects.CreateAsync(SubjectTypes.Goal, Unique("bsk goal"), cancellationToken: Ct);
        var viaApi = await subjects.CreateAsync(SubjectTypes.Goal, Unique("api goal"), cancellationToken: Ct);
        await status.ChangeStatusAsync(viaBsk.Urn, "New", Ct);

        var refused = await Assert.ThrowsAsync<SubjectGateException>(() => status.ChangeStatusAsync(viaService.Urn, "active", Ct));
        Assert.Equal(SubjectGates.GoalNeedsTargetDate, refused.Message);

        var (exit, _, error) = await BskAsync(["status", viaBsk.Urn, "Active"]);
        Assert.Equal(1, exit);
        Assert.Contains(SubjectGates.GoalNeedsTargetDate, error, StringComparison.Ordinal);

        await using var host = await ApiTestHost.StartAsync(classDb.ConnectionString, Ct);
        var answer = await host.Client.PostAsJsonAsync("/api/status", new { subject = viaApi.Id.ToString(), status = "Active" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Equal(SubjectGates.GoalNeedsTargetDate, (await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());

        // Nothing was written: no Goal moved, and bsk's New is still the newest.
        var repository = kernel.GetRequiredService<ISubjectRepository>();
        Assert.Null(await repository.GetStatusAsync(viaService.Id, Ct));
        Assert.Equal("New", await repository.GetStatusAsync(viaBsk.Id, Ct));
        Assert.Null(await repository.GetStatusAsync(viaApi.Id, Ct));

        // With a target date, each door makes it Active.
        foreach (var goal in new[] { viaService, viaBsk, viaApi })
        {
            await kernel.GetRequiredService<AttributeService>().SetAsync(goal.Urn, [new("target_date", "2027-06-30")], Ct);
        }

        await status.ChangeStatusAsync(viaService.Urn, "Active", Ct);
        Assert.Equal(0, (await BskAsync(["status", viaBsk.Urn, "Active"])).Exit);
        var made = await host.Client.PostAsJsonAsync("/api/status", new { subject = viaApi.Id.ToString(), status = "Active" }, Ct);
        Assert.True(made.IsSuccessStatusCode, await made.Content.ReadAsStringAsync(Ct));
        foreach (var goal in new[] { viaService, viaBsk, viaApi })
        {
            Assert.Equal("Active", await repository.GetStatusAsync(goal.Id, Ct));
        }
    }

    [Fact]
    public async Task An_Active_Goal_keeps_its_target_date_until_it_is_no_longer_Active()
    {
        await using var kernel = Kernel();
        var attributes = kernel.GetRequiredService<AttributeService>();
        var goal = await kernel.GetRequiredService<SubjectService>().CreateAsync(
            SubjectTypes.Goal, Unique("dated goal"), """{"target_date": "2027-01-31"}""", cancellationToken: Ct);
        await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(goal.Urn, "Active", Ct);

        var refused = await Assert.ThrowsAsync<SubjectGateException>(() =>
            attributes.SetAsync(goal.Urn, [new("target_date", " ")], Ct));
        Assert.Equal(SubjectGates.GoalNeedsTargetDate, refused.Message);
        var (exit, _, error) = await BskAsync(["set", goal.Urn, "target_date="]);
        Assert.Equal(1, exit);
        Assert.Contains(SubjectGates.GoalNeedsTargetDate, error, StringComparison.Ordinal);
        await using (var host = await ApiTestHost.StartAsync(classDb.ConnectionString, Ct))
        {
            var answer = await host.Client.PostAsJsonAsync(
                "/api/attributes", new { subject = goal.Id.ToString(), attrs = new Dictionary<string, string> { ["target_date"] = "", ["motivation"] = "Why" } }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        }

        var repository = kernel.GetRequiredService<ISubjectRepository>();
        Assert.Equal("2027-01-31", await repository.GetAttributeValueAsync(goal.Id, "target_date", Ct));
        Assert.Null(await repository.GetAttributeValueAsync(goal.Id, "motivation", Ct));

        // Another field still changes, and once it's Completed the date can go.
        await attributes.SetAsync(goal.Urn, [new("motivation", "Why"), new("target_date", "2027-02-28")], Ct);
        await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(goal.Urn, "Completed", Ct);
        await attributes.SetAsync(goal.Urn, [new("target_date", null)], Ct);
        Assert.Null(await repository.GetAttributeValueAsync(goal.Id, "target_date", Ct));
    }

    [Fact]
    public async Task A_Goal_Active_before_the_gate_without_a_target_date_is_left_alone()
    {
        await using var kernel = Kernel();
        var goal = await kernel.GetRequiredService<SubjectService>().CreateAsync(SubjectTypes.Goal, Unique("old goal"), cancellationToken: Ct);
        await WriteStatusAsync(kernel, goal, "Active");

        // Its other fields, its status set again and a clear of the date it doesn't have all go through.
        await kernel.GetRequiredService<AttributeService>().SetAsync(
            goal.Urn, [new("motivation", "Still matters"), new("target_date", "")], Ct);
        await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(goal.Urn, "Active", Ct);

        // Once it has a date, it keeps it while Active.
        await kernel.GetRequiredService<AttributeService>().SetAsync(goal.Urn, [new("target_date", "2027-03-31")], Ct);
        await Assert.ThrowsAsync<SubjectGateException>(() =>
            kernel.GetRequiredService<AttributeService>().SetAsync(goal.Urn, [new("target_date", "")], Ct));
    }

    [Fact]
    public async Task A_Habit_is_made_only_serving_a_Goal_or_an_Identity_Statement_from_every_door()
    {
        await using var kernel = Kernel();
        var subjects = kernel.GetRequiredService<SubjectService>();
        var creation = kernel.GetRequiredService<SubjectCreationService>();
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, Unique("habit goal"), cancellationToken: Ct);
        var value = await subjects.CreateAsync(
            SubjectTypes.Value, Unique("habit value"), """{"statement": "I look after myself."}""", cancellationToken: Ct);
        var project = await subjects.CreateAsync(SubjectTypes.Project, Unique("habit project"), cancellationToken: Ct);
        var capture = await kernel.GetRequiredService<CaptureService>().CaptureNoteAsync(Unique("stretch every morning"), Ct);
        var title = Unique("orphan habit");

        var refusals = new List<Exception>
        {
            await Assert.ThrowsAsync<SubjectGateException>(() => subjects.CreateAsync(SubjectTypes.Habit, title, cancellationToken: Ct)),
            await Assert.ThrowsAsync<SubjectGateException>(() => creation.CreateAsync(SubjectTypes.Habit, title, "{}", cancellationToken: Ct)),
            await Assert.ThrowsAsync<SubjectGateException>(() => creation.CreateAsync(SubjectTypes.Habit, title, "{}", project.Urn, SubjectRelations.Serves, Ct)),
            await Assert.ThrowsAsync<SubjectGateException>(() => creation.CreateAsync(SubjectTypes.Habit, title, "{}", goal.Urn, SubjectRelations.Supersedes, Ct)),
            await Assert.ThrowsAsync<SubjectGateException>(() => kernel.GetRequiredService<PromotionService>().PromoteAsync(capture.EventId, SubjectTypes.Habit, title, Ct)),
        };
        Assert.All(refusals, refusal => Assert.Equal(SubjectGates.HabitNeedsParent, refusal.Message));

        var (exit, _, error) = await BskAsync(["new", "Habit", title]);
        Assert.Equal(1, exit);
        Assert.Contains(SubjectGates.HabitNeedsParent, error, StringComparison.Ordinal);

        await using var host = await ApiTestHost.StartAsync(classDb.ConnectionString, Ct);
        var answer = await host.Client.PostAsJsonAsync("/api/subjects", new { type = "Habit", title }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Equal(SubjectGates.HabitNeedsParent, (await answer.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("error").GetString());
        Assert.Equal(0, await CountAsync("SELECT count(*) FROM bsk.subject WHERE title = @title;", new { title }));

        // Serving a Goal or an Identity Statement, each door makes one, Planned as every new Habit is.
        var made = new List<Guid>
        {
            (await creation.CreateAsync(SubjectTypes.Habit, Unique("goal habit"), "{}", goal.Urn, SubjectRelations.Serves, Ct)).Subject.Id,
            (await creation.CreateAsync(SubjectTypes.Habit, Unique("value habit"), "{}", value.Urn, SubjectRelations.Serves, Ct)).Subject.Id,
        };
        var bsk = await BskAsync(["new", "Habit", Unique("bsk habit"), "--parent", goal.Urn, "--relation", "serves", "--json"]);
        Assert.True(bsk.Exit == 0, bsk.Error);
        made.Add(JsonDocument.Parse(bsk.Out).RootElement.GetProperty("id").GetGuid());
        var web = await host.Client.PostAsJsonAsync(
            "/api/subjects", new { type = "Habit", title = Unique("web habit"), parent = value.Id.ToString(), relation = "serves" }, Ct);
        Assert.True(web.IsSuccessStatusCode, await web.Content.ReadAsStringAsync(Ct));
        made.Add((await web.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid());

        var repository = kernel.GetRequiredService<ISubjectRepository>();
        foreach (var id in made)
        {
            Assert.Equal("Planned", await repository.GetStatusAsync(id, Ct));
            Assert.Equal(1, await CountAsync(
                "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @id AND relation = 'serves';", new { id }));
        }
    }

    [Fact]
    public async Task A_Habit_keeps_its_last_Goal_or_Identity_Statement_from_every_door()
    {
        await using var kernel = Kernel();
        var subjects = kernel.GetRequiredService<SubjectService>();
        var relations = kernel.GetRequiredService<RelationService>();
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, Unique("kept goal"), cancellationToken: Ct);
        var other = await subjects.CreateAsync(SubjectTypes.Goal, Unique("second goal"), cancellationToken: Ct);
        var project = await subjects.CreateAsync(SubjectTypes.Project, Unique("served project"), cancellationToken: Ct);
        var habit = (await kernel.GetRequiredService<SubjectCreationService>().CreateAsync(
            SubjectTypes.Habit, Unique("kept habit"), "{}", goal.Urn, SubjectRelations.Serves, Ct)).Subject;
        await relations.LinkAsync(habit.Urn, SubjectRelations.Serves, other.Urn, Ct);
        await relations.LinkAsync(habit.Urn, SubjectRelations.Serves, project.Urn, Ct);

        // One of two goes; the Project it serves doesn't count, so the last Goal stays.
        Assert.Equal(1, (await relations.UnlinkAsync(habit.Urn, SubjectRelations.Serves, other.Urn, Ct)).Removed);
        var refused = await Assert.ThrowsAsync<SubjectGateException>(() =>
            relations.UnlinkAsync(habit.Urn, SubjectRelations.Serves, goal.Urn, Ct));
        Assert.Equal(SubjectGates.HabitNeedsParent, refused.Message);

        var (exit, _, error) = await BskAsync(["unlink", habit.Urn, "serves", goal.Urn]);
        Assert.Equal(1, exit);
        Assert.Contains(SubjectGates.HabitNeedsParent, error, StringComparison.Ordinal);
        await using (var host = await ApiTestHost.StartAsync(classDb.ConnectionString, Ct))
        {
            var answer = await host.Client.PostAsJsonAsync(
                "/api/link", new { from = habit.Id.ToString(), relation = "serves", to = goal.Id.ToString(), remove = true }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        }

        // The Project's edge, which isn't a parent, still goes, and an edge that isn't there is a no-op.
        Assert.Equal(1, (await relations.UnlinkAsync(habit.Urn, SubjectRelations.Serves, project.Urn, Ct)).Removed);
        Assert.Equal(0, (await relations.UnlinkAsync(habit.Urn, SubjectRelations.Serves, other.Urn, Ct)).Removed);
        Assert.Equal(1, await CountAsync(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @id;", new { id = habit.Id }));
    }

    [Fact]
    public async Task Two_removals_at_once_leave_a_Habit_one_parent()
    {
        await using var kernel = Kernel();
        var subjects = kernel.GetRequiredService<SubjectService>();
        var relations = kernel.GetRequiredService<RelationService>();
        var goal = await subjects.CreateAsync(SubjectTypes.Goal, Unique("race goal"), cancellationToken: Ct);
        var value = await subjects.CreateAsync(
            SubjectTypes.Value, Unique("race value"), """{"statement": "I finish what I start."}""", cancellationToken: Ct);
        var habit = (await kernel.GetRequiredService<SubjectCreationService>().CreateAsync(
            SubjectTypes.Habit, Unique("race habit"), "{}", goal.Urn, SubjectRelations.Serves, Ct)).Subject;
        await relations.LinkAsync(habit.Urn, SubjectRelations.Serves, value.Urn, Ct);

        var outcomes = await Task.WhenAll(
            Outcome(relations.UnlinkAsync(habit.Urn, SubjectRelations.Serves, goal.Urn, Ct)),
            Outcome(relations.UnlinkAsync(habit.Urn, SubjectRelations.Serves, value.Urn, Ct)));

        Assert.Equal(1, outcomes.Count(refused => refused));
        Assert.Equal(1, await CountAsync(
            "SELECT count(*) FROM bsk.subject_relation WHERE from_subject = @id;", new { id = habit.Id }));

        static async Task<bool> Outcome(Task removal)
        {
            try
            {
                await removal;
                return false;
            }
            catch (SubjectGateException)
            {
                return true;
            }
        }
    }

    [Fact]
    public async Task A_Habit_made_before_the_gate_with_no_parent_is_left_alone()
    {
        await using var kernel = Kernel();
        var habit = await kernel.CreateTrackedHabitAsync(Unique("old habit"), cancellationToken: Ct);
        var project = await kernel.GetRequiredService<SubjectService>().CreateAsync(SubjectTypes.Project, Unique("old project"), cancellationToken: Ct);
        var relations = kernel.GetRequiredService<RelationService>();

        await kernel.GetRequiredService<AttributeService>().SetAsync(habit.Urn, [new("cue", "After coffee")], Ct);
        await kernel.GetRequiredService<StatusService>().ChangeStatusAsync(habit.Urn, "Building", Ct);
        await relations.LinkAsync(habit.Urn, SubjectRelations.Serves, project.Urn, Ct);
        Assert.Equal(1, (await relations.UnlinkAsync(habit.Urn, SubjectRelations.Serves, project.Urn, Ct)).Removed);
    }

    /// <summary>Appends a status as an event straight to the log, as a Goal made before the gate had it.</summary>
    private static Task WriteStatusAsync(ServiceProvider kernel, SubjectRef subject, string status)
        => kernel.GetRequiredService<IEventStore>().AppendAsync(new NewEvent(
            EventKinds.StateChange, Provenances.Declared, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            kernel.GetRequiredService<EventSource>().Id,
            PayloadJson: JsonSerializer.Serialize(new { subject_id = subject.Id.ToString(), status })), Ct);

    private async Task<(int Exit, string Out, string Error)> BskAsync(string[] args)
        => await ConsoleCapture.RunAsync(() => BskRoot.Create().Command
            .Parse([.. args, "--connection", classDb.ConnectionString])
            .InvokeAsync(cancellationToken: Ct));

    private async Task<long> CountAsync(string sql, object args)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(sql, args, cancellationToken: Ct));
    }
}
