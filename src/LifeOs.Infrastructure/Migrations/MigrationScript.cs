using System.Security.Cryptography;
using System.Text;

namespace LifeOs.Infrastructure.Migrations;

/// <summary>
/// A single versioned SQL migration, named on disk as <c>NNNN__name.sql</c>.
/// </summary>
public sealed class MigrationScript
{
    public MigrationScript(long version, string name, string sql)
    {
        Version = version;
        Name = name;
        Sql = sql;
        Checksum = ComputeChecksum(sql);
    }

    /// <summary>The numeric version parsed from the <c>NNNN</c> prefix.</summary>
    public long Version { get; }

    /// <summary>The human-readable name parsed from after the <c>__</c> separator.</summary>
    public string Name { get; }

    /// <summary>The raw SQL body of the migration.</summary>
    public string Sql { get; }

    /// <summary>A stable SHA-256 hash of the SQL, used to detect edits after apply.</summary>
    public string Checksum { get; }

    private static string ComputeChecksum(string sql)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sql));
        return Convert.ToHexStringLower(bytes);
    }
}
