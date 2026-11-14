using System.CommandLine;
using LifeOs.Cli.Commands;
using LifeOs.Infrastructure;

namespace LifeOs.Cli;

/// <summary>
/// bsk's whole command tree and the two options every verb shares, so a test runs the verbs as
/// <c>bsk</c> does (<c>BskJsonTests</c>).
/// </summary>
internal sealed record BskRoot(RootCommand Command, Option<string?> Connection, Option<bool> Json)
{
    public static BskRoot Create()
    {
        var connectionOption = new Option<string?>("--connection", "-c")
        {
            Description = $"PostgreSQL connection string. Falls back to the {KernelConnectionString.EnvironmentVariable} " +
                          "environment variable, then the local development default.",
            Recursive = true
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit machine-readable JSON instead of human-readable text.",
            Recursive = true
        };

        var rootCommand = new RootCommand("bsk — the Life Kernel command-line interface.");
        rootCommand.Options.Add(connectionOption);
        rootCommand.Options.Add(jsonOption);

        rootCommand.Subcommands.Add(MigrateCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RebuildCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(CaptureCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(AttachCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(VoiceCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(ArtifactCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(JournalCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(IdeasCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(NewCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(LinkCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(UnlinkCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(StatusCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(ArchiveCommand.CreateArchive(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(ArchiveCommand.CreateRestore(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(PromoteCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(DecideCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(LogCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(SetCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RenameCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RecurCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(AdhereCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(HabitCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(TimerCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(TasksCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(FoodCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(SpaceCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RateCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(HomeCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(MetricCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(MeasureCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(LabCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(ReviewCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(OuraCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(WalletCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(LedgerCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RedeemCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(BountyCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(CoverCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RewardsCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(DomainsCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(InvolveCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(MaterializeCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RelateCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(TagCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(RenameTagCommand.Create(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(TriageCommand.CreateFlag(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(TriageCommand.CreateDrop(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(TriageCommand.CreateDismiss(connectionOption, jsonOption));
        rootCommand.Subcommands.Add(CheckCommand.Create(connectionOption, jsonOption));
        return new BskRoot(rootCommand, connectionOption, jsonOption);
    }
}
