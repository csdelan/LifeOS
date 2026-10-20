using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Subjects;
using LifeOs.Domain;

namespace LifeOs.Application.Rewards;

/// <summary>
/// Sets a Habit's establish target (REWARD-2) — the write path behind <c>bsk habit target</c>
/// and <c>POST /api/habits/target</c>. The target lives in <c>attributes.establish_target</c>
/// as numbers, <c>{ "min_adherence": 0.8, "window_days": 60 }</c>, which
/// <c>bsk.v_habit_establishment</c> reads to suggest "Ready to establish". A part the habit
/// never names reads as its default, so changing one part leaves the other alone.
/// <para>
/// The view is lenient (a part that isn't a usable number falls back to the default); this
/// writer is strict, so what Chris asks for is what the view reads: an adherence above 0 and
/// at most 1, or a percentage up to 100, stored as the fraction; a window of whole days from
/// 1 to <see cref="EstablishTarget.MaxWindowDays"/>.
/// </para>
/// </summary>
public sealed class HabitTargetService(SubjectService subjects, ISubjectRepository repository)
{
    /// <summary>The habit's target as the view reads it, and which parts are its own.</summary>
    public async Task<HabitTargetView> GetAsync(string reference, CancellationToken cancellationToken = default)
    {
        var habit = await ResolveHabitAsync(reference, cancellationToken);
        return View(habit, await ReadAsync(habit, cancellationToken));
    }

    /// <summary>
    /// Sets the minimum adherence, the window, or both; a part left null keeps what the habit
    /// has (its own value, or the default). Refused, with nothing written, for a non-Habit, an
    /// adherence outside (0, 1] that isn't a percentage up to 100, or a window outside
    /// 1..<see cref="EstablishTarget.MaxWindowDays"/> days.
    /// </summary>
    public async Task<HabitTargetView> SetAsync(
        string reference, decimal? minAdherence, int? windowDays, CancellationToken cancellationToken = default)
    {
        if (minAdherence is null && windowDays is null)
        {
            throw new ArgumentException("Say what to change: the adherence (e.g. 80%), the window in days, or both.");
        }

        var min = minAdherence is { } m ? EstablishTarget.NormalizeMinAdherence(m) : (decimal?)null;
        var window = windowDays is { } w ? EstablishTarget.ValidateWindowDays(w) : (int?)null;
        var habit = await ResolveHabitAsync(reference, cancellationToken);
        var stored = await ReadAsync(habit, cancellationToken);

        var next = new StoredTarget(min ?? stored.MinAdherence, window ?? stored.WindowDays);
        var target = new JsonObject();
        if (next.MinAdherence is { } nextMin)
        {
            target[EstablishTarget.MinAdherenceKey] = nextMin;
        }

        if (next.WindowDays is { } nextWindow)
        {
            target[EstablishTarget.WindowDaysKey] = nextWindow;
        }

        // `attributes || patch` merges only the top level, so the whole target is written.
        await WriteAsync(
            habit, new JsonObject { [EstablishTarget.AttributeKey] = target }.ToJsonString(), [], cancellationToken);
        return View(habit, next);
    }

    /// <summary>Puts the habit back on the default target (removes <c>establish_target</c>).</summary>
    public async Task<HabitTargetView> ClearAsync(string reference, CancellationToken cancellationToken = default)
    {
        var habit = await ResolveHabitAsync(reference, cancellationToken);
        await WriteAsync(habit, "{}", [EstablishTarget.AttributeKey], cancellationToken);
        return View(habit, new StoredTarget(null, null));
    }

    private async Task<SubjectRef> ResolveHabitAsync(string reference, CancellationToken cancellationToken)
    {
        var subject = await subjects.ResolveAsync(reference, cancellationToken);
        return subject.Type == SubjectTypes.Habit
            ? subject
            : throw new InvalidOperationException(
                $"'{subject.Title}' is a {subject.Type}; only a Habit has an establish target.");
    }

    private async Task<StoredTarget> ReadAsync(SubjectRef habit, CancellationToken cancellationToken)
        => EstablishTarget.Parse(await repository.GetAttributeValueAsync(
            habit.Id, EstablishTarget.AttributeKey, cancellationToken));

