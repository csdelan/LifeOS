namespace LifeOs.Application.Abstractions;

/// <summary>
/// A write refused because it would break a Goal's or a Habit's gate (<c>SubjectGates</c>, code
/// review CR125): making a Goal Active without a target date, or a Habit with no Goal or Identity
/// Statement it serves. The request can't be done as asked, so every door answers it as invalid,
/// in the gate's own words.
/// </summary>
public sealed class SubjectGateException(string message) : KernelRefusalException(RefusalKind.Invalid, message);
