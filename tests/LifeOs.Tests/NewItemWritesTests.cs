using LifeOs.Pilot.Shell;

namespace LifeOs.Tests;

/// <summary>
/// Code review CR67: the Pilot's New item makes the item once. A follow-up that fails after
/// <c>bsk new</c> (a tag, a link, the repeat, the attendee) is named, the rest still run, and the
/// dialog gets the item it made, instead of staying open for a Save that would make it again.
/// </summary>
public sealed class NewItemWritesTests
{
    private sealed record Made(string Urn, string Title);

    [Fact]
    public void A_follow_up_that_fails_is_named_the_rest_still_run_and_the_item_is_kept()
    {
        var creates = 0;
        var ran = new List<string>();

        var outcome = NewItemWrites.Run(
            () => { creates++; return new Made("urn:bsk:task:gate-1", "Fix the gate"); },
            made => made.Urn,
            [
                new("its tags", urn => ran.Add($"tags {urn}")),
                new("its serves link to urn:bsk:goal:home-1", _ => throw new InvalidOperationException("No subject matches it.")),
                new("its attendee", urn => ran.Add($"attendee {urn}")),
            ]);

        Assert.Equal(1, creates);
        Assert.Equal("urn:bsk:task:gate-1", outcome.Created.Urn);
        Assert.Equal(["tags urn:bsk:task:gate-1", "attendee urn:bsk:task:gate-1"], ran);
        Assert.Equal(["its serves link to urn:bsk:goal:home-1: No subject matches it."], outcome.Failed);

        var report = NewItemWrites.Report(outcome.Created.Title, outcome.Failed);
        Assert.NotNull(report);
        Assert.StartsWith("Fix the gate was made, but some of it didn't save:", report, StringComparison.Ordinal);
        Assert.Contains("- its serves link to urn:bsk:goal:home-1: No subject matches it.", report, StringComparison.Ordinal);
    }

    [Fact]
    public void When_the_item_isnt_made_nothing_else_runs()
    {
        var ran = false;

        Assert.Throws<InvalidOperationException>(() => NewItemWrites.Run<Made>(
            () => throw new InvalidOperationException("A Habit needs a parent."),
            made => made.Urn,
            [new("its tags", _ => ran = true)]));

        Assert.False(ran);
    }

    [Fact]
    public void When_everything_saves_there_is_nothing_to_report()
    {
        var outcome = NewItemWrites.Run(() => new Made("urn:bsk:idea:x-1", "X"), made => made.Urn, [new("its tags", _ => { })]);

        Assert.Empty(outcome.Failed);
        Assert.Null(NewItemWrites.Report(outcome.Created.Title, outcome.Failed));
    }
}
