namespace LifeOs.Application.Abstractions;

/// <summary>
/// Thrown when a food write was decided against fasts that changed before it could land —
/// another window started a fast, or recorded one this would overlap. Nothing was written;
/// reading the Food Log again and retrying is always safe. A conflict, so the API answers 409.
/// </summary>
public sealed class FoodConflictException(string message) : KernelRefusalException(RefusalKind.Conflict, message);
