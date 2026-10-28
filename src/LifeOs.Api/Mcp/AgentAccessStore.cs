using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Dapper;
using Npgsql;

namespace LifeOs.Api.Mcp;

/// <summary>
/// The agents Chris allowed in LifeOS and what each one called (migrations 0047 and 0048, agent-access plan
/// §4.5). It's on the owner connection: <c>bsk_ops</c> is never granted to <c>bsk_reader</c>, and
/// the MCP tools never see this class.
/// </summary>
/// <remarks>
/// The allow list is read on every <c>/mcp</c> request, not cached, so a revoke takes effect on the
/// agent's next call. A call refused because the client isn't allowed is logged at most once a
/// minute per client, so an agent retrying can't flood the log.
/// </remarks>
public sealed partial class AgentAccessStore(string ownerConnectionString, string homeTimeZone)
{
    /// <summary>How long the call log keeps a row.</summary>
    public const int RetentionDays = 90;

    /// <summary>The most of a call's arguments the log keeps, in characters.</summary>
    public const int MaxArgumentsLength = 1024;

    public const int DefaultCallLimit = 20;
    public const int MaxCallLimit = 200;

    public const string Ok = "ok";
    public const string Error = "error";
    public const string Refused = "refused";

    private static readonly TimeSpan RefusedLogGap = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CleanupGap = TimeSpan.FromHours(1);
    private const int MaxRefusedClientsRemembered = 1000;

    private readonly ConcurrentDictionary<string, long> lastRefusedLogged = new(StringComparer.Ordinal);
    private long nextCleanup;

    /// <summary>A client id as Supabase issues them, and as the table's CHECK allows.</summary>
    public static bool IsClientId(string? clientId) => clientId is not null && ClientIdShape().IsMatch(clientId);

