using System.Text.RegularExpressions;
using Dapper;
using LifeOs.Application.Subjects;
using LifeOs.Domain;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PilotSubjectReader = LifeOs.Pilot.Reader.SubjectReader;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR116 (fifth review R9): no view and no SQL in the code casts a JSON value it hasn't
/// checked. The log is append-only, so a value that fails its cast breaks the read for good, and
/// <c>bsk set &lt;item&gt; vision_order=1st</c> broke Browse, the Map and Vision.
/// <para>
/// A cast of a JSON value (<c>-&gt;&gt;</c> or <c>#&gt;&gt;</c>, maybe through <c>NULLIF</c> or
/// <c>btrim</c>) to a uuid, a number, a date or a boolean passes when a test sound for its type
/// checks the same path, in its own CASE branch or WHEN condition, or in a WHERE clause of the same
/// view or file: <c>is_uuid_text</c> or the uuid pattern, <c>jsonb_typeof</c> 'number' or
/// 'boolean', <c>is_iso_date_text</c>, or a whole-number pattern for an int. A timestamp has no
/// in-place test: it's read with <c>bsk.try_to_timestamptz</c>. Any other cast is in
/// <see cref="Checked"/>, with the CHECK that guards it, or in <see cref="Minted"/>, with why only a
/// well-formed value reaches it. What this doesn't check is a number past its type's range.
/// </para>
/// </summary>
public sealed class CastGuardTests(PostgresFixture postgres, ClassDatabase classDb) : IClassFixture<ClassDatabase>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Casts a CHECK on <c>bsk.event</c> guards, by where they are, their type and path.</summary>
    private static readonly Dictionary<string, string> Checked = new()
    {
        ["bsk.v_ledger numeric a.value#>>'{}'"] = "event_ledger_amounts_check",
        ["bsk.v_ledger numeric e.payload->>'dollars'"] = "event_ledger_dollars_check",
        ["bsk.v_ledger numeric e.payload->>'coin_rate'"] = "event_ledger_coin_rate_check",
        ["bsk.v_ledger uuid NULLIF(e.payload->>'subject_id','')"] = "event_ledger_subject_id_check",
        ["bsk.v_metric boolean e.payload->>'archived'"] = "event_archive_change_archived_check",
        ["bsk.v_space boolean e.payload->>'archived'"] = "event_archive_change_archived_check",
        ["bsk_derived.habit_occurrence_source date e.payload->>'occurrence'"] = "event_activity_occurrence_check",
        ["NpgsqlLedgerRepository.cs numeric (e.payload->'amounts')->>w.currency"] = "event_ledger_amounts_check",
        ["NpgsqlLedgerRepository.cs numeric a.value#>>'{}'"] = "event_ledger_amounts_check",
    };

    /// <summary>Casts that only a well-formed value reaches, and why.</summary>
    private static readonly Dictionary<string, string> Minted = new()
    {
        ["bsk.v_lab_visit uuid l.v->>'id'"] = "the version's WHERE keeps an event whose lab_visit.id is a uuid, which live reads as v.id",
        ["bsk.v_metric_reading uuid l.r->>'id'"] = "the version's WHERE keeps an event whose metric_reading.id is a uuid, which live reads as r.id",
        ["bsk.v_space_photo uuid l.p->>'id'"] = "the version's WHERE keeps an event whose space_photo.id is a uuid, which live reads as p.id",
        ["bsk.v_space_rating uuid l.r->>'id'"] = "the version's WHERE keeps an event whose space_rating.id is a uuid, which live reads as r.id",
    };

    [Fact]
    public async Task Every_cast_of_a_json_value_in_a_view_is_guarded()
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        var views = await connection.QueryAsync<(string Name, string Definition)>(new CommandDefinition(
            """
            SELECT schemaname || '.' || viewname, definition FROM pg_views
            WHERE schemaname IN ('bsk', 'bsk_derived') ORDER BY 1;
            """, cancellationToken: Ct));
        var unguarded = views.SelectMany(v => Unguarded(v.Name, v.Definition)).ToHashSet();
        Assert.NotEmpty(views);

        var listed = Checked.Keys.Concat(Minted.Keys).Where(k => k.StartsWith("bsk", StringComparison.Ordinal)).ToHashSet();
        Assert.True(listed.SetEquals(unguarded),
            $"Unguarded and not listed: {string.Join("; ", unguarded.Except(listed))}. Listed but not found: {string.Join("; ", listed.Except(unguarded))}.");

        // Each CHECK named is still there.
        var constraints = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT conname FROM pg_constraint WHERE conrelid = 'bsk.event'::regclass;", cancellationToken: Ct))).ToHashSet();
        Assert.All(Checked.Values, c => Assert.Contains(c, constraints));
    }

    /// <summary>
    /// The functions too (code review CR146): a view that calls one throws when the function does,
    /// and <c>bsk.recurrence_occurrences</c>, which every habit read calls, cast a pattern's
    /// <c>every</c> and <c>day</c> to int unchecked.
    /// </summary>
    [Fact]
    public async Task Every_cast_of_a_json_value_in_a_function_is_guarded()
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        var functions = (await connection.QueryAsync<(string Name, string Body)>(new CommandDefinition(
            """
            SELECT n.nspname || '.' || p.proname, p.prosrc
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname IN ('bsk', 'bsk_derived') ORDER BY 1;
            """, cancellationToken: Ct))).ToList();
        Assert.Contains(functions, f => f.Name == "bsk.recurrence_occurrences");

        var unguarded = functions.SelectMany(f => Unguarded(f.Name, f.Body)).ToHashSet();
        Assert.True(unguarded.SetEquals(FunctionCasts.Keys),
            $"Unguarded and not listed: {string.Join("; ", unguarded.Except(FunctionCasts.Keys))}. Listed but not found: {string.Join("; ", FunctionCasts.Keys.Except(unguarded))}.");
    }

    /// <summary>Casts in a function that only a checked value reaches, and why.</summary>
    private static readonly Dictionary<string, string> FunctionCasts = new()
    {
        ["bsk.is_archived boolean e.payload->>'archived'"] = "event_archive_change_archived_check allows only a boolean or null, as for bsk.v_metric",
        ["bsk.json_int numeric value#>>'{}'"] = "its CASE tests jsonb_typeof(value) = 'number', the same value unwrapped",
        ["bsk.json_num numeric value#>>'{}'"] = "its CASE tests jsonb_typeof(value) = 'number', the same value unwrapped",
        ["bsk.recurrence_from_text integer j->>'intervalDays'"] = "the IF around it tests the same path with a digits pattern",
    };

    [Theory]
    [InlineData("""{"freq":"interval","every":1.5,"unit":"days"}""", 0)]
    [InlineData("""{"freq":"interval","every":"2nd","unit":"weeks"}""", 0)]
    [InlineData("""{"freq":"monthly","day":"first"}""", 0)]
    [InlineData("""{"freq":"weekly","on":"mon"}""", 0)]
    [InlineData("""{"freq":"interval","every":2,"unit":"days"}""", 5)]
    [InlineData("""{"freq":"interval","every":"2","unit":"days"}""", 5)]
    public async Task A_repeat_pattern_reads_a_number_it_cant_use_as_no_dates(string pattern, int dates)
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        var found = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM bsk.recurrence_occurrences(@pattern::jsonb, '2026-11-01', '2026-11-01', '2026-11-10');",
            new { pattern }, cancellationToken: Ct));

        Assert.Equal(dates, found);
    }

    [Fact]
    public void Every_cast_of_a_json_value_in_the_code_is_guarded()
    {
        var unguarded = Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => Unguarded(Path.GetFileName(f), File.ReadAllText(f)))
            .ToHashSet();

        var listed = Checked.Keys.Concat(Minted.Keys).Where(k => !k.StartsWith("bsk", StringComparison.Ordinal)).ToHashSet();
        Assert.True(listed.SetEquals(unguarded),
            $"Unguarded and not listed: {string.Join("; ", unguarded.Except(listed))}. Listed but not found: {string.Join("; ", listed.Except(unguarded))}.");
    }

    [Fact]
    public void The_scan_sees_an_unguarded_cast_and_each_kind_of_guard()
    {
        Assert.Equal(
            [
                "v integer s.attributes->>'n'",
                "v timestamptz s.payload->>'at'",
                "v timestamptz s.payload->>'at'",
                "v uuid NULLIF(s.payload->>'id','')",
                "v uuid s.attributes->>'photo'",
            ],
            Unguarded("v", """
                SELECT (s.attributes->>'photo')::uuid, (s.attributes->>'n')::integer, (s.payload->>'at')::timestamptz,
                       CASE WHEN jsonb_typeof(s.payload->'at') = 'string' THEN (s.payload->>'at')::timestamptz END,
                       NULLIF(s.payload->>'id', '')::uuid
                """).Order(StringComparer.Ordinal));

        // A test in a CASE already closed guards nothing after it.
        Assert.Single(Unguarded("v", "SELECT CASE WHEN jsonb_typeof(s.payload->'n') = 'number' THEN 1 END, (s.payload->>'n')::numeric"));
        Assert.Empty(Unguarded("v", """
            SELECT CASE WHEN bsk.is_uuid_text(s.attributes->>'photo') THEN (s.attributes->>'photo')::uuid END,
                   CASE WHEN jsonb_typeof(m -> 'kcal') = 'number' THEN round((m ->> 'kcal')::numeric) END,
                   CASE WHEN (jsonb_typeof((le.base -> n.currency)) = 'number'::text) THEN ((le.base ->> n.currency))::numeric END,
                   CASE WHEN jsonb_typeof(m -> 'g') = 'number' AND (m ->> 'g')::numeric > 0 THEN 1 END,
                   CASE WHEN s.attributes->>'n' ~ '^\s*-?[0-9]{1,9}\s*$' THEN (s.attributes->>'n')::int END,
                   CASE WHEN regexp_replace(s.attributes->>'usd', '[\s$,]', '', 'g') ~ '^[0-9]+(\.[0-9]+)?$'
                        THEN regexp_replace(s.attributes->>'usd', '[\s$,]', '', 'g')::numeric END,
                   (COALESCE(CASE WHEN jsonb_typeof(m -> 'kcal') = 'number' THEN (m ->> 'kcal')::numeric END, 0))::bigint,
                   (l.r ->> 'space')::uuid, bsk.try_to_timestamptz(s.payload->>'at')
            FROM version v
            WHERE (v.r ->> 'space') ~* '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
            """));
    }

    [Fact]
    public async Task A_vision_order_that_isnt_a_number_reads_as_none_in_the_API_and_the_Pilot()
    {
        DapperConventions.Apply();
        await using var kernel = classDb.Services().BuildServiceProvider();
        var subjects = kernel.GetRequiredService<SubjectService>();
        var attributes = kernel.GetRequiredService<AttributeService>();
        var ns = Guid.NewGuid().ToString("N");
        var first = await subjects.CreateAsync(SubjectTypes.Value, $"Patient {ns}", """{"statement": "I am patient."}""", cancellationToken: Ct);
        var second = await subjects.CreateAsync(SubjectTypes.Value, $"Kind {ns}", """{"statement": "I am kind."}""", cancellationToken: Ct);
        await attributes.SetAsync(first.Urn, [new AttributeAssignment("vision_order", "1st")], Ct);
        await attributes.SetAsync(second.Urn, [new AttributeAssignment("vision_order", "2")], Ct);

        var api = await TestReaders.Subjects(classDb.ConnectionString).QuerySubjectsAsync(SubjectTypes.Value, cancellationToken: Ct);
        Assert.Null(Assert.Single(api, s => s.Id == first.Id).VisionOrder);
        Assert.Equal(2, Assert.Single(api, s => s.Id == second.Id).VisionOrder);

        var pilot = new PilotSubjectReader(BskReader.ConnectionStringFrom(classDb.ConnectionString)).QuerySubjects(SubjectTypes.Value);
        Assert.Null(Assert.Single(pilot, s => s.Id == first.Id).VisionOrder);
        Assert.Equal(2, Assert.Single(pilot, s => s.Id == second.Id).VisionOrder);
    }

    [Fact]
    public async Task A_record_whose_time_doesnt_parse_is_left_out_and_the_view_still_reads()
    {
        await using var db = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var owner = new NpgsqlConnection(db.ConnectionString);
        await owner.OpenAsync(Ct);

        // Made-up Oura records and fasts, not real data: one good and one malformed of each.
        async Task InsertAsync(string kind, string source, string payload) => await owner.ExecuteAsync(new CommandDefinition(
            "INSERT INTO bsk.event (kind, provenance, occurred_at, source_id, payload) VALUES (@kind, 'observed', now(), @source, @payload::jsonb);",
            new { kind, source, payload }, cancellationToken: Ct));
        const string Good = "2026-11-01T07:00:00Z";
        const string Later = "2026-11-01T08:00:00Z";
        foreach (var (id, start) in new[] { ("good", Good), ("bad", "yesterday-ish") })
        {
            await InsertAsync("measurement", "oura", $$$"""{"oura_sleep": {"id": "{{{id}}}", "day": "2026-11-01", "start": "{{{start}}}", "end": "{{{Later}}}"}}""");
            await InsertAsync("activity", "oura", $$$"""{"oura_workout": {"id": "{{{id}}}", "day": "2026-11-01", "start": "{{{start}}}", "end": "{{{Later}}}"}}""");
            await InsertAsync("observation", "oura", $$$"""{"oura_tag": {"id": "{{{id}}}", "day": "2026-11-01", "start": "{{{start}}}"}}""");
            await InsertAsync("activity", "api", $$$"""{"fast": {"id": "{{{Guid.NewGuid()}}}", "started_at": "{{{start}}}"}}""");
        }

        await InsertAsync("activity", "api", $$$"""{"fast": {"id": "{{{Guid.NewGuid()}}}", "started_at": "{{{Good}}}", "ended_at": "after lunch"}}""");

        Assert.Equal(["good"], await owner.QueryAsync<string>(new CommandDefinition("SELECT period_id FROM bsk.v_sleep_period;", cancellationToken: Ct)));
        Assert.Equal(["good"], await owner.QueryAsync<string>(new CommandDefinition("SELECT workout_id FROM bsk.v_workout;", cancellationToken: Ct)));
        Assert.Equal(["good"], await owner.QueryAsync<string>(new CommandDefinition("SELECT tag_id FROM bsk.v_oura_tag;", cancellationToken: Ct)));

        // Two fasts started well: one running, and one whose end doesn't parse, so it reads as running too.
        Assert.Equal(["running", "running"], await owner.QueryAsync<string>(new CommandDefinition("SELECT state FROM bsk.v_fast;", cancellationToken: Ct)));
    }

    [Fact]
    public async Task A_json_number_past_an_ints_range_reads_as_none()
    {
        await using var connection = new NpgsqlConnection(classDb.ConnectionString);
        Assert.Equal(4, await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT bsk.json_int('3.6'::jsonb);", cancellationToken: Ct)));
        Assert.Null(await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT bsk.json_int('1e12'::jsonb);", cancellationToken: Ct)));
        Assert.Null(await connection.ExecuteScalarAsync<int?>(new CommandDefinition("SELECT bsk.json_int('\"7\"'::jsonb);", cancellationToken: Ct)));
    }

    // ---- The scan ----

    private static readonly Regex Cast = new(
        @"\)::(?<type>uuid|integer|int|bigint|smallint|numeric|date|timestamp with time zone|timestamptz|boolean|bool)\b",
        RegexOptions.CultureInvariant);

    // Functions that pass text through, so a cast of one is a cast of the JSON value in it.
    private static readonly string[] TextFunctions = ["nullif", "btrim", "trim", "lower", "upper", "regexp_replace"];

    // Patterns that only a value of the type matches, which a guard may test it with.
    private static readonly Dictionary<string, string[]> Patterns = new()
    {
        ["uuid"] = [@"~*'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'"],
        ["integer"] = [@"~'^\s*-?[0-9]{1,9}\s*$'"],
        ["numeric"] = [@"~'^[0-9]+(\.[0-9]+)?$'", @"~'^\s*-?[0-9]{1,9}\s*$'"],
    };

    /// <summary>Each cast of a JSON value in <paramref name="text"/> with no sound guard, as "where type value".</summary>
    internal static IEnumerable<string> Unguarded(string where, string text)
    {
        var flat = Flat(text);
        foreach (Match match in Cast.Matches(text))
        {
            var close = match.Index;
            var open = Opening(text, close);
            if (open < 0)
            {
                continue;
            }

            // NULLIF(x ->> 'k', '')::uuid casts the call, and (x ->> 'k')::uuid what's in the parentheses.
            var start = open;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '.'))
            {
                start--;
            }

            var operand = start < open ? text[start..(close + 1)] : text[(open + 1)..close];
            if (!JsonValue(operand, out var value, out var shown))
            {
                continue;
            }

            var type = match.Groups["type"].Value switch
            {
                "int" => "integer",
                "bool" => "boolean",
                "timestamp with time zone" => "timestamptz",
                var t => t,
            };
            string[] guards = [.. Guards(type, Flat(value)), .. Guards(type, Flat(operand))];
            if (guards.Length > 0 && (InPlace(text[..start], guards) || InWhere(flat, guards)))
            {
                continue;
            }

            yield return $"{where} {type} {shown}";
        }
    }

    // Whether the operand is a JSON value read as text, maybe through TextFunctions: then the value,
    // and the operand as the tables above write it. A CASE, another call or a cast is a value
    // already checked, or one whose own casts are.
    private static bool JsonValue(string operand, out string value, out string shown)
    {
        value = shown = "";
        operand = Unwrap(operand.Trim());
        var call = Regex.Match(operand, @"^(?<name>[A-Za-z_][\w.]*)\s*\(");
        if (call.Success && operand.EndsWith(')') && Opening(operand, operand.Length - 1) == call.Length - 1)
        {
            var name = call.Groups["name"].Value;
            var arguments = Arguments(operand[call.Length..^1]);
            if (!TextFunctions.Contains(name.Split('.')[^1], StringComparer.OrdinalIgnoreCase)
                || !JsonValue(arguments[0], out value, out var first))
            {
                return false;
            }

            shown = $"{name}({string.Join(",", [first, .. arguments.Skip(1).Select(a => Compact(Unwrap(a.Trim())))])})";
            return true;
        }

        var outside = Outside(Untyped(operand));
        if (operand.StartsWith("CASE", StringComparison.OrdinalIgnoreCase) || outside.Contains("::", StringComparison.Ordinal)
            || !(outside.Contains("->>", StringComparison.Ordinal) || outside.Contains("#>>", StringComparison.Ordinal)))
        {
            return false;
        }

        value = operand;
        shown = Compact(operand);
        return true;
    }

    // The tests sound for a type, on a value as Flat writes it.
    private static string[] Guards(string type, string value)
    {
        string[] tests = type switch
        {
            "uuid" => [$"is_uuid_text{value}"],
            "numeric" or "integer" or "bigint" or "smallint" => [$"jsonb_typeof{value}='number'"],
            "date" => [$"is_iso_date_text{value}"],
            "boolean" => [$"jsonb_typeof{value}='boolean'"],
            _ => [],
        };
        return [.. tests, .. Patterns.GetValueOrDefault(type, []).Select(p => value + Flat(p))];
    }

    // In its own CASE branch (THEN, maybe inside a call) or later in the WHEN condition that tests it.
    private static bool InPlace(string before, string[] guards)
    {
        var when = before.LastIndexOf("WHEN", StringComparison.Ordinal);
        if (when < 0)
        {
            return false;
        }

        var segment = before[(when + 4)..];
        if (Regex.IsMatch(segment, @"\bEND\b"))
        {
            return false;
        }

        var then = segment.LastIndexOf("THEN", StringComparison.Ordinal);
        var condition = then < 0
            ? segment
            : Regex.IsMatch(segment[(then + 4)..], @"^[\s\w.(]*$") ? segment[..then] : null;
        return condition is not null && guards.Any(g => Flat(condition).Contains(g, StringComparison.Ordinal));
    }

    // A WHERE clause of the same view or file tests the value.
    private static bool InWhere(string flat, string[] guards)
    {
        string[] keywords = ["WHERE", "WHEN", "THEN", "SELECT", "FROM", "JOIN", "ELSE", "END"];
        foreach (var guard in guards)
        {
            for (var at = flat.IndexOf(guard, StringComparison.Ordinal); at >= 0; at = flat.IndexOf(guard, at + 1, StringComparison.Ordinal))
            {
                var before = flat[..at];
                if (keywords.MaxBy(k => before.LastIndexOf(k, StringComparison.Ordinal)) == "WHERE")
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Without casts to text, table aliases, spaces or parentheses, and with ->> read as ->, so a
    // test and a cast of one value agree however each is written.
    private static string Flat(string text)
    {
        var flat = Regex.Replace(Untyped(text), @"(?<![A-Za-z0-9_'.])[a-z_][a-z0-9_]*\.(?=[a-z_])", "");
        return Regex.Replace(flat, @"[\s()]", "").Replace("->>", "->", StringComparison.Ordinal);
    }

    private static string Untyped(string text)
        => text.Replace("::text[]", "", StringComparison.Ordinal).Replace("::text", "", StringComparison.Ordinal);

    private static string Compact(string text) => Regex.Replace(Untyped(text), @"\s+", "");

    // Without the parentheses that wrap all of it.
    private static string Unwrap(string text)
    {
        while (text.StartsWith('(') && text.EndsWith(')') && Opening(text, text.Length - 1) == 0)
        {
            text = text[1..^1].Trim();
        }

        return text;
    }

    // What's outside every parenthesis and quote.
    private static string Outside(string text)
    {
        var outside = new System.Text.StringBuilder();
        var depth = 0;
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && c == '(')
            {
                depth++;
            }
            else if (!quoted && c == ')')
            {
                depth--;
            }
            else if (!quoted && depth == 0)
            {
                outside.Append(c);
            }
        }

        return outside.ToString();
    }

    // A call's arguments, split at the commas outside every parenthesis and quote.
    private static List<string> Arguments(string text)
    {
        var arguments = new List<string>();
        var depth = 0;
        var quoted = false;
        var from = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\'':
                    quoted = !quoted;
                    break;
                case '(' when !quoted:
                    depth++;
                    break;
                case ')' when !quoted:
                    depth--;
                    break;
                case ',' when !quoted && depth == 0:
                    arguments.Add(text[from..i]);
                    from = i + 1;
                    break;
            }
        }

        arguments.Add(text[from..]);
        return arguments;
    }

    private static int Opening(string text, int close)
    {
        var depth = 0;
        for (var i = close; i >= 0; i--)
        {
            if (text[i] == ')')
            {
                depth++;
            }
            else if (text[i] == '(' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
