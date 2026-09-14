using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Shell;

/// <summary>Shared write sequences: attributes, status+rebuild, recurrence, tags, involve.</summary>
internal static class BskWrites
{
    public static void SetAttributes(BskCli bsk, string urn, IReadOnlyDictionary<string, string?> values)
    {
        var args = new List<string> { "set", urn };
        foreach (var (key, value) in values)
        {
            args.Add($"{key}={value ?? ""}");
        }

        if (args.Count > 2)
        {
            bsk.Run(args.ToArray());
        }
    }

    public static void ChangeStatus(BskCli bsk, string urn, string status)
    {
        bsk.Run("status", urn, status);
        bsk.Run("rebuild");
    }

    public static void ApplyRecurrence(BskCli bsk, string urn, string[]? recurArgs)
    {
        if (recurArgs is null)
        {
            return;
        }

        var args = new List<string> { "recur", urn };
        args.AddRange(recurArgs);
        bsk.Run(args.ToArray());
    }

    public static void ApplyTags(BskCli bsk, string urn, IEnumerable<string> tags)
    {
        var list = tags.Where(t => t.Length > 0).Distinct().ToList();
        if (list.Count == 0)
        {
            return;
        }

        var args = new List<string> { "tag", urn, "--add" };
        args.AddRange(list);
        bsk.Run(args.ToArray());
    }

    public static void ApplyLinks(BskCli bsk, string fromUrn, IEnumerable<(string Relation, string Target)> links)
    {
        foreach (var (relation, target) in links)
        {
            if (target.Length > 0)
            {
                bsk.Run("link", fromUrn, relation, target);
            }
        }
    }
}
