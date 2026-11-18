using Dapper;
using LifeOs.Api.Mcp;
using LifeOs.Domain;

namespace LifeOs.Api.Read;

/// <summary>
/// The agents that write through <c>/mcp</c>, by the source their events carry (agent-access plan
/// §5.3, step 2.4): what a read needs to say which agent wrote a row ("via Claude"). The client's
/// Person is in <c>bsk_ops.agent_client</c>, which the reader connection never reads, so the API
/// reads them on its own (<c>AgentAccessStore.SourcesAsync</c>) and hands them to the read.
/// </summary>
public sealed class AgentSources(IReadOnlyList<AgentSource> sources)
{
    /// <summary>
    /// No client known: an agent's row still says it was an agent's, by the Person its payload
    /// names (a capture, a journal entry) or as "an agent".
    /// </summary>
    public static readonly AgentSources None = new([]);

    /// <summary>What a row says when neither its payload nor its client names anyone.</summary>
    public const string Unnamed = "an agent";

    public IReadOnlyList<AgentSource> All { get; } = sources;

    /// <summary>The parameters <see cref="Join"/> reads, added to <paramref name="parameters"/>.</summary>
    public DynamicParameters AddTo(DynamicParameters parameters)
    {
        parameters.Add("agentSources", All.Select(s => s.SourceId).ToArray());
        parameters.Add("agentPeople", All.Select(s => s.PersonId).ToArray());
        parameters.Add("agentNames", All.Select(s => s.Name).ToArray());
        parameters.Add("agentPrefix", KernelSources.Mcp + ":");
        return parameters;
    }

    /// <summary>
    /// A lateral join, <c>via</c>, with the agent that wrote the event whose source is
    /// <paramref name="source"/>: one row with its <c>name</c> and <c>person_id</c> when the source
    /// is an agent's, none otherwise. The Person is the one <paramref name="person"/> names (the
    /// Person the agent acted as when it wrote, which a capture's or a journal entry's payload
    /// keeps), or else the one its client acts as now; the name is that Person's now, or the
    /// client's.
    /// </summary>
    public static string Join(string source, string person) =>
        $"""
        LEFT JOIN LATERAL (
            SELECT coalesce(p.title, ag.name, '{Unnamed}') AS name, p.id AS person_id
            FROM (SELECT {source} AS source_id) w
            LEFT JOIN unnest(@agentSources::text[], @agentPeople::uuid[], @agentNames::text[])
                      AS ag(source_id, person_id, name) ON ag.source_id = w.source_id
            LEFT JOIN bsk.subject p ON p.id = coalesce({person}, ag.person_id)
            WHERE starts_with(w.source_id, @agentPrefix)
        ) via ON true
        """;

    /// <summary>The columns <see cref="Join"/> answers, last in a row, so a read splits them off at <c>name</c>.</summary>
    public const string Columns = "via.name, via.person_id";

    /// <summary>The Person an event's payload names as the agent's (<c>agent_person_id</c>), or null.</summary>
    public static string PayloadPerson(string alias) =>
        $"CASE WHEN bsk.is_uuid_text({alias}.payload->>'{AgentWrites.PersonField}') THEN ({alias}.payload->>'{AgentWrites.PersonField}')::uuid END";
}

/// <summary>An agent's source (<c>mcp:&lt;client_id&gt;</c>), its Person now and its name.</summary>
public sealed record AgentSource(string SourceId, Guid? PersonId, string Name);
