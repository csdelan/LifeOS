using LifeOs.Cli;
using LifeOs.Infrastructure;

var bsk = BskRoot.Create();
var parsed = bsk.Command.Parse(args);
// A connection string left in the environment says where it points before anything runs (code review CR59).
if (KernelConnectionString.AmbientRemoteNotice(
        parsed.GetValue(bsk.Connection),
        Environment.GetEnvironmentVariable(KernelConnectionString.EnvironmentVariable)) is { } notice)
{
    Console.Error.WriteLine(notice);
}

// A bsk older than the database won't run against it (code review CR66).
if (await StaleBskGuard.RefusalAsync(parsed, bsk.Connection, CancellationToken.None) is { } refusal)
{
    Cli.WriteError(parsed.GetValue(bsk.Json), refusal);
    return 1;
}

return await parsed.InvokeAsync();
