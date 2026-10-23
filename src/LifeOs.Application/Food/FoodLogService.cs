using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeOs.Application.Abstractions;
using LifeOs.Application.Rewards;
using LifeOs.Domain;

namespace LifeOs.Application.Food;

/// <summary>
/// The Food Log's write path and reads (FOOD-1..4, food-log plan §3.5) — behind
/// <c>bsk food</c> and the <c>/api/food</c> endpoints. Every write is an append-only event:
/// <list type="bullet">
/// <item>a food entry is a <c>measurement</c> event (<c>meal</c>), dated when it was eaten</item>
/// <item>a fast is an <c>activity</c> event (<c>fast</c>); begin, end, a backfill and an edit
/// are each a new version of it</item>
/// <item>closing a day is an <c>activity</c> event (<c>food_day</c>)</item>
/// </list>
/// An edit or a delete appends a newer version of the same id; the newest by
/// <c>recorded_at</c> wins, so nothing is ever rewritten. Refusals are in words, with nothing
/// written. Days and wall-clock times are the home zone's.
/// </summary>
public sealed class FoodLogService(
    IFoodLogRepository food, FoodRewards rewards, FoodReads reads, IArtifactBlobStore blobs, IClock clock, string sourceId)
{
    /// <summary>The widest range <see cref="GetDaysAsync"/> answers.</summary>
    public const int MaxRangeDays = FoodReads.MaxRangeDays;

    /// <summary>How far ahead of the clock a time may be (a phone's clock running fast).</summary>
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(1);

    /// <summary>The earliest time an entry or fast may carry.</summary>
    private static readonly DateTimeOffset Earliest = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions OmitNulls = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The diet rules days are scored with.</summary>
    public DietRules Rules => reads.Rules;

    // ---------------------------------------------------------------------------------
    // Food entries
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Logs a food entry. With <paramref name="endFast"/>, the running fast ends at the
    /// entry's time in the same write ("Ends your 17 h fast at 12:30"). With
    /// <paramref name="photo"/> (a photo already stored, as another entry shows it), the new
    /// entry shows it too: Log again and Have another carry the last one's photo. Nothing is
    /// stored again, and each entry can later change or remove its own.
    /// </summary>
    public async Task<FoodWriteResult> LogAsync(
        FoodEntryInput input, bool endFast = false, Guid? photo = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var entry = await NormalizeAsync(input, now, cancellationToken);
        if (photo is { } stored)
        {
            await CheckPhotoAsync(stored, cancellationToken);
        }

        var mealId = Guid.NewGuid();
        var events = new List<NewEvent> { MealEvent(mealId, entry, photo, now) };
        var guard = FoodGuard.None;
        Fast? ending = null;

        if (endFast)
        {
            if (entry.Calories == 0)
            {
                throw new ArgumentException("A 0-calorie entry doesn't end a fast; leave the fast running.");
            }

            ending = await food.GetRunningFastAsync(cancellationToken)
                     ?? throw new InvalidOperationException("No fast is running, so there is none to end.");
            if (entry.EatenAt <= ending.StartedAt)
            {
                throw new ArgumentException(
                    $"That's before your fast began ({FoodText.When(ending.StartedLocal)}), so it can't end it.");
            }

            events.Add(FastEvent(ending.Id, ending.StartedAt, entry.EatenAt, occurredAt: entry.EatenAt, now));
            guard = new FoodGuard(ending.Id, new FastSpan(ending.StartedAt, entry.EatenAt, ending.Id));
        }

        var ids = await food.AppendAsync(events, guard, cancellationToken);
        var days = new List<DateOnly> { entry.EatenOn };
        var fast = ending is null ? null : await food.GetFastAsync(ending.Id, cancellationToken);
        if (fast?.EndedOn is { } endedOn)
        {
            days.Add(endedOn);
        }

        return await ResultAsync(
            FoodActions.Logged, await food.GetMealAsync(mealId, cancellationToken), fast, null, days, ids[0], cancellationToken);
    }

    /// <summary>
    /// Replaces a food entry with <paramref name="input"/> as a whole (named by its id or the
    /// start of it). The photo, when it has one, is kept, and so is the alcohol flag when the
    /// input leaves it out.
    /// </summary>
    public async Task<FoodWriteResult> EditAsync(
        string reference, FoodEntryInput input, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var meal = await ResolveMealAsync(reference, cancellationToken);
        var entry = await NormalizeAsync(input with { Alcoholic = input.Alcoholic ?? meal.Alcoholic }, now, cancellationToken);
        var ids = await food.AppendAsync(
            [MealEvent(meal.Id, entry, meal.PhotoArtifactId, now)], FoodGuard.None, cancellationToken);
        return await ResultAsync(
            FoodActions.Edited, await food.GetMealAsync(meal.Id, cancellationToken), null, null,
            [meal.EatenOn, entry.EatenOn], ids[0], cancellationToken);
    }

    /// <summary>Deletes a food entry: a voided version, so the record keeps what it was.</summary>
    public async Task<FoodWriteResult> DeleteAsync(string reference, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var meal = await ResolveMealAsync(reference, cancellationToken);
        var ids = await food.AppendAsync(
            [Voided(EventKinds.Measurement, "meal", meal.Id, now)], FoodGuard.None, cancellationToken);
        return await ResultAsync(FoodActions.Deleted, meal, null, null, [meal.EatenOn], ids[0], cancellationToken);
    }

    /// <summary>
    /// Sets or removes a food entry's photo (FOOD-5): a new version of the entry, the same in
    /// every other way, pointing at <paramref name="photoArtifactId"/> (an artifact with bytes)
    /// or at none. Setting the photo it already has writes nothing. Called by
    /// <see cref="FoodPhotoService"/>, which stores the bytes first.
    /// </summary>
    public async Task<FoodWriteResult> SetPhotoAsync(
        string reference, Guid? photoArtifactId, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var meal = await ResolveMealAsync(reference, cancellationToken);
        var action = photoArtifactId is null ? FoodActions.PhotoRemoved : FoodActions.PhotoSet;
        if (photoArtifactId is null && meal.PhotoArtifactId is null)
        {
            throw new InvalidOperationException($"{meal.Name} has no photo to remove.");
        }

        if (meal.PhotoArtifactId == photoArtifactId)
        {
            return await ResultAsync(action, meal, null, null, [meal.EatenOn], null, cancellationToken);
        }

        var entry = new NormalizedEntry(
            meal.Name, meal.Score, meal.Calories, meal.EatenAt, meal.EatenOn, meal.MealType,
            meal.ProteinG, meal.CarbsG, meal.FatG, meal.Alcoholic);
        var ids = await food.AppendAsync(
            [MealEvent(meal.Id, entry, photoArtifactId, now)], FoodGuard.None, cancellationToken);
        return await ResultAsync(
            action, await food.GetMealAsync(meal.Id, cancellationToken), null, null, [meal.EatenOn], ids[0],
            cancellationToken);
    }

    /// <summary>
    /// The photo of the entry named by <paramref name="reference"/>, for a new entry to show
    /// (<c>bsk food log --photo-from</c>); refused in words when it has none.
    /// </summary>
    public async Task<Guid> PhotoOfAsync(string reference, CancellationToken cancellationToken = default)
    {
        var meal = await ResolveMealAsync(reference, cancellationToken);
        return meal.PhotoArtifactId ?? throw new InvalidOperationException($"{meal.Name} has no photo to carry over.");
    }

    /// <summary>A photo a new entry shows must be stored here, as a JPEG, PNG or WebP.</summary>
    private async Task CheckPhotoAsync(Guid photo, CancellationToken cancellationToken)
    {
        var stored = await blobs.GetMetadataAsync(photo, cancellationToken);
        if (stored is null || !FoodPhotoService.PhotoTypes.Contains(stored.ContentType))
        {
            throw new ArgumentException("That photo isn't stored here: pick the photo again, or log the entry without one.");
        }
    }

    // ---------------------------------------------------------------------------------
    // Fasts
    // ---------------------------------------------------------------------------------

    /// <summary>Begins a fast at <paramref name="at"/> (default now). One fast runs at a time.</summary>
    public async Task<FoodWriteResult> BeginFastAsync(DateTimeOffset? at = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var start = CheckTime(at ?? now, now, "A fast");
        if (await food.GetRunningFastAsync(cancellationToken) is { } running)
        {
            throw new InvalidOperationException(
                $"A fast is already running (since {FoodText.When(running.StartedLocal)}). End it first.");
        }

        var span = new FastSpan(start, null);
        await RefuseOverlapAsync(span, cancellationToken);
        var fastId = Guid.NewGuid();
        var ids = await food.AppendAsync(
            [FastEvent(fastId, start, null, occurredAt: start, now)], new FoodGuard(Guid.Empty, span), cancellationToken);
        return await ResultAsync(
            FoodActions.FastBegun, null, await food.GetFastAsync(fastId, cancellationToken), null, [], ids[0], cancellationToken);
    }

    /// <summary>Ends the running fast at <paramref name="at"/> (default now).</summary>
    public async Task<FoodWriteResult> EndFastAsync(DateTimeOffset? at = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var running = await food.GetRunningFastAsync(cancellationToken)
                      ?? throw new InvalidOperationException("No fast is running, so there is none to end.");
        var end = CheckTime(at ?? now, now, "A fast's end");
        if (end <= running.StartedAt)
        {
            throw new ArgumentException(
                $"A fast ends after it starts; this one began {FoodText.When(running.StartedLocal)}.");
        }

        var span = new FastSpan(running.StartedAt, end, running.Id);
        await RefuseOverlapAsync(span, cancellationToken);
        var ids = await food.AppendAsync(
            [FastEvent(running.Id, running.StartedAt, end, occurredAt: end, now)],
            new FoodGuard(running.Id, span), cancellationToken);
        var fast = await food.GetFastAsync(running.Id, cancellationToken);
        return await ResultAsync(
            FoodActions.FastEnded, null, fast, null, Days(fast?.EndedOn), ids[0], cancellationToken);
    }

    /// <summary>Records a past fast from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public async Task<FoodWriteResult> RecordFastAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var (start, end) = CheckSpan(from, to, now);
        var span = new FastSpan(start, end);
        await RefuseOverlapAsync(span, cancellationToken);
        var fastId = Guid.NewGuid();
        var ids = await food.AppendAsync(
            [FastEvent(fastId, start, end, occurredAt: end, now)], new FoodGuard(Span: span), cancellationToken);
        var fast = await food.GetFastAsync(fastId, cancellationToken);
        return await ResultAsync(
            FoodActions.FastRecorded, null, fast, null, Days(fast?.EndedOn), ids[0], cancellationToken);
    }

    /// <summary>
    /// Changes a fast's times. Leaving <paramref name="to"/> out keeps a running fast running;
    /// a fast that has ended needs its end.
    /// </summary>
    public async Task<FoodWriteResult> EditFastAsync(
        string reference, DateTimeOffset from, DateTimeOffset? to, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var fast = await ResolveFastAsync(reference, cancellationToken);
        DateTimeOffset start;
        DateTimeOffset? end;
        if (to is null)
        {
            if (!fast.Running)
            {
                throw new ArgumentException("Give the fast an end time: it has already ended.");
            }

            start = CheckTime(from, now, "A fast");
            end = null;
        }
        else
        {
            (start, var finish) = CheckSpan(from, to.Value, now);
            end = finish;
        }

        var span = new FastSpan(start, end, fast.Id);
        await RefuseOverlapAsync(span, cancellationToken);
        var guard = new FoodGuard(fast.Running ? fast.Id : null, span);
        var ids = await food.AppendAsync(
            [FastEvent(fast.Id, start, end, occurredAt: end ?? start, now)], guard, cancellationToken);
        var changed = await food.GetFastAsync(fast.Id, cancellationToken);
        return await ResultAsync(
            FoodActions.FastEdited, null, changed, null, Days(fast.EndedOn, changed?.EndedOn), ids[0], cancellationToken);
    }

    /// <summary>Deletes a fast: a voided version.</summary>
    public async Task<FoodWriteResult> DeleteFastAsync(string reference, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var fast = await ResolveFastAsync(reference, cancellationToken);
        var ids = await food.AppendAsync(
            [Voided(EventKinds.Activity, "fast", fast.Id, now)], FoodGuard.None, cancellationToken);
        return await ResultAsync(
            FoodActions.FastDeleted, null, fast, null, Days(fast.EndedOn), ids[0], cancellationToken);
    }

    // ---------------------------------------------------------------------------------
    // Days
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Closes <paramref name="day"/> (default today): its food pays (food-log plan §3.3). A day
    /// already closed — by hand, or because it's over — writes nothing, but still settles, so a
    /// day that closed itself pays now if it hasn't.
    /// </summary>
    public async Task<FoodWriteResult> CloseDayAsync(DateOnly? day = null, CancellationToken cancellationToken = default)
    {
        var today = await food.TodayAsync(cancellationToken);
        var target = day ?? today;
        if (target > today)
        {
            throw new ArgumentException($"{target:yyyy-MM-dd} hasn't happened yet, so it can't be closed.");
        }

        var existing = (await food.GetDaysAsync(target, target, cancellationToken)).FirstOrDefault();
        if (target < today || existing is { ClosedByHand: true })
        {
            return await ResultAsync(FoodActions.AlreadyClosed, null, null, target, [target], null, cancellationToken);
        }

        var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new { food_day = new { on = target.ToString("yyyy-MM-dd"), closed = true } });
        var ids = await food.AppendAsync([Declared(EventKinds.Activity, now, now, payload)], FoodGuard.None, cancellationToken);
        return await ResultAsync(FoodActions.Closed, null, null, target, [target], ids[0], cancellationToken);
    }

    /// <summary>One day of the Food Log (default today): see <see cref="FoodReads.GetDayAsync"/>.</summary>
    public Task<FoodDayView> GetDayAsync(DateOnly? day = null, CancellationToken cancellationToken = default)
        => reads.GetDayAsync(day, cancellationToken);

    /// <summary>Every day of a range, at most <see cref="MaxRangeDays"/>: see <see cref="FoodReads.GetDaysAsync"/>.</summary>
    public Task<FoodRange> GetDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => reads.GetDaysAsync(from, to, cancellationToken);

    /// <summary>The drinks drunk in a range: see <see cref="FoodReads.GetDrinksAsync(DateTimeOffset, DateTimeOffset, CancellationToken)"/>.</summary>
    public Task<DrinkTally> GetDrinksAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => reads.GetDrinksAsync(from, to, cancellationToken);

    /// <summary>The drinks drunk over home-zone days: see <see cref="FoodReads.GetDrinksAsync(DateOnly, DateOnly, CancellationToken)"/>.</summary>
    public Task<DrinkTally> GetDrinksAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        => reads.GetDrinksAsync(from, to, cancellationToken);

    /// <summary>What "Log again" offers: see <see cref="FoodReads.GetRecentAsync"/>.</summary>
    public Task<IReadOnlyList<Meal>> GetRecentAsync(int limit = 20, CancellationToken cancellationToken = default)
        => reads.GetRecentAsync(limit, cancellationToken);

    /// <summary>
    /// Settles every food day with the ledger (food-log plan §3.4) — what the Food Log page
    /// calls when it opens, so a day that closed itself pays then. Never opens the wallet.
    /// </summary>
    public Task<Earned> SettleAsync(CancellationToken cancellationToken = default)
        => rewards.SettleAsync(cancellationToken: cancellationToken);

    /// <summary>A home-zone wall-clock time as an instant (for hosts reading "12:30").</summary>
    public Task<DateTimeOffset> InstantAtAsync(DateTime local, CancellationToken cancellationToken = default)
        => food.InstantAtAsync(local, cancellationToken);

    /// <summary>An instant as home-zone wall-clock time (for hosts printing "Mon Oct 19, 8:00 pm").</summary>
    public Task<DateTime> LocalAtAsync(DateTimeOffset instant, CancellationToken cancellationToken = default)
        => food.LocalAtAsync(instant, cancellationToken);

    /// <summary>Today in the home zone.</summary>
    public Task<DateOnly> TodayAsync(CancellationToken cancellationToken = default) => food.TodayAsync(cancellationToken);

    /// <summary>A live food entry named by its id or the start of it (at least 4 hex digits).</summary>
    public async Task<Meal> ResolveMealAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(reference, out var id))
        {
            return await food.GetMealAsync(id, cancellationToken)
                   ?? throw new InvalidOperationException($"No food entry has the id {id} (it may have been deleted).");
        }

        var matches = await food.FindMealsAsync(CheckPrefix(reference), cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No food entry's id starts with '{reference}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"'{reference}' starts {matches.Count} food entries' ids: " +
                string.Join("; ", matches.Take(5).Select(m => $"{Short(m.Id)} {m.Name} ({FoodText.When(m.EatenLocal)})")) +
                ". Give more of the id.")
        };
    }

    /// <summary>A live fast named by its id or the start of it (at least 4 hex digits).</summary>
    public async Task<Fast> ResolveFastAsync(string reference, CancellationToken cancellationToken = default)
    {
        if (Guid.TryParse(reference, out var id))
        {
            return await food.GetFastAsync(id, cancellationToken)
                   ?? throw new InvalidOperationException($"No fast has the id {id} (it may have been deleted).");
        }

        var matches = await food.FindFastsAsync(CheckPrefix(reference), cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException($"No fast's id starts with '{reference}'."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"'{reference}' starts {matches.Count} fasts' ids: " +
                string.Join("; ", matches.Take(5).Select(f => $"{Short(f.Id)} {FoodText.Span(f)}")) +
                ". Give more of the id.")
        };
    }

    /// <summary>The first 8 characters of an id, as the CLI prints it.</summary>
    public static string Short(Guid id) => id.ToString("N")[..8];

    // ---------------------------------------------------------------------------------
    // Checks and events
    // ---------------------------------------------------------------------------------

    private async Task<NormalizedEntry> NormalizeAsync(
        FoodEntryInput input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var name = (input.Name ?? "").Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("Say what it was: a name is required.");
        }

        if (name.Length > FoodEntries.NameMaxLength)
        {
            throw new ArgumentException($"Keep the name to {FoodEntries.NameMaxLength} characters.");
        }

        // A drink's score is always the lowest (FOOD-6), whatever was sent.
        var alcoholic = input.Alcoholic ?? false;
        var score = alcoholic ? FoodEntries.DrinkScore : input.Score;
        if (!FoodEntries.IsScore(score))
        {
            throw new ArgumentException(
                $"The health score is {FoodEntries.ScoreText(FoodEntries.ScoreMin)} to {FoodEntries.ScoreText(FoodEntries.ScoreMax)} " +
                $"in whole or half points, like 3 or 3.5 (1 = junk, 5 = very healthy); got {input.Score.ToString(CultureInfo.InvariantCulture)}.");
        }

        score = FoodEntries.TidyScore(score);

        if (input.Calories is < 0 or > FoodEntries.CaloriesMax)
        {
            throw new ArgumentException($"Calories are a whole number from 0 to {FoodEntries.CaloriesMax:N0}; got {input.Calories}.");
        }

        CheckGrams(input.ProteinG, "Protein");
        CheckGrams(input.CarbsG, "Carbs");
        CheckGrams(input.FatG, "Fat");

        string? mealType = null;
        if (!string.IsNullOrWhiteSpace(input.MealType))
        {
            mealType = FoodEntries.FindMealType(input.MealType)
                       ?? throw new ArgumentException(
                           $"Unknown meal type '{input.MealType}'. Expected one of: {string.Join(", ", FoodEntries.MealTypes)}.");
        }

        var eatenAt = CheckTime(input.EatenAt ?? now, now, "A food entry");
        var local = await food.LocalAtAsync(eatenAt, cancellationToken);
        return new NormalizedEntry(
            name, score, input.Calories, eatenAt, DateOnly.FromDateTime(local),
            mealType ?? (alcoholic ? FoodEntries.Drink : FoodEntries.GuessMealType(TimeOnly.FromDateTime(local))),
            input.ProteinG, input.CarbsG, input.FatG, alcoholic);
    }

    private static void CheckGrams(int? grams, string what)
    {
        if (grams is < 0 or > FoodEntries.GramsMax)
        {
            throw new ArgumentException($"{what} is whole grams from 0 to {FoodEntries.GramsMax:N0}; got {grams}.");
        }
    }

    /// <summary>
    /// Checks a time and keeps it to the whole second: food times need no finer, and a time
    /// that reaches the database both as a column and inside a payload then reads back the same
    /// from each.
    /// </summary>
    private static DateTimeOffset CheckTime(DateTimeOffset at, DateTimeOffset now, string what)
    {
        at = at.AddTicks(-(at.Ticks % TimeSpan.TicksPerSecond));
        if (at > now + Skew)
        {
            throw new ArgumentException($"{what} can't be in the future.");
        }

        if (at < Earliest)
        {
            throw new ArgumentException($"{what} can't be before the year 2000.");
        }

        return at;
    }

    private static (DateTimeOffset From, DateTimeOffset To) CheckSpan(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        from = CheckTime(from, now, "A fast");
        to = CheckTime(to, now, "A fast's end");
        if (to <= from)
        {
            throw new ArgumentException("A fast ends after it starts.");
        }

        return (from, to);
    }

    private static string CheckPrefix(string reference)
    {
        var trimmed = (reference ?? "").Trim();
        if (trimmed.Length < 4 || !trimmed.All(c => Uri.IsHexDigit(c) || c == '-'))
        {
            throw new ArgumentException($"'{reference}' isn't an id: give its first 8 characters, as the list shows them.");
        }

        return trimmed;
    }

    private async Task RefuseOverlapAsync(FastSpan span, CancellationToken cancellationToken)
    {
        var overlap = (await food.GetOverlappingFastsAsync(span, cancellationToken)).FirstOrDefault();
        if (overlap is not null)
        {
            throw new InvalidOperationException(
                $"That overlaps your fast {FoodText.Span(overlap)}. Change or delete that fast first.");
        }
    }

    private NewEvent MealEvent(Guid id, NormalizedEntry entry, Guid? photo, DateTimeOffset now)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                meal = new
                {
                    id,
                    name = entry.Name,
                    score = entry.Score,
                    calories = entry.Calories,
                    protein_g = entry.ProteinG,
                    carbs_g = entry.CarbsG,
                    fat_g = entry.FatG,
                    meal_type = entry.MealType,
                    photo,
                    alcoholic = entry.Alcoholic ? true : (bool?)null
                }
            },
            OmitNulls);
        return Declared(EventKinds.Measurement, entry.EatenAt, now, payload);
    }

    private NewEvent FastEvent(
        Guid id, DateTimeOffset start, DateTimeOffset? end, DateTimeOffset occurredAt, DateTimeOffset now)
    {
        var payload = JsonSerializer.Serialize(
            new { fast = new { id, started_at = start.ToUniversalTime(), ended_at = end?.ToUniversalTime() } },
            OmitNulls);
        return Declared(EventKinds.Activity, occurredAt, now, payload);
    }

    private NewEvent Voided(string kind, string key, Guid id, DateTimeOffset now)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object> { [key] = new { id, voided = true } });
        return Declared(kind, now, now, payload);
    }

    private NewEvent Declared(string kind, DateTimeOffset occurredAt, DateTimeOffset now, string payload)
        => new(kind, Provenances.Declared, occurredAt, now, sourceId, PayloadJson: payload);

    private static IReadOnlyList<DateOnly> Days(params DateOnly?[] days)
        => days.Where(d => d is not null).Select(d => d!.Value).Distinct().ToList();

    /// <summary>
    /// A write's result, after settling the food days (food-log plan §3.4): every write
    /// settles them all, so a day that closed itself pays the moment anything is logged.
    /// </summary>
    private async Task<FoodWriteResult> ResultAsync(
        string action, Meal? meal, Fast? fast, DateOnly? day, IReadOnlyList<DateOnly> days, Guid? cause,
        CancellationToken cancellationToken)
    {
        var touched = days.Distinct().Order().ToList();
        var earned = await rewards.SettleAsync(cause, touched, cancellationToken);
        return new FoodWriteResult(action, meal, fast, day, touched, earned);
    }

    /// <summary>An entry after its checks: trimmed, its time and day settled, its type known, a drink's score 1.</summary>
    private sealed record NormalizedEntry(
        string Name, decimal Score, int Calories, DateTimeOffset EatenAt, DateOnly EatenOn, string MealType,
        int? ProteinG, int? CarbsG, int? FatG, bool Alcoholic);
}

