using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// The web keeps TypeScript copies of some kernel rules, to show a day's XP or a level without a
/// round trip. <c>tests/rules/*.json</c> holds cases for them, each a rule, its arguments and the
/// kernel's answer; this checks the C# rules give those answers, and
/// <c>web/src/lib/rule-parity.test.ts</c> checks the TypeScript copies give the same (code review
/// CR8). After changing a rule on purpose, run with <c>LIFEOS_WRITE_RULES=1</c> to write the
/// kernel's new answers, and change the TypeScript until its test passes.
/// </summary>
public sealed class RuleParityTests
{
    /// <summary>Set to 1 to write the kernel's answers into the fixtures instead of checking them.</summary>
    public const string WriteVariable = "LIFEOS_WRITE_RULES";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> Files => new(["activity", "home", "reviews", "levels", "diet", "statuses"]);

    [Theory]
    [MemberData(nameof(Files))]
    public async Task The_kernel_gives_each_shared_case_its_answer(string file)
    {
        var path = Path.Combine(RepoRoot.Path, "tests", "rules", $"{file}.json");
        var document = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))!;
        var writing = Environment.GetEnvironmentVariable(WriteVariable) == "1";

        var wrong = new List<string>();
        foreach (var testCase in document["cases"]!.AsArray())
        {
            var rule = testCase!["rule"]!.GetValue<string>();
            var args = testCase["args"]!.AsArray();
            var actual = Answer(file, rule, args);
            if (!JsonNode.DeepEquals(actual, testCase["out"]))
            {
                wrong.Add($"{rule}({args.ToJsonString()}) is {actual?.ToJsonString() ?? "null"}, not {testCase["out"]?.ToJsonString() ?? "null"}");
                testCase["out"] = actual;
            }
        }

        if (writing)
        {
            // Only a file whose answers moved is written, so the rest keep their layout and don't
            // show as changed.
            if (wrong.Count > 0)
            {
                var text = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).Replace("\r\n", "\n") + "\n";
                await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), TestContext.Current.CancellationToken);
            }

            return;
        }

        Assert.True(wrong.Count == 0, $"tests/rules/{file}.json:\n" + string.Join("\n", wrong));
    }

    /// <summary>The kernel's answer to one case, as JSON; a rule that refuses answers <c>{"throws": true}</c>.</summary>
    private static JsonNode? Answer(string file, string rule, JsonArray args)
    {
        try
        {
            return JsonSerializer.SerializeToNode(Compute(file, rule, args), Json);
        }
        catch (ArgumentException)
        {
            return new JsonObject { ["throws"] = true };
        }
    }

    private static object? Compute(string file, string rule, JsonArray a) => (file, rule) switch
    {
        ("activity", "stepsXp") => ActivityRules.Default.StepsXp(Int(a[0])),
        ("activity", "sleepXp") => ActivityRules.Default.SleepXp(Int(a[0])),
        ("activity", "exerciseXp") => ActivityRules.Default.ExerciseXp(Long(a[0])),
        ("activity", "exerciseVerdict") => ExerciseRules.Counts(Text(a[0]), Text(a[1])),

        ("home", "points") => HomeRules.Points(Decimal(a[0])),
        ("home", "fade") => HomeRules.Fade(Text(a[0])!, Int(a[1]), Int(a[2])),
        ("home", "partValue") => HomeRules.Value(Text(a[0])!, Decimal(a[1]), Int(a[2]), Int(a[3])),
        ("home", "spaceScore") => HomeRules.SpaceScore(Long(a[0]), Long(a[1]), Long(a[2])),
        ("home", "partDrag") => HomeRules.Drag(Int(a[0]), Text(a[1])!, Long(a[2]), Long(a[3])),

        ("reviews", "period") => Period(Text(a[0])!, DateOnly.Parse(Text(a[1])!, System.Globalization.CultureInfo.InvariantCulture)),

        ("levels", "totalFor") => Curve(Text(a[0])!).TotalFor(Int(a[1])),
        ("levels", "levelFor") => Curve(Text(a[0])!).LevelFor(Long(a[1])),

        ("diet", "isScore") => FoodEntries.IsScore(Decimal(a[0])),
        ("diet", "caloriesPart") => DietRules.Default.CaloriesPartFor(Long(a[0])),
        ("diet", "fasting") => DietRules.Default.FastingFor(Long(a[0])),
        ("diet", "drinkFactor") => DietRules.Default.DrinkFactorFor(Int(a[0])),
        ("diet", "score") => Diet(a),
        ("diet", "mealType") => FoodEntries.GuessMealType(new TimeOnly(Int(a[0]), 30)),

        ("statuses", "statuses") => StatusVocabulary.For(Text(a[0])!),
        ("statuses", "initial") => StatusVocabulary.InitialStatusFor(Text(a[0])!) ?? StatusVocabulary.For(Text(a[0])!).FirstOrDefault() ?? "",
        ("statuses", "terminal") => StatusVocabulary.IsTerminal(Text(a[0])),

        _ => throw new InvalidOperationException($"tests/rules/{file}.json names a rule this test doesn't know: {rule}."),
    };

    private static object Period(string kind, DateOnly day)
    {
        var period = ReviewPeriods.PeriodFor(kind, day);
        var (start, end) = ReviewPeriods.LooksBack(kind, period);
        var (planStart, planEnd) = ReviewPeriods.Plans(kind, period);
        return new { date = period, start, end, planStart, planEnd };
    }

    /// <summary>A day's awards, from its calories, Σ score × calories, longest fast (or null) and drinks.</summary>
    private static object Diet(JsonArray a)
    {
        var score = DietRules.Default.Score(new DietDayFacts(
            Long(a[0]), Decimal(a[1]), a[2] is null ? null : Long(a[2]), Int(a[3])));
        return new { foodAward = score.FoodAward, fastAward = score.FastAward, fastAwardUncut = score.FastAwardUncut, total = score.Total };
    }

    private static ILevelCurve Curve(string domain) => domain switch
    {
        "health" => LifeDomains.Health.Curve,
        "environment" => LifeDomains.Environment.Curve,
        _ => throw new InvalidOperationException($"No domain {domain}."),
    };

    private static int Int(JsonNode? node) => node!.GetValue<int>();

    private static long Long(JsonNode? node) => node!.GetValue<long>();

    private static decimal Decimal(JsonNode? node) => node!.GetValue<decimal>();

    private static string? Text(JsonNode? node) => node?.GetValue<string>();
}
