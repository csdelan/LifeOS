namespace LifeOs.Application.Abstractions;

/// <summary>
/// Who a service's events say wrote them: a kernel host (<see cref="Domain.KernelSources"/>) or an
/// agent (<c>mcp:&lt;client_id&gt;</c>). The kernel's container holds its host's, so it can build
/// the services that take one; an agent's services are built with the agent's (code review CR18).
/// </summary>
public sealed record EventSource(string Id)
{
    public override string ToString() => Id;
}
