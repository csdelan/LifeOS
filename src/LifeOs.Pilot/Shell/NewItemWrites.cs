namespace LifeOs.Pilot.Shell;

/// <summary>
/// The New item dialog's writes (code review CR67): make the item, then each follow-up (its tags,
/// each link, its repeat, its attendee) on its own. Before, one failure anywhere left the dialog
/// open saying "Create failed" though <c>bsk new</c> had made the item, so a second Save made it
/// again. Once the item exists, a follow-up that fails is named and the rest still run, and the
/// dialog closes on the item it made. Kept free of WinForms so tests can compile it.
/// </summary>
internal static class NewItemWrites
{
    /// <summary>A write that finishes a new item, named as the report names it ("its tags").</summary>
    public sealed record FollowUp(string Name, Action<string> Apply);

    /// <summary>What was made, and each follow-up that failed with why.</summary>
    public sealed record Outcome<T>(T Created, IReadOnlyList<string> Failed);

    /// <summary>
    /// Makes the item with <paramref name="create"/>; if that fails, it throws and nothing is made.
    /// Then runs every follow-up on the new item's urn, collecting the ones that fail.
    /// </summary>
    public static Outcome<T> Run<T>(Func<T> create, Func<T, string> urnOf, IEnumerable<FollowUp> followUps)
    {
        var created = create();
        var urn = urnOf(created);
        var failed = new List<string>();
        foreach (var followUp in followUps)
        {
            try
            {
                followUp.Apply(urn);
            }
            catch (Exception ex)
            {
                failed.Add($"{followUp.Name}: {ex.Message}");
            }
        }

        return new Outcome<T>(created, failed);
    }

    /// <summary>What to tell Chris when some follow-ups failed, or null when they all saved.</summary>
    public static string? Report(string title, IReadOnlyList<string> failed)
        => failed.Count == 0
            ? null
            : $"{title} was made, but some of it didn't save:\n\n- {string.Join("\n- ", failed)}\n\n"
              + "The item is there; open it to add what's missing.";
}
