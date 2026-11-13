using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// What an item pays, read one way for the reconcile that pays it and the bounty it's edited
/// through (rewards plan §3.3, code review CR48): its own bounty, else, for a repetition of a
/// repeating Task, its series' (D16), so every repetition pays the same and a change to the
/// series' price reaches the repetitions completed after it. The API's item detail reads the same
/// rule on the reader connection (<c>SubjectReader</c>).
/// </summary>
public sealed class ItemPrices(ISubjectRepository subjects)
{
    /// <summary>The bounty <paramref name="subject"/> is priced from.</summary>
    public async Task<ItemPrice> ReadAsync(SubjectRef subject, CancellationToken cancellationToken = default)
    {
        var json = await subjects.GetAttributeValueAsync(subject.Id, Bounty.AttributeKey, cancellationToken);
        var fromSeries = false;
        if (json is null
            && subject.Type == SubjectTypes.Task
            && await subjects.GetAttributeValueAsync(subject.Id, TaskSeries.SeriesKey, cancellationToken) is { } seriesUrn
            && await subjects.FindByUrnAsync(seriesUrn, cancellationToken) is { } series)
        {
            json = await subjects.GetAttributeValueAsync(series.Id, Bounty.AttributeKey, cancellationToken);
            fromSeries = true;
        }

        return new ItemPrice(subject.Type, Bounty.Parse(json), Bounty.ParseEveryMinutes(json), fromSeries);
    }
}

/// <summary>
/// An item's stored bounty: each trigger priced on it, the timer's period when it sets one, and
/// whether it's a repetition priced from its series because it has no price of its own (D16).
/// </summary>
public sealed record ItemPrice(
    string Type, Dictionary<string, Dictionary<string, long>> Stored, int? EveryMinutes, bool FromSeries)
{
    /// <summary>
    /// What <paramref name="award"/> pays: its trigger's price, or, for the timer time on a
    /// completed Task, that price once per <see cref="EveryMinutes"/> recorded.
    /// </summary>
    public Dictionary<string, long> For(DueAward award)
    {
        var price = Bounty.Price(Type, award.Trigger, Stored);
        return string.Equals(award.Trigger, BountyTriggers.Timed, StringComparison.OrdinalIgnoreCase)
            ? Bounty.PriceTimed(price, award.TimedSeconds ?? 0, EveryMinutes ?? Bounty.DefaultEveryMinutes)
            : price;
    }
}
