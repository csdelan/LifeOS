using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LifeOs.Domain;

namespace LifeOs.Tests;

/// <summary>
/// Invariant 9 keeps the Pilot free of a ProjectReference to the kernel, so its
/// status vocabulary is a hand-copy of <see cref="StatusVocabulary"/> in
/// <c>PilotVocab.cs</c>, kept in lockstep "by convention". This test turns that
/// convention into a red build: it parses the Pilot source and asserts every type's
/// status list matches the kernel word-for-word. We read the source file rather than
/// reference the assembly because the Pilot is a Windows-only WinForms exe and its
/// vocab is <c>internal</c> — a reference would drag WinForms into the test suite and
/// pin it to a windows target for one comparison.
/// </summary>
public sealed class PilotVocabDriftTests
{
    private static readonly Regex EntryPattern =
        new(@"\[(\w+)\]\s*=\s*\[([^\]]*)\]", RegexOptions.Compiled);

    [Fact]
    public void Pilot_status_vocabulary_matches_the_kernel_word_for_word()
    {
        var pilot = ParsePilotStatusByType();

        // Sanity: if the parser found nothing, the source shape changed — fail loudly
        // rather than pass a vacuous comparison.
        Assert.NotEmpty(pilot);

        var kernelTypes = StatusVocabulary.StatusBearingTypes;

        Assert.Equal(
            kernelTypes.OrderBy(t => t, StringComparer.Ordinal),
            pilot.Keys.OrderBy(t => t, StringComparer.Ordinal));

        foreach (var type in kernelTypes)
        {
            // Exact spelling and order: the Pilot's status folding relies on the
            // documented casing, and the default status is the first list item.
            Assert.Equal(StatusVocabulary.For(type), pilot[type]);
        }
    }

    private static Dictionary<string, string[]> ParsePilotStatusByType()
    {
        var source = File.ReadAllText(PilotVocabPath());

        // Restrict to the StatusByType dictionary so no other `[Key] = [...]` shape
        // could be picked up.
        var start = source.IndexOf("StatusByType", StringComparison.Ordinal);
        Assert.True(start >= 0, "Could not find StatusByType in PilotVocab.cs.");
        var end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, "Could not find the end of StatusByType in PilotVocab.cs.");
        var block = source[start..end];

        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (Match entry in EntryPattern.Matches(block))
        {
            var type = entry.Groups[1].Value;
            var statuses = entry.Groups[2].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => s.Trim('"'))
                .ToArray();
            map[type] = statuses;
        }

        return map;
    }

    private static string PilotVocabPath([CallerFilePath] string thisFile = "")
    {
        // tests/LifeOs.Tests/PilotVocabDriftTests.cs → repo root → the Pilot source.
        var testsDir = Path.GetDirectoryName(thisFile)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testsDir, "..", ".."));
        return Path.Combine(repoRoot, "src", "LifeOs.Pilot", "Shell", "PilotVocab.cs");
    }
}
