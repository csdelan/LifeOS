using System.Text.Json;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOs.Cli;

/// <summary>
/// Shared CLI plumbing: the composition root, the <c>--json</c> output
/// convention, and a consistent error surface / exit codes. Commands are thin
/// adapters that resolve a service, call it, and format the result through here.
/// </summary>
internal static class Cli
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ServiceProvider BuildServices(string connectionString)
        => new ServiceCollection().AddLifeOsKernel(connectionString).BuildServiceProvider();

    public static void WriteJson(object payload)
        => Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    public static void WriteError(bool asJson, string message)
    {
        if (asJson)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = message }, JsonOptions));
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }

    /// <summary>
    /// Runs a command body with the shared error surface: 0 on success (the body's
    /// own return), 130 on cancellation, 1 on any other failure.
    /// </summary>
    public static async Task<int> RunAsync(bool asJson, Func<Task<int>> body)
    {
        try
        {
            return await body();
        }
        catch (OperationCanceledException)
        {
            WriteError(asJson, "Operation cancelled.");
            return 130;
        }
        catch (AmbiguousSubjectException ex) when (asJson)
        {
            // Machine consumers get the candidates as data, not a formatted message,
            // so they can present the choice themselves.
            Console.Error.WriteLine(JsonSerializer.Serialize(
                new
                {
                    error = $"'{ex.Reference}' is ambiguous.",
                    candidates = ex.Candidates.Select(c => new { id = c.Id, urn = c.Urn, type = c.Type, title = c.Title })
                },
                JsonOptions));
            return 1;
        }
        catch (Exception ex)
        {
            WriteError(asJson, ex.Message);
            return 1;
        }
    }
}
