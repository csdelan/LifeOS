using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeOs.Domain;

namespace LifeOs.Application.Activity;

/// <summary>
/// One Oura record trimmed to what LifeOS keeps (D14, activity plan §3.2), ready to compare
/// with what LifeOS holds: what identifies it, its day, when it happened, its JSON (the value
/// under the payload's one key) and a hash of what's worth a new version.
/// </summary>
public sealed record OuraRecord(
    OuraCollection Collection, string Key, DateOnly Day, DateTimeOffset OccurredAt, string Json, string Hash)
{
    /// <summary>The event's payload: <c>{ "&lt;payload key&gt;": &lt;record&gt; }</c>.</summary>
    public string PayloadJson => $"{{{JsonSerializer.Serialize(Collection.PayloadKey)}:{Json}}}";
}

/// <summary>
/// Turns Oura's records into the payloads of §3.2, written field by field in a fixed order so
/// the same record always makes the same JSON and the same hash. Oura's own id for a daily
/// record is kept but left out of the hash: those ids aren't documented as stable, and a new
/// one alone isn't a change worth a version.
/// </summary>
public static class OuraPayloads
{
    /// <summary>How many hex digits of the SHA-256 an <c>external_id</c> carries.</summary>
    public const int HashLength = 16;

    /// <summary>What an <c>external_id</c> ends with when a record has no earlier version.</summary>
    public const string First = "first";

    public static OuraRecord Activity(OuraActivityDay day)
        => Daily(OuraCollections.DailyActivity, day.Day, day.Timestamp, day.OuraId, w =>
        {
            Int(w, "steps", day.Steps);
            Int(w, "active_calories", day.ActiveCalories);
            Int(w, "total_calories", day.TotalCalories);
            Int(w, "walking_m", day.WalkingM);
            Int(w, "high_s", day.HighS);
            Int(w, "medium_s", day.MediumS);
            Int(w, "low_s", day.LowS);
            Int(w, "sedentary_s", day.SedentaryS);
            Int(w, "resting_s", day.RestingS);
            Int(w, "non_wear_s", day.NonWearS);
            Int(w, "score", day.Score);
        });

    public static OuraRecord SleepScore(OuraSleepScore day)
        => Daily(OuraCollections.DailySleep, day.Day, day.Timestamp, day.OuraId, w =>
        {
            Int(w, "score", day.Score);
            Contributors(w, day.Contributors);
        });

    public static OuraRecord Readiness(OuraReadiness day)
        => Daily(OuraCollections.DailyReadiness, day.Day, day.Timestamp, day.OuraId, w =>
        {
            Int(w, "score", day.Score);
            Number(w, "temperature_deviation", day.TemperatureDeviation);
            Number(w, "temperature_trend_deviation", day.TemperatureTrendDeviation);
            Contributors(w, day.Contributors);
        });

    public static OuraRecord Workout(OuraWorkout workout)
    {
        var json = Write(w =>
        {
            w.WriteString("id", workout.Id);
            w.WriteString("day", Text(workout.Day));
            w.WriteString("activity", workout.Activity);
            w.WriteString("label", workout.Label);
            w.WriteString("start", Text(workout.Start));
            w.WriteString("end", Text(workout.End));
            Int(w, "calories", workout.Calories);
            Int(w, "distance_m", workout.DistanceM);
            w.WriteString("intensity", workout.Intensity);
            w.WriteString("source", workout.Source);
        });
        return new OuraRecord(OuraCollections.Workout, workout.Id, workout.Day, workout.Start, json, Hash(json));
    }

