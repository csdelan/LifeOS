using System.Text.Json;
using LifeOs.Application.Abstractions;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>
/// Chris's one-line note on a workout (ACTIVITY-3, activity plan §3.2): LifeOS's one write
/// about Oura's data, behind <c>bsk oura note</c> and, from A3, the Activity page. Each change
/// is a <c>declared</c> <c>note</c> event, <c>{workout_note: {workout, text}}</c>, keyed by the
/// workout's Oura id, so syncs never touch it; the newest wins and an empty text clears it.
/// Refusals are in words, with nothing written.
/// </summary>
public sealed class WorkoutNoteService(IActivityRepository activity, IEventStore events, IClock clock, string sourceId)
{
    /// <summary>The longest note, in characters.</summary>
    public const int MaxLength = 140;

    /// <summary>How much of an Oura id names a workout, at least.</summary>
    public const int MinPrefix = 8;

    // Unicode's own line breaks, which aren't control characters.
    private const char LineSeparator = (char)0x2028;
    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Sets the note on the workout named by <paramref name="reference"/> (its Oura id or the
    /// start of it); an empty or blank <paramref name="text"/> clears it. A note the same as
    /// the one it has writes nothing. Answers the workout as it now reads.
    /// </summary>
    public async Task<Workout> SetAsync(string reference, string? text, CancellationToken cancellationToken = default)
    {
        var note = Normalize(text);
        var workout = await ResolveAsync(reference, cancellationToken);
        if ((workout.Note ?? "") == note)
        {
            return workout;
        }

        var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new { workout_note = new { workout = workout.Id, text = note } });
        await events.AppendAsync(
            new NewEvent(EventKinds.Note, Provenances.Declared, now, now, sourceId, PayloadJson: payload),
            cancellationToken);
        return await activity.GetWorkoutAsync(workout.Id, cancellationToken) ?? workout;
    }

    /// <summary>Clears the note on the workout named by <paramref name="reference"/>.</summary>
    public Task<Workout> ClearAsync(string reference, CancellationToken cancellationToken = default)
        => SetAsync(reference, "", cancellationToken);

    /// <summary>
    /// The live workout named by its Oura id, or by the first <see cref="MinPrefix"/> or more
    /// characters of it. Throws <see cref="InvalidOperationException"/> when none matches or
    /// several do, and <see cref="ArgumentException"/> for a reference too short to use.
    /// </summary>
    public async Task<Workout> ResolveAsync(string reference, CancellationToken cancellationToken = default)
    {
        var id = reference?.Trim() ?? "";
        if (id.Length == 0)
        {
            throw new ArgumentException("Name the workout by its Oura id, or the first 8 characters of it.");
        }

        if (await activity.GetWorkoutAsync(id, cancellationToken) is { } exact)
        {
            return exact;
        }

        if (id.Length < MinPrefix)
        {
            throw new ArgumentException(
                $"'{id}' is too short to name a workout: give at least the first {MinPrefix} characters of its id.");
        }

        var matches = await activity.FindWorkoutsAsync(id, cancellationToken);
        return matches.Count switch
        {
            0 => throw new InvalidOperationException(
                $"LifeOS holds no workout whose id starts with '{id}' (it may have been deleted in the Oura app, or not synced yet)."),
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"'{id}' starts {matches.Count} workouts' ids: "
                + string.Join("; ", matches.Take(5).Select(w => $"{w.Id} {w.Activity} on {OuraPayloads.Text(w.Day)}"))
                + ". Give more of the id.")
        };
    }

    /// <summary>
    /// The note as it's kept: trimmed, empty to clear. Throws <see cref="ArgumentException"/>
    /// for a line break (or any other control character) or more than <see cref="MaxLength"/>
    /// characters.
    /// </summary>
    public static string Normalize(string? text)
    {
        var note = text?.Trim() ?? "";
        if (note.Any(c => char.IsControl(c) || c == LineSeparator || c == ParagraphSeparator))
        {
            throw new ArgumentException("A workout note is one line: take out the line break.");
        }

        if (note.Length > MaxLength)
        {
            throw new ArgumentException($"A workout note is up to {MaxLength} characters; that one is {note.Length}.");
        }

        return note;
    }
}
