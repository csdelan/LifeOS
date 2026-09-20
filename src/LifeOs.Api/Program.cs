using Dapper;
using LifeOs.Api.Hosting;

namespace LifeOs.Api;

public static class Program
{
    public static void Main(string[] args)
    {
        // Postgres column names are snake_case; Dapper maps them to PascalCase
        // (expected_cadence -> ExpectedCadence). Same setting as the Pilot reader.
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        var builder = WebApplication.CreateBuilder(args);
        ApiHost.BindListenPort(builder);
        ApiHost.ConfigureServices(builder);

        var app = builder.Build();
        ApiHost.ConfigurePipeline(app);
        app.Run();
    }
}