    public static OuraRecord SleepPeriod(OuraSleepPeriod period)
    {
        var json = Write(w =>
        {
            w.WriteString("id", period.Id);
            w.WriteString("day", Text(period.Day));
            w.WriteString("type", period.Type);
            w.WriteString("start", Text(period.Start));
            w.WriteString("end", Text(period.End));
            Int(w, "in_bed_s", period.InBedS);
            Int(w, "asleep_s", period.AsleepS);
            Int(w, "deep_s", period.DeepS);
            Int(w, "light_s", period.LightS);
            Int(w, "rem_s", period.RemS);
            Int(w, "awake_s", period.AwakeS);
            Int(w, "latency_s", period.LatencyS);
            Int(w, "efficiency", period.Efficiency);
            Int(w, "average_hrv", period.AverageHrv);
            Number(w, "average_hr", period.AverageHr);
            Int(w, "lowest_hr", period.LowestHr);
        });
        return new OuraRecord(OuraCollections.Sleep, period.Id, period.Day, period.Start, json, Hash(json));
    }

    public static OuraRecord Tag(OuraTag tag)
    {
        var json = Write(w =>
        {
            w.WriteString("id", tag.Id);
            w.WriteString("day", Text(tag.Day));
            w.WriteString("end_day", tag.EndDay is { } endDay ? Text(endDay) : null);
            w.WriteString("type_code", tag.TypeCode);
            w.WriteString("custom_name", tag.CustomName);
            w.WriteString("start", Text(tag.Start));
            w.WriteString("end", tag.End is { } end ? Text(end) : null);
            w.WriteString("comment", tag.Comment);
        });
        return new OuraRecord(OuraCollections.Tag, tag.Id, tag.Day, tag.Start, json, Hash(json));
    }

    /// <summary>
    /// The version that withdraws a workout, sleep period or tag Oura no longer has:
    /// <c>{id, day, voided: true}</c>, dated as the version it replaces was.
    /// </summary>
    public static OuraRecord Voided(OuraCollection collection, string key, DateOnly day, DateTimeOffset occurredAt)
    {
        if (!collection.Voidable)
        {
            throw new ArgumentException($"Oura never withdraws a {collection.Name} record.", nameof(collection));
        }

        var json = Write(w =>
        {
            w.WriteString("id", key);
            w.WriteString("day", Text(day));
            w.WriteBoolean("voided", true);
        });
        return new OuraRecord(collection, key, day, occurredAt, json, Hash(json));
    }

    /// <summary>
    /// The <c>external_id</c> of a version: <c>&lt;collection&gt;:&lt;key&gt;:&lt;hash&gt;:&lt;the
    /// event it replaces, or "first"&gt;</c>. Naming the predecessor lets a value that flips back
    /// (A → B → A) record, while a retried or racing sync's duplicate is refused.
    /// </summary>
    public static string ExternalId(OuraRecord record, Guid? replaces)
        => $"{record.Collection.Name}:{record.Key}:{record.Hash}:{(replaces is { } id ? id.ToString("D") : First)}";

    /// <summary>The first <see cref="HashLength"/> hex digits of the JSON's SHA-256, lower case.</summary>
    public static string Hash(string json)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..HashLength];

    /// <summary>A day as Oura and the views write it.</summary>
    public static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A time with Oura's offset kept, without trailing zeros in the fraction.</summary>
    public static string Text(DateTimeOffset time)
        => time.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture);

    // A daily record: identified by its day; Oura's id rides along outside the hash.
    private static OuraRecord Daily(
        OuraCollection collection, DateOnly day, DateTimeOffset? timestamp, string? ouraId, Action<Utf8JsonWriter> fields)
    {
        var hashed = Write(w =>
        {
            w.WriteString("day", Text(day));
            fields(w);
        });
        var json = Write(w =>
        {
            w.WriteString("day", Text(day));
            w.WriteString("oura_id", ouraId);
            fields(w);
        });
        var occurredAt = timestamp ?? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new OuraRecord(collection, Text(day), day, occurredAt, json, Hash(hashed));
    }

    private static string Write(Action<Utf8JsonWriter> fields)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            fields(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void Int(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is { } v)
        {
            writer.WriteNumber(name, v);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void Number(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } v && double.IsFinite(v))
        {
            writer.WriteNumber(name, v);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void Contributors(Utf8JsonWriter writer, IReadOnlyDictionary<string, int?> contributors)
    {
        writer.WriteStartObject("contributors");
        foreach (var (name, value) in contributors.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            Int(writer, name, value);
        }

        writer.WriteEndObject();
    }
}
