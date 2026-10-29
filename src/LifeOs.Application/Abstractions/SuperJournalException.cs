namespace LifeOs.Application.Abstractions;

/// <summary>What went wrong with SuperJournal (D19).</summary>
public enum SuperJournalFailure
{
    /// <summary>This server has no <c>SuperJournal:BaseUrl</c>. The API answers 503.</summary>
    NotSetUp,

    /// <summary>What was sent can't be saved (SuperJournal's 400, or LifeOS's own check). The API answers 400.</summary>
    Invalid,

    /// <summary>SuperJournal refused LifeOS's token (401): a server setting. The API answers 502.</summary>
    Refused,

    /// <summary>The journal, entry or attachment isn't the person's (403). The API answers 403.</summary>
    Forbidden,

    /// <summary>SuperJournal has no such journal, entry or attachment (404). The API answers 404.</summary>
    NotFound,

    /// <summary>The entry changed since it was read (409): read it again. The API answers 409.</summary>
    Conflict,

    /// <summary>The upload is over the size limit (413). The API answers 413.</summary>
    TooLarge,

    /// <summary>SuperJournal couldn't be reached, timed out, failed or answered something unreadable. The API answers 502.</summary>
    Unavailable,
}

/// <summary>
/// SuperJournal couldn't do what was asked. The message says why in words and never carries a
/// token or a key. When it's thrown from a save, nothing was written to <c>bsk</c>.
/// </summary>
public sealed class SuperJournalException(SuperJournalFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>SuperJournal isn't set up on this server: the words the API answers with.</summary>
    public const string NotSetUpMessage =
        "SuperJournal isn't set up on this server: add SuperJournal__BaseUrl and SuperJournal__SigningKey.";

    public SuperJournalFailure Failure { get; } = failure;

    public static SuperJournalException NotSetUp() => new(SuperJournalFailure.NotSetUp, NotSetUpMessage);
}
