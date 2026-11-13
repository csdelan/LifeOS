using LifeOs.Api.Hosting;

namespace LifeOs.Api;

public static class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        ApiHost.BindListenPort(builder);
        ApiHost.ConfigureServices(builder);

        var app = builder.Build();
        ApiHost.ConfigurePipeline(app);
        app.Run();
    }
}