    /// <summary>Whether <paramref name="clientId"/> is on the allow list and not revoked.</summary>
    public async Task<bool> IsAllowedAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM bsk_ops.agent_client WHERE client_id = @clientId AND revoked_at IS NULL);",
            new { clientId },
            cancellationToken: cancellationToken));
    }

    /// <summary>
    /// Allows a client, or allows it again after a revoke. Its name and website are what the consent
    /// page showed, and <paramref name="redirectUri"/> is where it sent the browser back to (kept
    /// when not given). Allowing one that's already allowed keeps when it was first allowed.
    /// </summary>
    /// <remarks>
    /// <paramref name="replaces"/> are clients this one takes over from (migration 0048): Claude
    /// registers a new client each time it connects again. Each must be on the list with the same
    /// name and return address, or nothing changes and an <see cref="ArgumentException"/> says
    /// why. The new client acts as the Person the most recently allowed of them acted as, when it
    /// acts as no one yet and that Person is still an AI agent in use. They're revoked, with
    /// <see cref="AgentClient.ReplacedBy"/> naming this one, and keep their own link, so what
    /// they did stays theirs. All of it happens together or not at all.
    /// </remarks>
    public async Task<AgentClient> AllowAsync(
        string clientId, string name, string? clientUri, string? redirectUri = null,
        IReadOnlyCollection<string>? replaces = null, CancellationToken cancellationToken = default)
    {
        var replaced = replaces?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (replaced.Length > 0)
        {
            var old = (await connection.QueryAsync<ReplacedRow>(new CommandDefinition(
                """
                SELECT client_id AS clientid, name, redirect_uri AS redirecturi
                FROM bsk_ops.agent_client
                WHERE client_id = ANY(@replaced)
                FOR UPDATE;
                """,
                new { replaced },
                transaction,
                cancellationToken: cancellationToken))).ToDictionary(r => r.ClientId, StringComparer.Ordinal);
            foreach (var id in replaced)
            {
                if (id == clientId)
                {
                    throw new ArgumentException("A client can't replace itself.");
                }

                if (!old.TryGetValue(id, out var row))
                {
                    throw new ArgumentException($"{id} isn't on LifeOS's list, so there's nothing to replace.");
                }

                if (row.Name != name || redirectUri is null || row.RedirectUri != redirectUri)
                {
                    throw new ArgumentException(
                        $"{id} is {row.Name}, returning to {row.RedirectUri ?? "an address LifeOS doesn't know"}. "
                        + "A client replaces only one with the same name and return address.");
                }
            }
        }

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk_ops.agent_client (client_id, name, client_uri, redirect_uri)
            VALUES (@clientId, @name, @clientUri, @redirectUri)
            ON CONFLICT (client_id) DO UPDATE SET
                name = EXCLUDED.name,
                client_uri = EXCLUDED.client_uri,
                redirect_uri = coalesce(EXCLUDED.redirect_uri, agent_client.redirect_uri),
                allowed_at = CASE WHEN agent_client.revoked_at IS NULL THEN agent_client.allowed_at ELSE now() END,
                revoked_at = NULL,
                replaced_by = NULL,
                updated_at = now();
            """,
            new { clientId, name, clientUri, redirectUri },
            transaction,
            cancellationToken: cancellationToken));
        if (replaced.Length > 0)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE bsk_ops.agent_client n
                SET person_id = carried.person_id, updated_at = now()
                FROM (
                    SELECT a.person_id
                    FROM bsk_ops.agent_client a
                    JOIN bsk.v_subject p ON p.id = a.person_id AND p.type = 'Person' AND NOT p.archived
                                        AND lower(coalesce(p.attributes->>'person_kind', 'human')) = 'ai'
                    WHERE a.client_id = ANY(@replaced)
                    ORDER BY a.allowed_at DESC
                    LIMIT 1
                ) carried
                WHERE n.client_id = @clientId AND n.person_id IS NULL;

                UPDATE bsk_ops.agent_client
                SET revoked_at = coalesce(revoked_at, now()), replaced_by = @clientId, updated_at = now()
                WHERE client_id = ANY(@replaced);
                """,
                new { clientId, replaced },
                transaction,
                cancellationToken: cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);
        return (await ListAsync(clientId, cancellationToken)).Single();
    }

    /// <summary>Revokes a client: <c>/mcp</c> refuses it from its next call. Null when it was never allowed.</summary>
    public async Task<AgentClient?> RevokeAsync(string clientId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var changed = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE bsk_ops.agent_client
            SET revoked_at = coalesce(revoked_at, now()), updated_at = now()
            WHERE client_id = @clientId;
            """,
            new { clientId },
            cancellationToken: cancellationToken));
        return changed == 0 ? null : (await ListAsync(clientId, cancellationToken)).SingleOrDefault();
    }

    /// <summary>
    /// Links a client to the AI-agent Person it acts as, or unlinks it when <paramref name="personId"/>
    /// is null. A Person that isn't an AI agent, or is archived, is refused in words.
    /// </summary>
    public async Task<AgentClient?> LinkPersonAsync(
        string clientId, Guid? personId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        if (personId is { } id)
        {
            var person = await connection.QueryFirstOrDefaultAsync<PersonRow>(new CommandDefinition(
                """
                SELECT title, lower(coalesce(attributes->>'person_kind', 'human')) AS kind, archived
                FROM bsk.v_subject
                WHERE id = @id AND type = 'Person';
                """,
                new { id },
                cancellationToken: cancellationToken));
            if (person is null)
            {
                throw new ArgumentException($"No Person has the id {id}.");
            }

            if (person.Kind != "ai" || person.Archived)
            {
                throw new ArgumentException(person.Archived
                    ? $"{person.Title} is archived. Link an agent to a Person that's in use."
                    : $"{person.Title} is a person, not an AI agent. Link an agent to a Person whose kind is AI.");
            }
        }

        var changed = await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE bsk_ops.agent_client SET person_id = @personId, updated_at = now() WHERE client_id = @clientId;",
            new { clientId, personId },
            cancellationToken: cancellationToken));
        return changed == 0 ? null : (await ListAsync(clientId, cancellationToken)).SingleOrDefault();
    }

    /// <summary>
    /// Every client on the allow list, allowed or revoked, and any other client LifeOS refused in
    /// the log's 90 days: its last call, its calls this week (Monday to Sunday in the home zone),
    /// and the same for refusals. Allowed ones first, then by name.
    /// </summary>
    public async Task<IReadOnlyList<AgentClient>> ListAsync(CancellationToken cancellationToken = default)
        => await ListAsync(clientId: null, cancellationToken);

    /// <summary>A client's latest calls, or everyone's, newest first.</summary>
    public async Task<IReadOnlyList<AgentCall>> RecentCallsAsync(
        string? clientId, int? limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AgentCall>(new CommandDefinition(
            """
            SELECT id, called_at AS calledat, client_id AS clientid, tool, arguments, outcome, items,
                   duration_ms AS durationms
            FROM bsk_ops.agent_call
            WHERE (@clientId::text IS NULL OR client_id = @clientId)
            ORDER BY called_at DESC, id DESC
            LIMIT @limit;
            """,
            new { clientId, limit = Math.Clamp(limit ?? DefaultCallLimit, 1, MaxCallLimit) },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>
    /// Logs a tool call. Every hour or so it also deletes rows older than 90 days. A failure
    /// throws; the caller decides whether that stops the call.
    /// </summary>
    public async Task RecordCallAsync(AgentCallRecord call, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk_ops.agent_call (client_id, tool, arguments, outcome, items, duration_ms)
            VALUES (@clientId, @tool, @arguments, @outcome, @items, @durationMs);
            """,
            new
            {
                clientId = call.ClientId,
                tool = call.Tool,
                arguments = Trim(call.Arguments),
                outcome = call.Outcome,
                items = call.Items,
                durationMs = (int)Math.Clamp(call.DurationMs, 0, int.MaxValue),
            },
            cancellationToken: cancellationToken));
        await CleanUpIfDueAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Logs a call refused because the client isn't allowed, at most once a minute per client.
    /// Answers whether it logged one.
    /// </summary>
    public async Task<bool> RecordRefusedAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var now = Environment.TickCount64;
        if (lastRefusedLogged.TryGetValue(clientId, out var last) && now - last < (long)RefusedLogGap.TotalMilliseconds)
        {
            return false;
        }

        if (lastRefusedLogged.Count >= MaxRefusedClientsRemembered)
        {
            lastRefusedLogged.Clear();
        }

        lastRefusedLogged[clientId] = now;
        await RecordCallAsync(new AgentCallRecord(clientId, Tool: null, Arguments: null, Refused, Items: null, DurationMs: 0), cancellationToken);
        return true;
    }

    /// <summary>Arguments longer than 1 KB, cut to fit with an ellipsis, never through a surrogate pair.</summary>
    public static string? Trim(string? arguments)
    {
        if (arguments is null || arguments.Length <= MaxArgumentsLength)
        {
            return arguments;
        }

        var cut = MaxArgumentsLength - 1;
        if (char.IsHighSurrogate(arguments[cut - 1]))
        {
            cut--;
        }

        return string.Concat(arguments.AsSpan(0, cut), "…");
    }

    private async Task<IReadOnlyList<AgentClient>> ListAsync(string? clientId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        // The week starts Monday (Postgres's date_trunc week) in the home zone.
        var rows = await connection.QueryAsync<AgentClient>(new CommandDefinition(
            """
            WITH week AS (
                SELECT date_trunc('week', now() AT TIME ZONE @zone) AT TIME ZONE @zone AS starts
            ),
            calls AS (
                SELECT client_id,
                       max(called_at) FILTER (WHERE outcome <> 'refused') AS last_call_at,
                       count(*) FILTER (WHERE outcome <> 'refused' AND called_at >= (SELECT starts FROM week)) AS calls_this_week,
                       max(called_at) FILTER (WHERE outcome = 'refused') AS last_refused_at,
                       count(*) FILTER (WHERE outcome = 'refused' AND called_at >= (SELECT starts FROM week)) AS refused_this_week
                FROM bsk_ops.agent_call
                WHERE @clientId::text IS NULL OR client_id = @clientId
                GROUP BY client_id
            ),
            clients AS (
                SELECT c.client_id, c.name, c.client_uri, c.redirect_uri, c.allowed_at, c.revoked_at,
                       c.replaced_by, c.person_id
                FROM bsk_ops.agent_client c
                WHERE @clientId::text IS NULL OR c.client_id = @clientId
                UNION ALL
                SELECT k.client_id, NULL, NULL, NULL, NULL, NULL, NULL, NULL
                FROM calls k
                WHERE k.last_refused_at IS NOT NULL
                  AND NOT EXISTS (SELECT 1 FROM bsk_ops.agent_client c WHERE c.client_id = k.client_id)
            )
            SELECT c.client_id AS clientid, c.name, c.client_uri AS clienturi, c.redirect_uri AS redirecturi,
                   c.allowed_at IS NOT NULL AND c.revoked_at IS NULL AS allowed,
                   c.allowed_at AS allowedat, c.revoked_at AS revokedat, c.replaced_by AS replacedby,
                   c.person_id AS personid, p.title AS personname,
                   k.last_call_at AS lastcallat, coalesce(k.calls_this_week, 0)::int AS callsthisweek,
                   k.last_refused_at AS lastrefusedat, coalesce(k.refused_this_week, 0)::int AS refusedthisweek
            FROM clients c
            LEFT JOIN calls k ON k.client_id = c.client_id
            LEFT JOIN bsk.v_subject p ON p.id = c.person_id
            ORDER BY (c.allowed_at IS NOT NULL AND c.revoked_at IS NULL) DESC, c.name NULLS LAST, c.client_id;
            """,
            new { clientId, zone = homeTimeZone },
            cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private async Task CleanUpIfDueAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var now = Environment.TickCount64;
        var due = Interlocked.Read(ref nextCleanup);
        if (now < due || Interlocked.CompareExchange(ref nextCleanup, now + (long)CleanupGap.TotalMilliseconds, due) != due)
        {
            return;
        }

        await connection.ExecuteAsync(new CommandDefinition(
            $"DELETE FROM bsk_ops.agent_call WHERE called_at < now() - interval '{RetentionDays} days';",
            cancellationToken: cancellationToken));
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(ownerConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,200}$")]
    private static partial Regex ClientIdShape();

    private sealed class ReplacedRow
    {
        public string ClientId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? RedirectUri { get; set; }
    }

    private sealed class PersonRow
    {
        public string Title { get; set; } = "";
        public string Kind { get; set; } = "";
        public bool Archived { get; set; }
    }
}

/// <summary>An agent on the allow list, or one LifeOS refused.</summary>
public sealed class AgentClient
{
    public string ClientId { get; set; } = "";

    /// <summary>Its name as the consent page showed it; null for a client never allowed.</summary>
    public string? Name { get; set; }

    public string? ClientUri { get; set; }

    /// <summary>
    /// Where the consent page sent the browser back to when it was allowed (migration 0048). With
    /// the name, it says which agent this is: a client with both the same replaces this one.
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>On the allow list and not revoked: <c>/mcp</c> answers it.</summary>
    public bool Allowed { get; set; }

    public DateTime? AllowedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>The client that replaced this one, when it was revoked that way.</summary>
    public string? ReplacedBy { get; set; }

    /// <summary>The AI-agent Person it acts as (GEN-15).</summary>
    public Guid? PersonId { get; set; }

    public string? PersonName { get; set; }

    public DateTime? LastCallAt { get; set; }
    public int CallsThisWeek { get; set; }
    public DateTime? LastRefusedAt { get; set; }
    public int RefusedThisWeek { get; set; }
}

/// <summary>One row of the call log.</summary>
public sealed class AgentCall
{
    public long Id { get; set; }
    public DateTime CalledAt { get; set; }
    public string ClientId { get; set; } = "";

    /// <summary>Null for a call refused before any tool ran.</summary>
    public string? Tool { get; set; }

    /// <summary>The arguments as JSON, trimmed to 1 KB.</summary>
    public string? Arguments { get; set; }

    /// <summary><c>ok</c>, <c>error</c> or <c>refused</c>.</summary>
    public string Outcome { get; set; } = "";

    /// <summary>Items in the answer's lists, when it had any.</summary>
    public int? Items { get; set; }

    public int DurationMs { get; set; }
}

/// <summary>A call to log.</summary>
public sealed record AgentCallRecord(
    string ClientId, string? Tool, string? Arguments, string Outcome, int? Items, long DurationMs);
