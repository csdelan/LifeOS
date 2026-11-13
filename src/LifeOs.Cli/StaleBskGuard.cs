using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using LifeOs.Infrastructure;
using LifeOs.Infrastructure.Migrations;
using Npgsql;

namespace LifeOs.Cli;

/// <summary>
/// Refuses to run a verb against a database newer than this <c>bsk</c> (code review CR66). The
/// Pilot writes to staging through the <c>bsk</c> <c>scripts/install-bsk.ps1</c> installed; after a
/// push migrates staging, that copy no longer knows the schema's rules until the script runs
/// again, and before this it wrote anyway. Checked once, before any verb runs, so no verb can
/// forget it. <c>migrate</c> and <c>check</c> still run: one only catches a database up, and the
/// other only reads diagnostics.
/// </summary>
internal static class StaleBskGuard
{
    private static readonly HashSet<string> Exempt = new(StringComparer.Ordinal) { "migrate", "check" };

    /// <summary>Why the parsed verb won't run, or null when it may.</summary>
    public static async Task<string?> RefusalAsync(
        ParseResult parsed, Option<string?> connectionOption, CancellationToken cancellationToken)
    {
        if (parsed.Errors.Count > 0 || parsed.Action is HelpAction || Verb(parsed) is not { } verb || Exempt.Contains(verb))
        {
            return null;
        }

        AppliedMigration? applied;
        try
        {
            applied = await SchemaVersion.NewestAppliedAsync(
                KernelConnectionString.Resolve(parsed.GetValue(connectionOption)), cancellationToken);
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException or ArgumentException or TimeoutException)
        {
            // A database it can't reach is the verb's to report, in its own words.
            return null;
        }

        return SchemaVersion.OlderThanDatabase(SchemaVersion.Shipped, applied);
    }

    /// <summary>The top-level verb (<c>new</c> for <c>bsk new …</c>), or null for <c>bsk</c> alone.</summary>
    private static string? Verb(ParseResult parsed)
    {
        var result = parsed.CommandResult;
        while (result.Parent is CommandResult parent && parent.Parent is not null)
        {
            result = parent;
        }

        return result.Parent is null ? null : result.Command.Name;
    }
}
