namespace LifeOs.Application.Abstractions;

/// <summary>
/// Thrown when a food write was decided against fasts that changed before it could land —
/// another window started a fast, or recorded one this would overlap. Nothing was written;
/// reading the Food Log again and retrying is always safe. A refusal in words, so hosts
/// answer it like any other (a 400 from the API).
/// </summary>
public sealed class FoodConflictException(string message) : InvalidOperationException(message);
