using Dapper;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// How Dapper reads LifeOS rows, set once for the process by the kernel's composition root
/// (code review CR18) rather than by each host: Postgres column names are snake_case, and
/// Dapper maps them to PascalCase (<c>expected_cadence</c> to <c>ExpectedCadence</c>). The
/// WinForms Pilot, which doesn't build the kernel, sets the same.
/// </summary>
public static class DapperConventions
{
    public static void Apply() => DefaultTypeMap.MatchNamesWithUnderscores = true;
}
