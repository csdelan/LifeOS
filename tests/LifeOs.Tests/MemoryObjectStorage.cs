using System.Collections.Concurrent;
using LifeOs.Application.Abstractions;

namespace LifeOs.Tests;

/// <summary>
/// An <see cref="IObjectStorage"/> in memory, standing in for Supabase Storage: it keeps what is
/// put, counts uploads, and can be told to fail or to lose or change an object.
/// </summary>
internal sealed class MemoryObjectStorage(string bucket = "test-artifacts") : IObjectStorage
{
    private readonly ConcurrentDictionary<string, byte[]> objects = new(StringComparer.Ordinal);

    public string Store => "memory";

    public string Bucket { get; } = bucket;

    /// <summary>How many uploads reached the store.</summary>
    public int Uploads { get; private set; }

    /// <summary>When set, every request fails as an unreachable store would.</summary>
    public bool Failing { get; set; }

    public IReadOnlyCollection<string> Keys => objects.Keys.ToList();

    public Task PutAsync(string key, byte[] bytes, string contentType, CancellationToken cancellationToken = default)
    {
        if (Failing)
        {
            throw new ObjectStorageException("The test store is down.");
        }

        Uploads++;
        objects.TryAdd($"{Bucket}/{key}", bytes.ToArray());
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(string bucket, string key, CancellationToken cancellationToken = default)
    {
        if (Failing)
        {
            throw new ObjectStorageException("The test store is down.");
        }

        return Task.FromResult(objects.TryGetValue($"{bucket}/{key}", out var bytes) ? bytes.ToArray() : null);
    }

    /// <summary>The bytes at <paramref name="key"/> in <see cref="Bucket"/>, or null.</summary>
    public byte[]? Peek(string key) => objects.TryGetValue($"{Bucket}/{key}", out var bytes) ? bytes : null;

    /// <summary>Replaces the object at <paramref name="key"/> (corruption a reader must catch).</summary>
    public void Overwrite(string key, byte[] bytes) => objects[$"{Bucket}/{key}"] = bytes;

    /// <summary>Loses the object at <paramref name="key"/>.</summary>
    public void Lose(string key) => objects.TryRemove($"{Bucket}/{key}", out _);
}
