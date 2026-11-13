using LifeOs.Api.Mcp;
using LifeOs.Api.Read;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.DependencyInjection;

namespace LifeOs.Tests;

/// <summary>
/// The API's readers on a test's connection, built as the API builds them: from a
/// <see cref="ReaderKernel"/> on that connection, in its zone and on its clock (code review CR81).
/// </summary>
internal static class TestReaders
{
    /// <summary>A reader kernel on <paramref name="connectionString"/>, in <paramref name="zone"/> on <paramref name="clock"/>.</summary>
    public static ReaderKernel On(string connectionString, string zone = "UTC", IClock? clock = null)
        => new(new KernelSettings(connectionString) { HomeTimeZone = zone }, connectionString, clock ?? new TestClock(DateTimeOffset.UtcNow), objectStorage: null);

    public static SubjectReader Subjects(string connectionString) => On(connectionString).Get<SubjectReader>();

    public static ChangeReader Changes(string connectionString) => On(connectionString).Get<ChangeReader>();

    public static McpReads Mcp(string connectionString, string zone, IClock clock) => On(connectionString, zone, clock).Get<McpReads>();
}
