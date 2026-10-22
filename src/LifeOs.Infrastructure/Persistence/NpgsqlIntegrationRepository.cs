using System.Globalization;
using System.Security.Cryptography;
using Dapper;
using LifeOs.Application.Abstractions;
using LifeOs.Infrastructure.Integrations;
using Npgsql;

namespace LifeOs.Infrastructure.Persistence;

/// <summary>
/// Connections to outside services in <c>bsk_ops.integration</c> (migration 0036, activity plan
/// §3.1, §3.5). Tokens are sealed with <see cref="TokenCipher"/> on the way in and opened on the
/// way out, so nothing above this class sees them encrypted, and nothing in the database sees
/// them plain. A host without a token key can still read a connection's state; storing or
/// reading tokens then refuses in words.
/// </summary>
public sealed class NpgsqlIntegrationRepository(string connectionString, TokenCipher? cipher) : IIntegrationRepository
{
    // Aliased to the row's property names: the CLI host doesn't turn on Dapper's underscore
    // matching. Dates as text, since Dapper has no DateOnly mapping in every host.
    private const string StateColumns = """
        service, access_token_enc IS NOT NULL AS connected, connected_at AS connectedat,
        expires_at AS expiresat, scopes, needs_reconnect AS needsreconnect,
        reconnect_reason AS reconnectreason, last_synced_at AS lastsyncedat,
        last_sync_error AS lastsyncerror, history_from::text AS historyfrom
        """;

    public async Task<IntegrationState?> GetAsync(string service, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<StateRow>(new CommandDefinition(
            $"SELECT {StateColumns} FROM bsk_ops.integration WHERE service = @service;",
            new { service },
            cancellationToken: cancellationToken));
        return row?.ToState();
    }