/// <summary>
/// A food entry as a host sends it (FOOD-1). <paramref name="EatenAt"/> defaults to now and
/// <paramref name="MealType"/> to a guess from the home-zone time; the nutrients are optional
/// whole grams. <paramref name="Score"/> is 1 to 5 in whole or half points (3.5).
/// <paramref name="Alcoholic"/> marks one alcoholic drink (FOOD-6): its score is then always 1
/// and its meal type defaults to a drink. Left out, a new entry isn't a drink and an edit keeps
/// what the entry was.
/// </summary>
public sealed record FoodEntryInput(
    string Name,
    decimal Score,
    int Calories,
    DateTimeOffset? EatenAt = null,
    string? MealType = null,
    int? ProteinG = null,
    int? CarbsG = null,
    int? FatG = null,
    bool? Alcoholic = null);

/// <summary>What a food write did (<see cref="FoodActions"/>).</summary>
public static class FoodActions
{
    public const string Logged = "logged";
    public const string Edited = "edited";
    public const string Deleted = "deleted";
    public const string PhotoSet = "photo_set";
    public const string PhotoRemoved = "photo_removed";
    public const string FastBegun = "fast_begun";
    public const string FastEnded = "fast_ended";
    public const string FastRecorded = "fast_recorded";
    public const string FastEdited = "fast_edited";
    public const string FastDeleted = "fast_deleted";
    public const string Closed = "closed";
    public const string AlreadyClosed = "already_closed";
}

