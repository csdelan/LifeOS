namespace LifeOs.Application.Abstractions;

/// <summary>
/// The object store (<see cref="IObjectStorage"/>) refused a request or couldn't be reached, or
/// gave back bytes that don't match what was stored. Nothing about it is the caller's fault, so
/// the API answers 502.
/// </summary>
public sealed class ObjectStorageException(string message, Exception? innerException = null)
    : Exception(message, innerException);
