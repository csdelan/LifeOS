using System.Globalization;
using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>What marking or unmarking a day did: the day as it now reads (null once unmarked) and what the change paid or moved.</summary>
public sealed record ExceptionDayChange(ExceptionDay? Exception, Earned Earned);

/// <summary>Today's date in the home zone and the marked days among the last <see cref="Days"/>, newest first.</summary>
public sealed record ExceptionDays(DateOnly Today, int Days, IReadOnlyList<ExceptionDay> Marked);

/// <summary>
/// Days Chris marks as exceptions (REWARD-8, D17, xp-domains plan §7.4.3), behind
/// <c>bsk domains exception</c> and <c>/api/domains/exceptions</c>. A day of sickness, injury or
/// travel, marked with a comment saying which, neither helps nor hurts the life domains: from
/// X10 the fold skips it. Each change is a <c>declared</c> <c>note</c> event,
/// <c>{exception_day: {day, comment}}</c>; the newest per day wins, and unmarking writes
/// <c>{day, voided: true}</c>. Days are today or earlier in the home zone. Refusals are in
/// words, with nothing written.
/// </summary>
public sealed class ExceptionDayService(
    ILedgerRepository ledger, IEventStore events, RewardService rewards, IHomeCalendar calendar, IClock clock,
    string sourceId)
{
    /// <summary>The longest comment, in characters.</summary>
    public const int MaxLength = 140;

    /// <summary>The payload key an exception day's events carry.</summary>
    public const string PayloadKey = "exception_day";

    /// <summary>How far back a list reads by default: the consistency window.</summary>
    public const int DefaultDays = 90;

    /// <summary>The longest list: two years.</summary>
    public const int MaxDays = 731;

    // Unicode's own line breaks, which aren't control characters.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Marks <paramref name="day"/> (today in the home zone when null) as an exception, with
    /// <paramref name="comment"/> saying why. Marking a day already marked with the same
    /// comment writes nothing; a different comment writes a newer version. Throws
    /// <see cref="ArgumentException"/> for a blank or overlong comment, a line break, or a day
    /// after today.
    /// </summary>
    public async Task<ExceptionDayChange> MarkAsync(
        DateOnly? day, string? comment, CancellationToken cancellationToken = default)
    {
        var text = Normalize(comment);
        var today = await TodayAsync(cancellationToken);
        var on = day ?? today;
        if (on > today)
        {
            throw new ArgumentException(
                $"An exception day is today or earlier; {Text(on)} hasn't happened yet (today is {Text(today)}).");
        }

        var current = await FindAsync(on, today, cancellationToken);
        if (current is not null && current.Comment == text)
        {
            return new ExceptionDayChange(current, Earned.Nothing);
        }

        var earned = await rewards.ChangeDomainInputsAsync(
            ct => AppendAsync(new { day = Text(on), comment = text }, ct), cancellationToken);
        return new ExceptionDayChange(await FindAsync(on, today, cancellationToken), earned);
    }

    /// <summary>
    /// Unmarks <paramref name="day"/>, so it counts like any other day again. A day that isn't
    /// marked writes nothing.
    /// </summary>
    public async Task<ExceptionDayChange> UnmarkAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var today = await TodayAsync(cancellationToken);
        if (await FindAsync(day, today, cancellationToken) is null)
        {
            return new ExceptionDayChange(null, Earned.Nothing);
        }

        var earned = await rewards.ChangeDomainInputsAsync(
            ct => AppendAsync(new { day = Text(day), voided = true }, ct), cancellationToken);
        return new ExceptionDayChange(null, earned);
    }

    /// <summary>
    /// The marked days among the last <paramref name="days"/> (today included), newest first.
    /// Throws <see cref="ArgumentException"/> for a count outside 1 to <see cref="MaxDays"/>.
    /// </summary>
    public async Task<ExceptionDays> ListAsync(int days = DefaultDays, CancellationToken cancellationToken = default)
    {
        Check(days);
        var (today, marked) = await ledger.GetExceptionDaysAsync(days, cancellationToken);
        return new ExceptionDays(today, days, marked);
    }

    /// <summary>Throws <see cref="ArgumentException"/> for a day count outside 1 to <see cref="MaxDays"/>.</summary>
    public static void Check(int days)
    {
        if (days < 1 || days > MaxDays)
        {
            throw new ArgumentException($"Ask for 1 to {MaxDays} days; got {days}.");
        }
    }

    /// <summary>
    /// The comment as it's kept: trimmed. Throws <see cref="ArgumentException"/> when it's
    /// blank, has a line break (or any other control character), or runs past
    /// <see cref="MaxLength"/> characters.
    /// </summary>
    public static string Normalize(string? comment)
    {
        var text = comment?.Trim() ?? "";
        if (text.Length == 0)
        {
            throw new ArgumentException("Say why the day is an exception (sick, injured, travelling…): the comment is required.");
        }

        if (text.Any(c => char.IsControl(c) || c == LineSeparator || c == ParagraphSeparator))
        {
            throw new ArgumentException("An exception day's comment is one line: take out the line break.");
        }

        if (text.Length > MaxLength)
        {
            throw new ArgumentException($"An exception day's comment is up to {MaxLength} characters; that one is {text.Length}.");
        }

        return text;
    }

    private async Task<ExceptionDay?> FindAsync(DateOnly day, DateOnly today, CancellationToken cancellationToken)
    {
        // The list reads back from today, so it reaches the day when it's no later than today.
        var span = today.DayNumber - day.DayNumber + 1;
        if (span < 1)
        {
            return null;
        }

        var (_, marked) = await ledger.GetExceptionDaysAsync(span, cancellationToken);
        return marked.FirstOrDefault(d => d.Day == day);
    }

    private Task AppendAsync(object exceptionDay, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { [PayloadKey] = exceptionDay });
        return events.AppendAsync(
            new NewEvent(EventKinds.Note, Provenances.Declared, now, now, sourceId, PayloadJson: payload),
            cancellationToken);
    }

    private Task<DateOnly> TodayAsync(CancellationToken cancellationToken) => calendar.DateAtAsync(clock.UtcNow, cancellationToken);

    private static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
