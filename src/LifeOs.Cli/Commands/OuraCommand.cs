using System.CommandLine;
using LifeOs.Application.Activity;
using LifeOs.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli.Commands;

/// <summary>
/// <c>bsk oura</c> — the Oura connection (ACTIVITY-1, activity plan §3.1, §3.6). The CLI never
/// connects: it uses the connection made on the Activity page, with <c>Oura__ClientId</c>,
/// <c>Oura__ClientSecret</c> and <c>Integrations__TokenKey</c> from the environment. The
/// database it points at decides which connection that is (dev and staging each have their own).
/// <list type="bullet">
/// <item><c>bsk oura status</c> — the connection's state. Never prints a token.</item>
/// <item><c>bsk oura refresh</c> — spends the refresh token for a new pair now, to check
/// refreshing works (activity plan A6).</item>
/// </list>
/// </summary>
internal static class OuraCommand
{
    public static Command Create(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("oura", "Your Oura Ring connection: 'oura status', 'oura refresh'.");
        command.Subcommands.Add(CreateStatus(connectionOption, jsonOption));
        command.Subcommands.Add(CreateRefresh(connectionOption, jsonOption));
        return command;
    }

    private static Command CreateStatus(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("status", "Show whether Oura is connected, its scopes and the last sync.");
        command.SetAction((parseResult, cancellationToken) => Run(
            parseResult, connectionOption, jsonOption,
            (oura, ct) => oura.GetStatusAsync(ct), cancellationToken));
        return command;
    }

    private static Command CreateRefresh(Option<string?> connectionOption, Option<bool> jsonOption)
    {
        var command = new Command("refresh", "Refresh Oura's access token now (refresh tokens are single-use).");
        command.SetAction((parseResult, cancellationToken) => Run(
            parseResult, connectionOption, jsonOption,
            (oura, ct) => oura.RefreshAsync(ct), cancellationToken));
        return command;
    }

    private static Task<int> Run(
        ParseResult parseResult, Option<string?> connectionOption, Option<bool> jsonOption,
        Func<OuraConnectionService, CancellationToken, Task<OuraStatus>> action, CancellationToken cancellationToken)
    {
        var connectionString = KernelConnectionString.Resolve(parseResult.GetValue(connectionOption));
        var asJson = parseResult.GetValue(jsonOption);

        return Cli.RunAsync(asJson, async () =>
        {
            await using var provider = Cli.BuildServices(connectionString);
            var status = await action(provider.GetRequiredService<OuraConnectionService>(), cancellationToken);
            if (asJson)
            {
                Cli.WriteJson(status);
            }
            else
            {
                Write(status);
            }

            return 0;
        });
    }

    private static void Write(OuraStatus status)
    {
        Console.WriteLine(status.State switch
        {
            OuraStatus.NotConfigured =>
                "Oura isn't set up here: set Oura__ClientId, Oura__ClientSecret and Integrations__TokenKey.",
            OuraStatus.NotConnected => "Oura isn't connected: press Connect Oura on the Activity page.",
            OuraStatus.Reconnect => $"Oura needs reconnecting: {status.ReconnectReason ?? "Oura refused the stored sign-in."}",
            _ => "Oura is connected."
        });

        if (status.ConnectedAt is { } connectedAt)
        {
            Console.WriteLine($"  Connected     {RewardOutput.Stamp(connectedAt)}");
        }

        if (status.ExpiresAt is { } expiresAt)
        {
            Console.WriteLine($"  Token until   {RewardOutput.Stamp(expiresAt)} (refreshed before then)");
        }

        if (status.Scopes.Count > 0)
        {
            Console.WriteLine($"  Scopes        {string.Join(", ", status.Scopes)}");
        }

        if (status.MissingScopes.Count > 0)
        {
            Console.WriteLine($"  Not granted   {string.Join(", ", status.MissingScopes)}: reconnect and allow them");
        }

        if (status.LastSyncedAt is { } syncedAt)
        {
            Console.WriteLine($"  Last sync     {RewardOutput.Stamp(syncedAt)}");
        }
        else if (status.Connected)
        {
            Console.WriteLine("  Last sync     never");
        }

        if (status.LastSyncError is { } error)
        {
            Console.WriteLine($"  Sync error    {error}");
        }
    }
}
