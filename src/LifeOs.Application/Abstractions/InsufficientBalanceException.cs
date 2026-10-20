using LifeOs.Domain;

namespace LifeOs.Application.Abstractions;

/// <summary>
/// Thrown when a redemption would take a currency below zero — there is no overdraft
/// (REWARD-3). Nothing was written. An <see cref="InvalidOperationException"/>, so every
/// door reports it as a refused request rather than a failure.
/// </summary>
public sealed class InsufficientBalanceException(string currency, long balance, long requested)
    : InvalidOperationException(
        $"You have {balance} {Currencies.LabelFor(currency)}; this needs {requested}.")
{
    public string Currency { get; } = currency;

    public long Balance { get; } = balance;

    public long Requested { get; } = requested;
}
