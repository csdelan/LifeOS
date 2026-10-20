namespace LifeOs.Application.Abstractions;

/// <summary>
/// Thrown when a reconcile decided to pay or give back an award, but another write
/// changed that award first — two reconciles racing on one item. Nothing was written;
/// reconciling again from the current record is always safe.
/// </summary>
public sealed class LedgerConflictException(string awardKey)
    : Exception($"The award '{awardKey}' changed while it was being reconciled; reconcile again.")
{
    public string AwardKey { get; } = awardKey;
}
