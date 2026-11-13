using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using LifeOs.Domain;
using Npgsql;

namespace LifeOs.Tests;

/// <summary>
/// <c>db/schema/current.sql</c> is the schema every migration adds up to: each table, index,
/// view and function as it stands now, where the migrations hold every version of them. It is
/// generated from a migrated database and checked here, so CI fails when it is stale (code
/// review CR13). Regenerate it with <c>./scripts/update-schema.ps1</c>.
/// </summary>
public sealed partial class SchemaSnapshotTests(PostgresFixture postgres)
{
    /// <summary>Set to 1 to write the snapshot instead of checking it.</summary>
    public const string WriteVariable = "LIFEOS_WRITE_SCHEMA";

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_schema_snapshot_is_current()
    {
        var path = Path.Combine(RepoRoot.Path, "db", "schema", "current.sql");
        var actual = await SnapshotAsync();

        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, actual, new UTF8Encoding(false), Ct);
            return;
        }

        var committed = File.Exists(path) ? Normalize(await File.ReadAllTextAsync(path, Ct)) : "";
        Assert.True(
            committed == actual,
            "db/schema/current.sql is out of date. Run ./scripts/update-schema.ps1 and commit the result.");
    }

    [Fact]
    public async Task The_kernel_vocabulary_matches_the_check_constraints()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(Ct);

        Assert.Equal(Constants(typeof(SubjectTypes)), await AllowedAsync(connection, "bsk.subject", "type"));
        Assert.Equal(Constants(typeof(EventKinds)), await AllowedAsync(connection, "bsk.event", "kind"));
        Assert.Equal(Constants(typeof(Provenances)), await AllowedAsync(connection, "bsk.event", "provenance"));
    }

    // The lists the doors read hold every constant once, so a new type or kind reaches them all
    // (code review CR41).
    [Fact]
    public void The_type_and_kind_lists_hold_every_constant()
    {
        Assert.Equal(Constants(typeof(SubjectTypes)), new SortedSet<string>(SubjectTypes.All, StringComparer.Ordinal));
        Assert.Equal(SubjectTypes.All.Count, SubjectTypes.All.Distinct().Count());
        Assert.Equal(Constants(typeof(EventKinds)), new SortedSet<string>(EventKinds.All, StringComparer.Ordinal));
        Assert.Equal(EventKinds.All.Count, EventKinds.All.Distinct().Count());
        Assert.All(SubjectTypes.All, type => Assert.Contains(type, LifeOs.Api.Mcp.Tools.ItemTools.TypeHelp));
    }

    /// <summary>The values a column's CHECK constraint allows, from <c>column IN (...)</c>.</summary>
    private async Task<SortedSet<string>> AllowedAsync(NpgsqlConnection connection, string table, string column)
    {
        var definitions = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid = @table::regclass AND contype = 'c';
            """,
            new { table }, cancellationToken: Ct));
        var check = Assert.Single(definitions, d => Regex.IsMatch(d, $@"^CHECK \(\({column} = ANY"));
        return new SortedSet<string>(Quoted().Matches(check).Select(m => m.Groups[1].Value), StringComparer.Ordinal);
    }

    private static SortedSet<string> Constants(Type type) => new(
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!),
        StringComparer.Ordinal);

    [GeneratedRegex(@"'([^']+)'::text")]
    private static partial Regex Quoted();

    private async Task<string> SnapshotAsync()
    {
        // A database of its own, so no test's rows or temporary objects reach the snapshot.
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);

        var latest = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT max(version) FROM public.schema_migrations;", cancellationToken: Ct));
        var text = new StringBuilder();
        text.Append($"""
            -- The LifeOS schema as migrations 0001 to {latest:0000} leave it. GENERATED: don't edit it.
            -- Change the schema with a new migration in db/migrations, then run
            -- ./scripts/update-schema.ps1 to regenerate this file. CI fails while it is stale.
            --
            -- Read this to see what a view or function is now; read the migrations for why.

            """);

        foreach (var schema in await QueryAsync<string>(connection, $"""
            SELECT nspname FROM pg_namespace WHERE nspname LIKE 'bsk%' ORDER BY nspname;
            """))
        {
            text.Append($"\nCREATE SCHEMA {schema};\n");
        }

        text.Append("\n-- ---------------------------------------------------------------- tables\n");
        foreach (var (oid, name) in await QueryAsync<(long, string)>(connection, $"""
            SELECT c.oid::int8, quote_ident(n.nspname) || '.' || quote_ident(c.relname)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p') AND {OurSchemas("n.nspname")}
            ORDER BY n.nspname, c.relname;
            """))
        {
            var lines = new List<string>();
            lines.AddRange(await QueryAsync<string>(connection, """
                SELECT '    ' || quote_ident(a.attname) || ' ' || format_type(a.atttypid, a.atttypmod)
                       || CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE '' END
                       || coalesce(' DEFAULT ' || pg_get_expr(d.adbin, d.adrelid), '')
                FROM pg_attribute a
                LEFT JOIN pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
                WHERE a.attrelid = @oid::oid AND a.attnum > 0 AND NOT a.attisdropped
                ORDER BY a.attnum;
                """, new { oid }));
            lines.AddRange(await QueryAsync<string>(connection, """
                SELECT '    CONSTRAINT ' || quote_ident(conname) || ' ' || pg_get_constraintdef(oid)
                FROM pg_constraint
                WHERE conrelid = @oid::oid
                ORDER BY conname;
                """, new { oid }));
            text.Append($"\n{Comment(await DescriptionAsync(connection, oid, "pg_class"))}CREATE TABLE {name} (\n");
            text.Append(string.Join(",\n", lines)).Append("\n);\n");
        }

        text.Append("\n-- ---------------------------------------------------------------- indexes\n\n");
        foreach (var index in await QueryAsync<string>(connection, $"""
            SELECT pg_get_indexdef(i.indexrelid) || ';'
            FROM pg_index i
            JOIN pg_class t ON t.oid = i.indrelid
            JOIN pg_class x ON x.oid = i.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE {OurSchemas("n.nspname")}
              AND NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conindid = i.indexrelid)
            ORDER BY n.nspname, t.relname, x.relname;
            """))
        {
            text.Append(index).Append('\n');
        }

        text.Append("\n-- ---------------------------------------------------------------- views\n");
        foreach (var (oid, name, kind) in await QueryAsync<(long, string, string)>(connection, $"""
            SELECT c.oid::int8, quote_ident(n.nspname) || '.' || quote_ident(c.relname), c.relkind::text
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('v', 'm') AND {OurSchemas("n.nspname")}
            ORDER BY n.nspname, c.relname;
            """))
        {
            var definition = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                "SELECT pg_get_viewdef(@oid::oid, true);", new { oid }, cancellationToken: Ct));
            var create = kind == "m" ? "CREATE MATERIALIZED VIEW" : "CREATE VIEW";
            text.Append($"\n{Comment(await DescriptionAsync(connection, oid, "pg_class"))}{create} {name} AS\n{definition!.TrimEnd()}\n");
        }

        text.Append("\n-- ---------------------------------------------------------------- functions\n");
        foreach (var (oid, definition) in await QueryAsync<(long, string)>(connection, $"""
            SELECT p.oid::int8, pg_get_functiondef(p.oid)
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE p.prokind IN ('f', 'p') AND {OurSchemas("n.nspname")}
              AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
            ORDER BY n.nspname, p.proname, pg_get_function_identity_arguments(p.oid);
            """))
        {
            text.Append($"\n{Comment(await DescriptionAsync(connection, oid, "pg_proc"))}{definition.TrimEnd()};\n");
        }

        var triggers = await QueryAsync<string>(connection, $"""
            SELECT pg_get_triggerdef(t.oid) || ';'
            FROM pg_trigger t
            JOIN pg_class c ON c.oid = t.tgrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE NOT t.tgisinternal AND {OurSchemas("n.nspname")}
            ORDER BY n.nspname, c.relname, t.tgname;
            """);
        if (triggers.Count > 0)
        {
            text.Append("\n-- ---------------------------------------------------------------- triggers\n\n");
            foreach (var trigger in triggers)
            {
                text.Append(trigger).Append('\n');
            }
        }

        return Normalize(text.ToString());
    }

    /// <summary>The schemas the migrations make, and <c>public</c> for their history table.</summary>
    private static string OurSchemas(string column)
        => $"({column} IN ('public') OR {column} LIKE 'bsk%')";

    private async Task<string?> DescriptionAsync(NpgsqlConnection connection, long oid, string catalog)
        => await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            $"SELECT obj_description(@oid::oid, '{catalog}');", new { oid }, cancellationToken: Ct));

    /// <summary>An object's COMMENT, as SQL comment lines above it.</summary>
    private static string Comment(string? description)
        => string.IsNullOrWhiteSpace(description)
            ? ""
            : string.Concat(description.Replace("\r\n", "\n").Trim().Split('\n').Select(line => $"-- {line}".TrimEnd() + "\n"));

    private async Task<List<T>> QueryAsync<T>(NpgsqlConnection connection, string sql, object? parameters = null)
        => [.. await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: Ct))];

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd() + "\n";
}
