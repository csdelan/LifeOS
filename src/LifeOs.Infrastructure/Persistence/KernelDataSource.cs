using System.Collections.Concurrent;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// One <see cref="NpgsqlDataSource"/> per connection string for the life of the process (code
/// review CR18), as Npgsql already keeps one pool per connection string. The kernel's container
/// registers the owner connection's, and the API's readers take the reader connection's, so the
/// repositories open every connection from a data source rather than each from a string. One
/// place to set what every connection shares, should anything need it.
/// </summary>
public static class KernelDataSource
{
    private static readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> Shared = new(StringComparer.Ordinal);

    public static NpgsqlDataSource For(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return Shared.GetOrAdd(connectionString, cs => new Lazy<NpgsqlDataSource>(() => NpgsqlDataSource.Create(cs))).Value;
    }
}
