using Npgsql;

namespace LifeOs.Infrastructure;

/// <summary>
/// The <c>bsk_writer</c> role the API writes as (migration 0071, code review CR126). It reads and
/// writes the kernel's tables but owns none, so it can't alter or drop them or switch off the
/// append-only triggers. <c>bsk migrate</c>, <c>bsk</c> and the backup keep their own roles.
/// </summary>
public static class BskWriter
{
    public const string RoleName = "bsk_writer";

    /// <summary>Where the API finds its connection as the role, beside <c>ConnectionStrings:Writer</c>.</summary>
    public const string EnvironmentVariable = "BSK_WRITER_CONNECTION_STRING";

    /// <summary>
    /// The password the tests' database server and <c>scripts/live-check.ps1</c> give the role, on
    /// this machine only. No migration sets one (the role is NOLOGIN), and
    /// <c>scripts/fly-setup.ps1</c> refuses this one.
    /// </summary>
    public const string LocalPassword = "bsk_writer";

    /// <summary>
    /// <paramref name="ownerConnectionString"/> with the role's local credentials in place of the
    /// owner's, keeping the host, database and every other setting.
    /// </summary>
    public static string ConnectionStringFrom(string ownerConnectionString)
        => new NpgsqlConnectionStringBuilder(ownerConnectionString)
        {
            Username = RoleName,
            Password = LocalPassword,
        }.ConnectionString;
}
