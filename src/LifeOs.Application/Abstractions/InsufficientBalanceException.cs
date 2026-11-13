using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

/// <summary>
/// Thrown when a redemption would take a currency below zero — there is no overdraft
/// (REWARD-3). Nothing was written. A refused request, not a failure: the API answers 400.
/// </summary>
public sealed class InsufficientBalanceException(string currency, long balance, long requested)
    : KernelRefusalException(
        RefusalKind.Invalid,
        $"You have {balance} {Currencies.LabelFor(currency)}; this needs {requested}.")
{
    public string Currency { get; } = currency;

    public long Balance { get; } = balance;

    public long Requested { get; } = requested;
}
