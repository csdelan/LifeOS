using System.Text.Json;
using System.Text.Json.Nodes;
using LifeOs.Application.Subjects;
using LifeOs.Infrastructure.DependencyInjection;
using LifeOs.Infrastructure.Integrations;
using LifeOs.Infrastructure.Storage;
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

    /// <summary>
    /// The kernel on <paramref name="connectionString"/>. With <c>Storage__SupabaseSecretKey</c>
    /// (and <c>Storage__SupabaseUrl</c> or <c>VITE_SUPABASE_URL</c>) in the environment, new
    /// artifact bytes go to Supabase Storage and stored ones read from there, as the API does.
    /// With <c>Oura__ClientId</c>, <c>Oura__ClientSecret</c> and <c>Integrations__TokenKey</c>,
    /// <c>bsk oura</c> uses the Oura connection made on the Activity page.
    /// </summary>
    public static ServiceProvider BuildServices(string connectionString)
        => new ServiceCollection()
            .AddLifeOsKernel(
                connectionString,
                objectStorage: SupabaseStorageOptions.FromEnvironment(),
                integrations: IntegrationSettings.FromEnvironment())
            .BuildServiceProvider();

    /// <summary>
    /// Prints <paramref name="payload"/> as JSON. A read or write the API also answers prints the
    /// API's own shape for it, made by the same mapper in <c>LifeOs.Contracts</c> (code review
    /// CR89); <c>BskJsonTests</c> checks each one against the endpoint.
    /// </summary>
    public static void WriteJson(object payload)
        => Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    /// <summary>
    /// Prints the API's shape for a write with what <c>bsk</c> says besides it (the event it
    /// wrote, say): each property of <paramref name="extra"/> joins the payload's, and one the
    /// payload already has is a mistake, refused.
    /// </summary>
    public static void WriteJson(object payload, object extra) => WriteJson(Join(payload, extra));

    /// <summary>
    /// The API's shape for <paramref name="payload"/> with <paramref name="extra"/>'s properties
    /// beside its own, for a list whose rows each carry what <c>bsk</c> adds.
    /// </summary>
    public static JsonObject Join(object payload, object extra)
    {
        var node = JsonSerializer.SerializeToNode(payload, payload.GetType(), JsonOptions)!.AsObject();
        var added = JsonSerializer.SerializeToNode(extra, extra.GetType(), JsonOptions)!.AsObject();
        foreach (var name in added.Select(p => p.Key).ToList())
        {
            var value = added[name];
            added.Remove(name);
            if (node.ContainsKey(name))
            {
                throw new InvalidOperationException($"bsk's '{name}' would hide the API's own.");
            }

            node[name] = value;
        }

        return node;
    }

    /// <summary>
    /// One of the readers the API reads through (<c>LifeOs.Contracts</c>), on this kernel, so a
    /// <c>bsk</c> read answers what the endpoint does.
    /// </summary>
    public static T Reader<T>(IServiceProvider provider) where T : class
        => ActivatorUtilities.CreateInstance<T>(provider);

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
