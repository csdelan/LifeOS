using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Cli;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// <c>bsk --json</c> prints what <c>/api</c> answers for the same read or write (code review CR89):
/// each pair below runs the real bsk command tree in-process and the real API host on one
/// database. A read must answer every property the endpoint does, with the same value (bsk may
/// say more beside it); a write, done once through each door on different items, every property
/// in the same shape. A verb whose JSON the API also answers goes in one of the lists, and a
/// shape that drifts fails here before an agent or script reading bsk finds it.
/// <para>
/// The database is in Pacific time, the home zone <c>bsk</c> takes when nothing names another, so
/// its kernel and the API's agree on the day.
/// </para>
/// </summary>
public sealed class BskJsonTests(PostgresFixture postgres)
{
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Each_write_prints_the_shape_the_endpoint_answers()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct, HomeZone.Default);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct);
        var writes = await SeedAsync(db, host);

        var problems = new List<string>();
        foreach (var (name, bsk, api) in writes)
        {
            SameShape(api, bsk, name, problems);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task Each_read_prints_what_the_endpoint_answers()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct, HomeZone.Default);
        await using var host = await ApiTestHost.StartAsync(db.ConnectionString, Ct);
        await SeedAsync(db, host);
        var today = await db.TodayAsync();
        var from = Iso(today.AddDays(-6));
        var to = Iso(today);

        // bsk's arguments, the endpoint, and where in bsk's output the endpoint's answer is.
        (string[] Bsk, string Api, string? At)[] reads =
        [
            (["wallet"], "/api/wallet", null),
            (["ledger"], "/api/ledger", null),
            (["ledger", "--daily", "--tz", HomeZone.Default, "--from", from, "--to", to],
                $"/api/ledger/daily?from={from}&to={Iso(today.AddDays(1))}&tz={HomeZone.Default}", "days"),
            (["rewards", "list"], "/api/rewards", null),
            (["domains"], "/api/domains", null),
            (["domains", "health", "--days", "30"], "/api/domains/health/days?days=30", null),
            (["domains", "exceptions"], "/api/domains/exceptions", null),
            (["food"], "/api/food/day", null),
            (["food", "--week"], $"/api/food/days?from={Iso(StartOfWeek(today))}&to={Iso(StartOfWeek(today).AddDays(6))}", null),
            (["food", "drinks"], "/api/food/drinks", null),
            (["space", "list", "--archived"], "/api/environment/spaces", "spaces"),
            (["rate", "--list"], "/api/environment/ratings", null),
            (["home"], "/api/environment", null),
            (["metric", "list", "--archived"], "/api/measurements/metrics", "metrics"),
            (["metric", "show", "Weight"], "/api/measurements/metrics/Weight", null),
            (["lab", "list"], "/api/measurements/visits", null),
            (["review", "list", "--from", from, "--to", to], $"/api/reviews?from={from}&to={to}", null),
            (["oura", "steps", "--days", "7"], $"/api/activity/days?from={from}&to={to}", null),
            (["oura", "workouts", "--days", "7", "--all"], $"/api/activity/workouts?from={from}&to={to}", null),
            (["oura", "tags", "--days", "7"], $"/api/activity/tags?from={from}&to={to}", null),
            (["oura", "status"], "/api/oura/status", null),
        ];

        var problems = new List<string>();
        foreach (var (args, path, at) in reads)
        {
            var bsk = await BskAsync(db, args);
            var api = JsonNode.Parse(await host.Client.GetStringAsync(path, Ct));
            Within(api, at is null ? bsk : bsk?[at], $"bsk {string.Join(' ', args)} ↔ GET {path}", problems);
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// The Pilot reads bsk's JSON for a new item, a journal entry and a timer (its copies are in
    /// <c>src/LifeOs.Pilot/Cli/BskJson.cs</c>, compiled in here): every property it reads is in what
    /// bsk prints, and what bsk prints reads into it as the Pilot reads it.
    /// </summary>
    [Fact]
    public async Task The_pilot_finds_what_it_reads_in_bsks_json()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct, HomeZone.Default);
        var draft = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(draft, "A note from the Pilot.", Ct);
            var first = await BskAsync(db, ["new", "task", "Pilot task"]);
            var second = await BskAsync(db, ["new", "task", "Pilot task, too"]);
            var firstUrn = first!["urn"]!.GetValue<string>();
            var secondUrn = second!["urn"]!.GetValue<string>();
            var journal = await BskAsync(db, ["journal", firstUrn, "--file", draft]);
            await BskAsync(db, ["timer", "start", firstUrn]);
            var switched = await BskAsync(db, ["timer", "start", secondUrn]);
            var completed = await BskAsync(db, ["timer", "complete", secondUrn]);

            var problems = new List<string>();
            Reads(typeof(LifeOs.Pilot.Cli.CreatedSubject), first, "bsk new", problems);
            Reads(typeof(LifeOs.Pilot.Cli.JournalCapture), journal, "bsk journal", problems);
            Reads(typeof(LifeOs.Pilot.Cli.TimerResultJson), switched, "bsk timer start", problems);
            Reads(typeof(LifeOs.Pilot.Cli.TimerResultJson), completed, "bsk timer complete", problems);
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

            var paused = switched.Deserialize<LifeOs.Pilot.Cli.TimerResultJson>(JsonSerializerOptions.Web)!;
            Assert.Equal([firstUrn], paused.Paused.Select(p => p.Urn));
            Assert.True(completed.Deserialize<LifeOs.Pilot.Cli.TimerResultJson>(JsonSerializerOptions.Web)!.Completed);
        }
        finally
        {
            File.Delete(draft);
        }
    }

    /// <summary>Each property of <paramref name="type"/> is in <paramref name="node"/>, and the node reads into it.</summary>
    private static void Reads(Type type, JsonNode? node, string path, List<string> problems)
    {
        if (node is not JsonObject json)
        {
            problems.Add($"{path}: bsk prints {node?.GetValueKind().ToString() ?? "nothing"}, not an object.");
            return;
        }

        foreach (var property in type.GetProperties())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!json.ContainsKey(name))
            {
                problems.Add($"{path}.{name}: bsk doesn't print it.");
            }
            else if (property.PropertyType.IsGenericType && json[name] is JsonArray { Count: > 0 } items)
            {
                Reads(property.PropertyType.GetGenericArguments()[0], items[0], $"{path}.{name}[0]", problems);
            }
        }

        try
        {
            node.Deserialize(type, JsonSerializerOptions.Web);
        }
        catch (JsonException ex)
        {
            problems.Add($"{path}: doesn't read as the Pilot's {type.Name}: {ex.Message}");
        }
    }

    private static DateOnly StartOfWeek(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>
    /// Writes through both doors, each on items of its own, and answers each pair of answers. What
    /// they write is what the reads above read: a Task done, a habit followed, a reward bought, an
    /// exception day, a meal, a space rated, metrics read, a lab visit and two reviews.
    /// </summary>
    private async Task<List<(string Name, JsonNode? Bsk, JsonNode? Api)>> SeedAsync(DomainTestDatabase db, ApiTestHost host)
    {
        var today = await db.TodayAsync();
        await db.OpenWalletAsync(daysAgo: 30);
        await db.CreditDaysAsync(today, fromAgo: 10, toAgo: 1, xp: 20);
        var price = new Dictionary<string, long> { [Currencies.Coins] = 5, [Currencies.HealthXp] = 10 };
        var bskHabit = await db.HabitAsync(today, startOffset: 10, price);
        var apiHabit = await db.HabitAsync(today, startOffset: 10, price);
        var writes = new List<(string, JsonNode?, JsonNode?)>();

        async Task<JsonNode?> Api(string path, object body)
        {
            var response = await host.Client.PostAsJsonAsync(path, body, Ct);
            var text = await response.Content.ReadAsStringAsync(Ct);
            Assert.True(response.IsSuccessStatusCode, $"POST {path} → {(int)response.StatusCode}: {text}");
            return JsonNode.Parse(text);
        }

        async Task<(JsonNode? Bsk, JsonNode? Api)> Pair(string name, string[] bsk, string path, object body)
        {
            var pair = (await BskAsync(db, bsk), await Api(path, body));
            writes.Add(($"bsk {string.Join(' ', bsk)} ↔ POST {path} ({name})", pair.Item1, pair.Item2));
            return pair;
        }

        var (bskTask, apiTask) = await Pair("new", ["new", "task", "Bsk task"], "/api/subjects", new { type = SubjectTypes.Task, title = "Api task" });
        var bskUrn = bskTask!["urn"]!.GetValue<string>();
        var apiUrn = apiTask!["urn"]!.GetValue<string>();
        await Pair("rename", ["rename", bskUrn, "Bsk task, renamed"], "/api/rename", new { subject = apiUrn, title = "Api task, renamed" });
        await Pair("bounty", ["bounty", bskUrn, "coins=3"], "/api/bounty", new { subject = apiUrn, amounts = new { coins = 3 } });
        await Pair("status", ["status", bskUrn, "Completed"], "/api/status", new { subject = apiUrn, status = "Completed" });
        await Pair("adhere", ["adhere", bskHabit.Urn, "followed"], "/api/adhere", new { habit = apiHabit.Urn, state = "followed" });
        await Pair("habit target", ["habit", "target", bskHabit.Urn, "--adherence", "80%"], "/api/habits/target", new { habit = apiHabit.Urn, minAdherence = 0.8m });

        var (bskChore, apiChore) = (await BskAsync(db, ["new", "task", "Bsk chore"]), await Api("/api/subjects", new { type = SubjectTypes.Task, title = "Api chore" }));
        await Pair("recur", ["recur", bskChore!["urn"]!.GetValue<string>(), "--freq", "daily"], "/api/recur",
            new { subject = apiChore!["urn"]!.GetValue<string>(), recurrence = new { freq = "daily" } });
        await Pair("tasks settle", ["tasks", "settle"], "/api/tasks/settle", new { });

        await Pair("reward", ["rewards", "add", "Bsk treat", "coins=1"], "/api/rewards", new { title = "Api treat", price = new { coins = 1 } });
        await Pair("redeem", ["redeem", "Bsk treat"], "/api/redeem", new { reward = "Api treat" });
        await Pair("exception day", ["domains", "exception", Iso(today.AddDays(-2)), "Bsk sick"], "/api/domains/exceptions",
            new { day = Iso(today.AddDays(-3)), comment = "Api sick" });

        await Pair("meal", ["food", "log", "Bsk salad", "--score", "4", "--cal", "300"], "/api/food/meals",
            new { name = "Api salad", score = 4, calories = 300 });

        await Pair("space", ["space", "add", "Kitchen"], "/api/environment/spaces", new { name = "Office" });
        await Pair("rating", ["rate", "Kitchen", "4", "3.5", "4.5"], "/api/environment/ratings",
            new { ratings = new[] { new { space = "Office", tidiness = 4, cleanliness = 3.5m, aesthetics = 4.5m } } });

        await Pair("metric", ["metric", "add", "Weight"], "/api/measurements/metrics", new { name = "Waist" });
        await Pair("reading", ["measure", "Weight", "192.4"], "/api/measurements/readings", new { metric = "Waist", value = 34.5m });
        await Pair("lab visit", ["lab", "add", "--lab", "Quest", "Glucose=104@70..100"], "/api/measurements/visits",
            new { collectedAt = DateTimeOffset.UtcNow, lab = "Labcorp", results = new[] { new { metric = "LDL", value = 131m, high = 99m } } });

        await Pair("review", ["review", "open", "daily"], "/api/reviews/open", new { kind = "daily", date = Iso(today.AddDays(-1)) });

        // A Person's photo goes up as a file to bsk and as multipart to the API.
        var bskFriend = (await BskAsync(db, ["new", "person", "Bsk friend"]))!["urn"]!.GetValue<string>();
        var apiFriend = (await Api("/api/subjects", new { type = SubjectTypes.Person, title = "Api friend" }))!["id"]!.GetValue<string>();
        var picture = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(picture, FoodPhotoTests.Jpeg(), Ct);
            using var form = new MultipartFormDataContent { { new ByteArrayContent(FoodPhotoTests.Jpeg()), "photo", "friend.jpg" } };
            var upload = await host.Client.PostAsync($"/api/people/{apiFriend}/photo", form, Ct);
            var uploaded = await upload.Content.ReadAsStringAsync(Ct);
            Assert.True(upload.IsSuccessStatusCode, uploaded);
            writes.Add(("bsk person photo --file ↔ POST /api/people/{id}/photo (person photo)",
                await BskAsync(db, ["person", "photo", bskFriend, "--file", picture]), JsonNode.Parse(uploaded)));
        }
        finally
        {
            File.Delete(picture);
        }

        await Pair("person photo --remove", ["person", "photo", bskFriend, "--remove"], $"/api/people/{apiFriend}/photo/delete", new { });
        return writes;
    }

    /// <summary>Runs <c>bsk --json</c> on the database, and answers what it printed.</summary>
    private async Task<JsonNode?> BskAsync(DomainTestDatabase db, string[] args)
    {
        var bsk = BskRoot.Create();
        var (exit, output, error) = await ConsoleCapture.RunAsync(() => bsk.Command
            .Parse([.. args, "--json", "--connection", db.ConnectionString])
            .InvokeAsync(cancellationToken: Ct));
        Assert.True(exit == 0, $"bsk {string.Join(' ', args)} → {exit}: {error}{output}");
        return JsonNode.Parse(output);
    }

    /// <summary>Every property <paramref name="api"/> has, <paramref name="bsk"/> has with the same value.</summary>
    private static void Within(JsonNode? api, JsonNode? bsk, string path, List<string> problems)
    {
        switch (api)
        {
            case JsonObject apiObject when bsk is JsonObject bskObject:
                foreach (var (name, value) in apiObject)
                {
                    if (!bskObject.ContainsKey(name))
                    {
                        problems.Add($"{path}.{name}: bsk doesn't print it.");
                        continue;
                    }

                    Within(value, bskObject[name], $"{path}.{name}", problems);
                }

                return;
            case JsonArray apiArray when bsk is JsonArray bskArray:
                if (apiArray.Count != bskArray.Count)
                {
                    problems.Add($"{path}: the API answers {apiArray.Count}, bsk prints {bskArray.Count}.");
                    return;
                }

                for (var i = 0; i < apiArray.Count; i++)
                {
                    Within(apiArray[i], bskArray[i], $"{path}[{i}]", problems);
                }

                return;
            case JsonObject or JsonArray:
                problems.Add($"{path}: the API answers {api.GetValueKind()}, bsk prints {bsk?.GetValueKind().ToString() ?? "nothing"}.");
                return;
            default:
                if (!JsonNode.DeepEquals(api, bsk))
                {
                    problems.Add($"{path}: the API answers {api?.ToJsonString() ?? "null"}, bsk prints {bsk?.ToJsonString() ?? "null"}.");
                }

                return;
        }
    }

    /// <summary>
    /// Every property <paramref name="api"/> has, <paramref name="bsk"/> has in the same kind
    /// (object, array, string, number or true/false); a null on either side says nothing.
    /// </summary>
    private static void SameShape(JsonNode? api, JsonNode? bsk, string path, List<string> problems)
    {
        if (api is null || bsk is null)
        {
            return;
        }

        switch (api)
        {
            case JsonObject apiObject when bsk is JsonObject bskObject:
                foreach (var (name, value) in apiObject)
                {
                    if (!bskObject.ContainsKey(name))
                    {
                        problems.Add($"{path}.{name}: bsk doesn't print it.");
                        continue;
                    }

                    SameShape(value, bskObject[name], $"{path}.{name}", problems);
                }

                return;
            case JsonArray apiArray when bsk is JsonArray bskArray:
                if (apiArray.Count > 0 && bskArray.Count > 0)
                {
                    SameShape(apiArray[0], bskArray[0], $"{path}[0]", problems);
                }

                return;
            default:
                if (Kind(api) != Kind(bsk))
                {
                    problems.Add($"{path}: the API answers {Kind(api)}, bsk prints {Kind(bsk)}.");
                }

                return;
        }
    }

    private static string Kind(JsonNode node) => node.GetValueKind() switch
    {
        JsonValueKind.True or JsonValueKind.False => "true/false",
        var kind => kind.ToString()
    };
}
