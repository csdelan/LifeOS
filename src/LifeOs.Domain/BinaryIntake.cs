namespace LifeOs.Domain;

/// <summary>
/// The feature a stored file belongs to (code review CR112): the Inbox's captures, or a meal's,
/// a room's or a person's photo, or a lab visit's report. Its name begins the event's
/// <c>external_id</c> (<c>&lt;name&gt;:&lt;sha256&gt;</c>), so the same file stored again for the
/// same feature collapses to its first event, while the same bytes stored for another feature are
/// an event of that feature's own. Before CR112 the key was the bare hash for every feature, so a
/// file shared to the Inbox that matched a meal's photo came back as the photo's event, and the
/// Inbox flagged it. The object key stays the hash alone: the same bytes are one object.
/// </summary>
public sealed record BinaryIntake
{
    /// <summary>A document or voice note captured to the Inbox; its event's payload is empty.</summary>
    public static readonly BinaryIntake Inbox = new("inbox", payloadKey: null);

    public static readonly BinaryIntake FoodPhoto = new("food_photo", "food_photo");

    public static readonly BinaryIntake SpacePhoto = new("space_photo", "space_photo");

    public static readonly BinaryIntake LabReport = new("lab_visit", "lab_visit");

    public static readonly BinaryIntake PersonPhoto = new("person_photo", "person_photo");

    /// <summary>Every feature that stores files; a new one is added here.</summary>
    public static IReadOnlyList<BinaryIntake> All { get; } = [Inbox, FoodPhoto, SpacePhoto, LabReport, PersonPhoto];

    private BinaryIntake(string name, string? payloadKey)
    {
        Name = name;
        PayloadKey = payloadKey;
    }

    public string Name { get; }

    /// <summary>
    /// The payload key the feature's events carry, by which one stored before CR112 (its
    /// <c>external_id</c> the bare hash) is still found as the feature's; <c>null</c> for the
    /// Inbox, whose captures carry an empty payload.
    /// </summary>
    public string? PayloadKey { get; }

    public string ExternalId(string sha256) => $"{Name}:{sha256}";

    public override string ToString() => Name;
}
