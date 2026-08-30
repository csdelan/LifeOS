using System.CommandLine;
using LifeOs.Cli.Commands;
using LifeOs.Infrastructure;

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

return await rootCommand.Parse(args).InvokeAsync();
