using System.Diagnostics;
using LifeOs.Api.Auth;
using LifeOs.Api.Mcp.Tools;
using LifeOs.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Protocol;

namespace LifeOs.Api.Mcp;

/// <summary>
/// The read-only MCP server at <c>/mcp</c> (agent-access plan §4): the tools, the server
/// instructions, and sign-in. Off unless <c>Mcp:Enabled</c> is true; off, nothing is registered
/// and <c>/mcp</c> and its metadata answer 404.
/// </summary>
public static class McpHost
{
    public const string Path = "/mcp";

    /// <summary>
    /// Registers the server when <c>Mcp:Enabled</c> is true. Call after <c>AddLifeOsAuth</c>,
    /// whose JWT validation it shares.
    /// </summary>
    public static void AddLifeOsMcp(this WebApplicationBuilder builder, string readerConnectionString, string homeTimeZone)
    {
        var options = McpOptions.From(builder.Configuration);
        if (!options.Enabled)
        {
            return;
        }

        var auth = SupabaseAuthOptions.From(builder.Configuration);
        var services = builder.Services;
        services.AddSingleton(sp => new McpReads(readerConnectionString, homeTimeZone, sp.GetRequiredService<IClock>()));

        var server = services
            .AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation
                {
                    Name = "lifeos",
                    Title = "LifeOS",
                    Version = typeof(McpHost).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                };
                o.ServerInstructions = McpGuide.Instructions;
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<SnapshotTools>()
            .WithTools<ItemTools>()
            .WithTools<RecordTools>()
            .WithTools<PlanTools>()
            .WithResources<McpGuide>()
            .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                var started = Stopwatch.GetTimestamp();
                var tool = context.Params?.Name ?? "?";
                var client = context.User is { } user ? AgentClaims.ClientId(user) : null;
                var logger = context.Services?.GetService<ILoggerFactory>()?.CreateLogger("LifeOs.Api.Mcp");
                var outcome = AgentAccessStore.Error;
                int? items = null;
                try
                {
                    var result = await next(context, cancellationToken);
                    outcome = result.IsError == true ? AgentAccessStore.Error : AgentAccessStore.Ok;
                    items = result.IsError == true ? null : McpCallLog.CountItems(result);
                    logger?.LogInformation(
                        "MCP {Tool} for {ClientId}: {Outcome} in {Milliseconds} ms",
                        tool, client, outcome, Elapsed(started));
                    return result;
                }
                catch (ArgumentException e)
                {
                    // A reader refusing an argument (a range too long, a malformed day) says why in words.
                    logger?.LogInformation("MCP {Tool} for {ClientId}: refused in {Milliseconds} ms", tool, client, Elapsed(started));
                    return McpResults.Error(e.Message);
                }
                catch (Exception e) when (e is not McpException and not OperationCanceledException)
                {
                    logger?.LogError(e, "MCP {Tool} for {ClientId}: failed in {Milliseconds} ms", tool, client, Elapsed(started));
                    throw;
                }
                finally
                {
                    // The access log (§4.5). Writing it never fails the call.
                    if (client is not null && context.Services?.GetService<AgentAccessStore>() is { } access)
                    {
                        await McpCallLog.RecordAsync(
                            access,
                            new AgentCallRecord(client, tool, McpCallLog.Arguments(context.Params?.Arguments), outcome, items, Elapsed(started)),
                            logger);
                    }
                }
            }));

        if (options.Allows(McpDomains.Health))
        {
            server.WithTools<HealthTools>();
        }

        if (options.Allows(McpDomains.Food))
        {
            server.WithTools<FoodTools>();
        }

        if (options.Allows(McpDomains.Rewards))
        {
            server.WithTools<RewardsTools>();
        }

        if (options.Allows(McpDomains.Home))
        {
            server.WithTools<HomeTools>();
        }

        // Protected resource metadata (RFC 9728) at /.well-known/oauth-protected-resource/mcp,
        // naming Supabase Auth as the authorization server.
        services.AddAuthentication().AddMcp(o =>
        {
            o.Events.OnResourceMetadataRequest = context =>
            {
                var request = context.HttpContext.Request;
                context.ResourceMetadata = new ProtectedResourceMetadata
                {
                    Resource = $"{request.Scheme}://{request.Host}{Path}",
                    AuthorizationServers = [auth.Issuer],
                    BearerMethodsSupported = ["header"],
                    ResourceName = "LifeOS",
                };
                return Task.CompletedTask;
            };
        });

        // A 401 from /mcp points to that metadata, so a client can find where to sign in;
        // /api's 401s stay as they are.
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
        {
            var previous = o.Events.OnChallenge;
            o.Events.OnChallenge = async context =>
            {
                if (!context.Request.Path.StartsWithSegments(Path))
                {
                    await previous(context);
                    return;
                }

                context.HandleResponse();
                await context.HttpContext.ChallengeAsync(McpAuthenticationDefaults.AuthenticationScheme);
            };
        });
    }

    /// <summary>
    /// Maps <c>/mcp</c> for agents only (the <c>McpAgent</c> policy). Off, <c>/mcp</c> and its
    /// metadata answer 404 to anyone, rather than the sign-in every other path asks for.
    /// </summary>
    public static void MapLifeOsMcp(this WebApplication app)
    {
        if (app.Services.GetRequiredService<McpOptions>().Enabled)
        {
            app.MapMcp(Path).RequireAuthorization(AuthServiceCollectionExtensions.McpAgentPolicy);
            return;
        }

        foreach (var pattern in new[] { Path, Path + "/{**rest}", MetadataPath, MetadataPath + "/{**rest}" })
        {
            app.Map(pattern, () => Results.NotFound()).AllowAnonymous();
        }
    }

    private const string MetadataPath = "/.well-known/oauth-protected-resource";

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