    private async Task WriteAsync(
        SubjectRef habit, string patchJson, IReadOnlyList<string> removeKeys, CancellationToken cancellationToken)
    {
        if (!await repository.UpdateAttributesAsync(habit.Id, patchJson, removeKeys, cancellationToken))
        {
            // The habit resolved a moment ago; a missing row now is a real fault.
            throw new SubjectNotFoundException(habit.Urn);
        }
    }

    private static HabitTargetView View(SubjectRef habit, StoredTarget stored)
        => new(
            habit,
            stored.MinAdherence ?? EstablishTarget.DefaultMinAdherence,
            stored.WindowDays ?? EstablishTarget.DefaultWindowDays,
            AdherenceIsCustom: stored.MinAdherence is not null,
            WindowIsCustom: stored.WindowDays is not null);
}

/// <summary>
/// A Habit's establish target (REWARD-2): followed at least <see cref="DefaultMinAdherence"/>
/// of the time over the last <see cref="DefaultWindowDays"/> days unless the habit says
/// otherwise in <c>attributes.establish_target</c>. The defaults are the ones
/// <c>bsk.v_habit_establishment</c> (migration 0027) falls back to.
/// </summary>
public static class EstablishTarget
{
    public const string AttributeKey = "establish_target";
    public const string MinAdherenceKey = "min_adherence";
    public const string WindowDaysKey = "window_days";

    public const decimal DefaultMinAdherence = 0.8m;
    public const int DefaultWindowDays = 60;

    /// <summary>Ten years: far past any useful window, and well inside the view's date range.</summary>
    public const int MaxWindowDays = 3650;

    /// <summary>
    /// The minimum adherence as a fraction: one above 0 and at most 1 is taken as it is, and
    /// one above 1 and at most 100 as a percentage (80 → 0.8), as the view reads it.
    /// </summary>
    public static decimal NormalizeMinAdherence(decimal value) => value switch
    {
        > 0 and <= 1 => value,
        > 1 and <= 100 => value / 100,
        _ => throw new ArgumentException(
            $"An establish target is the share of days followed: above 0 and up to 1 (0.8), or a percentage up to 100 (80); got {value}.")
    };

    public static int ValidateWindowDays(int value)
        => value is >= 1 and <= MaxWindowDays
            ? value
            : throw new ArgumentException(
                $"An establish window is a whole number of days from 1 to {MaxWindowDays}; got {value}.");

    /// <summary>
    /// The parts of a stored target the view honours, read as leniently as it reads them: a
    /// part that isn't a number in range is absent (the default applies).
    /// </summary>
    internal static StoredTarget Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new StoredTarget(null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new StoredTarget(null, null);
            }

            decimal? min = null;
            if (Number(root, MinAdherenceKey) is { } m and > 0 and <= 100)
            {
                min = m <= 1 ? m : m / 100;
            }

            int? window = null;
            if (Number(root, WindowDaysKey) is { } w and >= 1 and <= int.MaxValue)
            {
                window = (int)decimal.Floor(w);
            }

            return new StoredTarget(min, window);
        }
        catch (JsonException)
        {
            return new StoredTarget(null, null);
        }
    }

    private static decimal? Number(JsonElement target, string key)
        => target.TryGetProperty(key, out var value)
           && value.ValueKind == JsonValueKind.Number
           && value.TryGetDecimal(out var number)
            ? number
            : null;
}

/// <summary>The parts of <c>attributes.establish_target</c> a habit names; null = the default.</summary>
internal sealed record StoredTarget(decimal? MinAdherence, int? WindowDays);

/// <summary>
/// A habit's establish target as <c>bsk.v_habit_establishment</c> reads it: the minimum
/// adherence as a fraction and the window in days, and whether each is the habit's own
/// (else the default).
/// </summary>
public sealed record HabitTargetView(
    SubjectRef Habit, decimal MinAdherence, int WindowDays, bool AdherenceIsCustom, bool WindowIsCustom);
