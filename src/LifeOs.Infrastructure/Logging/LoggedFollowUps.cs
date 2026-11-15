using LifeOs.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeOs.Infrastructure.Logging;

/// <summary>
/// The <see cref="IFollowUpLog"/>: a follow-up that failed after its write committed is logged as a
/// warning (code review CR113), on the host's logger when it has one. The catch-up and the
/// reconcile sweep heal what it left, so it's no error of the write's.
/// </summary>
public sealed partial class LoggedFollowUps(ILogger<LoggedFollowUps>? logger = null) : IFollowUpLog
{
    private readonly ILogger logger = logger ?? NullLogger<LoggedFollowUps>.Instance;

    public void Failed(string followUp, string about, Exception exception)
        => Log(logger, followUp, about, exception);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "A write to {About} landed, but {FollowUp} failed after it; the catch-up heals it.")]
    private static partial void Log(ILogger logger, string followUp, string about, Exception exception);
}
