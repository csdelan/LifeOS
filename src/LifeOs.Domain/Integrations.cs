namespace LifeOs.Domain;

/// <summary>
/// Outside services LifeOS reads from: the <c>service</c> key of a connection in
/// <c>bsk_ops.integration</c> (migration 0036, activity plan §3.1).
/// </summary>
public static class Integrations
{
    /// <summary>The Oura Ring (ACTIVITY-1..4): steps, workouts, sleep and readiness.</summary>
    public const string Oura = "oura";
}