/// <summary>
/// A food write's outcome: what it did, the entry or fast as it now stands (as it stood, for a
/// delete), the day it closed, the days it touched (which a reconcile re-scores), and what it
/// earned.
/// </summary>
public sealed record FoodWriteResult(
    string Action, Meal? Meal, Fast? Fast, DateOnly? Day, IReadOnlyList<DateOnly> Days, Earned Earned);

/// <summary>
/// One day of the Food Log: its entries (oldest first), the fasts that ended on it, the fast
/// running now, the newest entry with calories (Begin fast offers its time), the day's facts,
/// its score, the entries eaten inside a recorded fast (entry id → fast id), what the ledger
/// pays it, and the sobriety streak as of it (null before the Food Log's first day).
/// </summary>
public sealed record FoodDayView(
    DateOnly Day,
    DateOnly Today,
    IReadOnlyList<Meal> Meals,
    IReadOnlyList<Fast> Fasts,
    Fast? Running,
    Meal? LastMeal,
    FoodDay Facts,
    DietScore Score,
    IReadOnlyDictionary<Guid, Guid> EatenDuringFast,
    FoodDayPaid Paid,
    SobrietyStreak? Sobriety = null);

/// <summary>One day's facts, its score, and what the ledger pays it, for the week and month views.</summary>
public sealed record FoodDaySummary(FoodDay Facts, DietScore Score, FoodDayPaid Paid);

/// <summary>
/// Every day of a range, oldest first, with today in the home zone, and the sobriety streak as
/// of the range's last day so far (null when that's before the Food Log's first day).
/// </summary>
public sealed record FoodRange(
    DateOnly From, DateOnly To, DateOnly Today, IReadOnlyList<FoodDaySummary> Days, SobrietyStreak? Sobriety = null);
