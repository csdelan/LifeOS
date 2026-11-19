using System.Text.RegularExpressions;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR153: an index built on a <c>bsk</c> function (today <c>event_record_idx</c>, on
/// <c>bsk.event_record</c>) holds what the function answered when each row was indexed. A migration
/// that replaces the function without rebuilding the index leaves old rows filed under the old
/// answer, so <c>recent_changes</c> misses their newer versions and shows superseded ones, and no
/// test on an empty template would notice. AGENTS.md asked for the <c>REINDEX</c>; this holds every
/// migration to it, for every index on a function, not only the one it was written for.
/// </summary>
public sealed partial class IndexedFunctionRebuildTests
{
    [Fact]
    public void Every_migration_that_replaces_an_indexed_function_rebuilds_its_indexes()
    {
        var indexes = IndexedFunctions(File.ReadAllText(Path.Combine(RepoRoot.Path, "db", "schema", "current.sql")));
        Assert.Contains("event_record_idx", indexes.Keys);

        var migrations = Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "db", "migrations"), "*.sql")
            .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), File.ReadAllText(f)))
            .ToList();

        Assert.Empty(Unrebuilt(migrations, indexes));
    }

    [Fact]
    public void The_check_finds_a_replaced_function_whose_index_is_not_rebuilt()
    {
        var indexes = new Dictionary<string, IReadOnlySet<string>> { ["thing_idx"] = new HashSet<string> { "thing_key" } };
        const string Create = "CREATE FUNCTION bsk.thing_key(p jsonb) RETURNS text AS $$ SELECT 'a' $$ LANGUAGE sql IMMUTABLE;\nCREATE INDEX thing_idx ON bsk.event (bsk.thing_key(payload));";
        const string Replace = "CREATE OR REPLACE FUNCTION bsk.thing_key(p jsonb) RETURNS text AS $$ SELECT 'b' $$ LANGUAGE sql IMMUTABLE;";

        Assert.Empty(Unrebuilt([("0001__a.sql", Create)], indexes));
        Assert.Equal(["0002__b.sql: replaces bsk.thing_key but doesn't rebuild thing_idx"],
            Unrebuilt([("0001__a.sql", Create), ("0002__b.sql", Replace)], indexes));
        Assert.Empty(Unrebuilt([("0001__a.sql", Create), ("0002__b.sql", Replace + "\nREINDEX INDEX bsk.thing_idx;")], indexes));
    }

    /// <summary>Each index in the schema that calls a <c>bsk</c> function, with the functions it calls.</summary>
    private static Dictionary<string, IReadOnlySet<string>> IndexedFunctions(string schema)
        => IndexLine().Matches(schema)
            .Select(m => (Name: m.Groups["name"].Value, Functions: FunctionCall().Matches(m.Groups["body"].Value)
                .Select(c => c.Groups["fn"].Value).ToHashSet()))
            .Where(i => i.Functions.Count > 0)
            .ToDictionary(i => i.Name, i => (IReadOnlySet<string>)i.Functions);

    /// <summary>
    /// Each migration that replaces a function an index already built calls, without a
    /// <c>REINDEX INDEX</c> of it or a new <c>CREATE INDEX</c> of it after the function.
    /// </summary>
    private static List<string> Unrebuilt(
        IReadOnlyList<(string Name, string Sql)> migrations, IReadOnlyDictionary<string, IReadOnlySet<string>> indexes)
    {
        var built = new HashSet<string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (name, sql) in migrations)
        {
            foreach (Match function in FunctionDefinition().Matches(sql))
            {
                var fn = function.Groups["fn"].Value;
                foreach (var (index, _) in indexes.Where(i => i.Value.Contains(fn) && built.Contains(i.Key)))
                {
                    var after = sql[function.Index..];
                    var rebuilt = Regex.IsMatch(after, $@"REINDEX\s+INDEX\s+(bsk\.)?{index}\b", RegexOptions.IgnoreCase)
                        || Regex.IsMatch(after, $@"CREATE\s+(UNIQUE\s+)?INDEX\s+(IF\s+NOT\s+EXISTS\s+)?{index}\b", RegexOptions.IgnoreCase);
                    if (!rebuilt)
                    {
                        problems.Add($"{name}: replaces bsk.{fn} but doesn't rebuild {index}");
                    }
                }
            }

            foreach (Match created in IndexCreation().Matches(sql))
            {
                built.Add(created.Groups["name"].Value);
            }
        }

        return problems;
    }

    [GeneratedRegex(@"^CREATE (UNIQUE )?INDEX (?<name>\w+) ON (?<body>.*);$", RegexOptions.Multiline)]
    private static partial Regex IndexLine();

    [GeneratedRegex(@"\bbsk\.(?<fn>\w+)\(")]
    private static partial Regex FunctionCall();

    [GeneratedRegex(@"CREATE\s+(OR\s+REPLACE\s+)?FUNCTION\s+bsk\.(?<fn>\w+)\s*\(", RegexOptions.IgnoreCase)]
    private static partial Regex FunctionDefinition();

    [GeneratedRegex(@"CREATE\s+(UNIQUE\s+)?INDEX\s+(IF\s+NOT\s+EXISTS\s+)?(?<name>\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex IndexCreation();
}