    public async Task BeginConnectAsync(string service, PendingConnect pending, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk_ops.integration
                (service, pending_state, pending_verifier, pending_redirect_uri, pending_until)
            VALUES (@service, @state, @verifier, @redirect, @until)
            ON CONFLICT (service) DO UPDATE SET
                pending_state = EXCLUDED.pending_state,
                pending_verifier = EXCLUDED.pending_verifier,
                pending_redirect_uri = EXCLUDED.pending_redirect_uri,
                pending_until = EXCLUDED.pending_until,
                updated_at = now();
            """,
            new
            {
                service,
                state = pending.State,
                verifier = pending.CodeVerifier,
                redirect = pending.RedirectUri.AbsoluteUri,
                until = pending.Until.UtcDateTime
            },
            cancellationToken: cancellationToken));
    }

    public async Task<PendingConnect?> TakePendingAsync(
        string service, string state, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        // Read and clear in one statement, so a state is good for one connect only.
        var row = await connection.QueryFirstOrDefaultAsync<PendingRow>(new CommandDefinition(
            """
            WITH taken AS (
                SELECT service, pending_state, pending_verifier, pending_redirect_uri, pending_until
                FROM bsk_ops.integration
                WHERE service = @service AND pending_state = @state
                FOR UPDATE)
            UPDATE bsk_ops.integration i SET
                pending_state = NULL, pending_verifier = NULL,
                pending_redirect_uri = NULL, pending_until = NULL, updated_at = now()
            FROM taken
            WHERE i.service = taken.service
            RETURNING taken.pending_state AS state, taken.pending_verifier AS verifier,
                      taken.pending_redirect_uri AS redirect, taken.pending_until AS until;
            """,
            new { service, state },
            cancellationToken: cancellationToken));
        return row is null
            ? null
            : new PendingConnect(row.State, row.Verifier, new Uri(row.Redirect, UriKind.Absolute), Utc(row.Until));
    }

    public async Task SaveConnectionAsync(
        string service, OAuthTokens tokens, DateTimeOffset connectedAt, CancellationToken cancellationToken = default)
    {
        var sealedTokens = Seal(service, tokens);
        await using var connection = await OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO bsk_ops.integration
                (service, access_token_enc, refresh_token_enc, expires_at, scopes, connected_at)
            VALUES (@service, @access, @refresh, @expires, @scopes, @connectedAt)
            ON CONFLICT (service) DO UPDATE SET
                access_token_enc = EXCLUDED.access_token_enc,
                refresh_token_enc = EXCLUDED.refresh_token_enc,
                expires_at = EXCLUDED.expires_at,
                scopes = EXCLUDED.scopes,
                connected_at = EXCLUDED.connected_at,
                needs_reconnect = false,
                reconnect_reason = NULL,
                pending_state = NULL, pending_verifier = NULL,
                pending_redirect_uri = NULL, pending_until = NULL,
                updated_at = now();
            """,
            new
            {
                service,
                access = sealedTokens.Access,
                refresh = sealedTokens.Refresh,
                expires = tokens.ExpiresAt.UtcDateTime,
                scopes = tokens.Scopes.ToArray(),
                connectedAt = connectedAt.UtcDateTime
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IIntegrationLock?> LockAsync(string service, CancellationToken cancellationToken = default)
    {
        var connection = await OpenAsync(cancellationToken);
        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            var row = await connection.QueryFirstOrDefaultAsync<LockRow>(new CommandDefinition(
                """
                SELECT access_token_enc AS access, refresh_token_enc AS refresh, expires_at AS expiresat,
                       scopes, needs_reconnect AS needsreconnect
                FROM bsk_ops.integration
                WHERE service = @service
                FOR UPDATE;
                """,
                new { service },
                transaction,
                cancellationToken: cancellationToken));
            if (row is null)
            {
                await transaction.DisposeAsync();
                await connection.DisposeAsync();
                return null;
            }

            var (tokens, unreadable) = Open(service, row);
            return new Lock(this, service, connection, transaction, tokens, unreadable, row.NeedsReconnect);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }

            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private TokenCipher RequireCipher()
        => cipher ?? throw new OuraConnectionException(
            $"This host has no {TokenCipher.KeyKey}, so it can't store or read integration tokens.");

    private (byte[] Access, byte[] Refresh) Seal(string service, OAuthTokens tokens)
    {
        var key = RequireCipher();
        return (key.Encrypt(tokens.AccessToken, $"{service}.access_token"),
                key.Encrypt(tokens.RefreshToken, $"{service}.refresh_token"));
    }

    private (OAuthTokens? Tokens, bool Unreadable) Open(string service, LockRow row)
    {
        if (row.Access is null || row.Refresh is null || row.ExpiresAt is null)
        {
            return (null, false);
        }

        if (cipher is null)
        {
            return (null, true);
        }

        try
        {
            return (new OAuthTokens(
                cipher.Decrypt(row.Access, $"{service}.access_token"),
                cipher.Decrypt(row.Refresh, $"{service}.refresh_token"),
                Utc(row.ExpiresAt.Value),
                row.Scopes ?? []), false);
        }
        catch (CryptographicException)
        {
            return (null, true);
        }
    }

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? Utc(DateTime? value) => value is { } v ? Utc(v) : null;

    /// <summary>A row locked with <c>FOR UPDATE</c> until commit or dispose.</summary>
    private sealed class Lock(
        NpgsqlIntegrationRepository owner,
        string service,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OAuthTokens? tokens,
        bool unreadable,
        bool needsReconnect) : IIntegrationLock
    {
        private bool committed;

        public OAuthTokens? Tokens { get; private set; } = tokens;
        public bool TokensUnreadable { get; private set; } = unreadable;
        public bool NeedsReconnect { get; private set; } = needsReconnect;

        public async Task SaveTokensAsync(OAuthTokens fresh, CancellationToken cancellationToken = default)
        {
            var sealedTokens = owner.Seal(service, fresh);
            await ExecuteAsync(
                """
                UPDATE bsk_ops.integration SET
                    access_token_enc = @access, refresh_token_enc = @refresh,
                    expires_at = @expires, scopes = @scopes, updated_at = now()
                WHERE service = @service;
                """,
                new
                {
                    service,
                    access = sealedTokens.Access,
                    refresh = sealedTokens.Refresh,
                    expires = fresh.ExpiresAt.UtcDateTime,
                    scopes = fresh.Scopes.ToArray()
                },
                cancellationToken);
            Tokens = fresh;
            TokensUnreadable = false;
        }

        public async Task MarkReconnectAsync(string reason, CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(
                """
                UPDATE bsk_ops.integration SET
                    needs_reconnect = true, reconnect_reason = @reason, updated_at = now()
                WHERE service = @service;
                """,
                new { service, reason },
                cancellationToken);
            NeedsReconnect = true;
        }

        public async Task ClearTokensAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteAsync(
                """
                UPDATE bsk_ops.integration SET
                    access_token_enc = NULL, refresh_token_enc = NULL, expires_at = NULL,
                    scopes = '{}', connected_at = NULL,
                    needs_reconnect = false, reconnect_reason = NULL,
                    pending_state = NULL, pending_verifier = NULL,
                    pending_redirect_uri = NULL, pending_until = NULL,
                    updated_at = now()
                WHERE service = @service;
                """,
                new { service },
                cancellationToken);
            Tokens = null;
            TokensUnreadable = false;
            NeedsReconnect = false;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await transaction.CommitAsync(cancellationToken);
            committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync();
                }
                catch (InvalidOperationException)
                {
                    // Already completed (or the connection broke): nothing to roll back.
                }
                catch (NpgsqlException)
                {
                    // The connection broke; Postgres rolls back on its own.
                }
            }

            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }

        private Task<int> ExecuteAsync(string sql, object parameters, CancellationToken cancellationToken)
            => connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
    }

    private sealed class StateRow
    {
        public string Service { get; set; } = "";
        public bool Connected { get; set; }
        public DateTime? ConnectedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string[]? Scopes { get; set; }
        public bool NeedsReconnect { get; set; }
        public string? ReconnectReason { get; set; }
        public DateTime? LastSyncedAt { get; set; }
        public string? LastSyncError { get; set; }
        public string? HistoryFrom { get; set; }

        public IntegrationState ToState() => new(
            Service, Connected, Utc(ConnectedAt), Utc(ExpiresAt), Scopes ?? [], NeedsReconnect, ReconnectReason,
            Utc(LastSyncedAt), LastSyncError,
            HistoryFrom is null ? null : DateOnly.ParseExact(HistoryFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private sealed class PendingRow
    {
        public string State { get; set; } = "";
        public string Verifier { get; set; } = "";
        public string Redirect { get; set; } = "";
        public DateTime Until { get; set; }
    }

    private sealed class LockRow
    {
        public byte[]? Access { get; set; }
        public byte[]? Refresh { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string[]? Scopes { get; set; }
        public bool NeedsReconnect { get; set; }
    }
}
