import { useQuery } from "@tanstack/react-query";
import { BotIcon, Loader2Icon, ShieldXIcon, TriangleAlertIcon } from "lucide-react";
import { useEffect, useMemo, useState, type ReactNode } from "react";
import { useAuth } from "@/components/auth/auth-provider";
import { AuthChrome, AuthConfigError, AuthReachError, AuthSplash, Mark } from "@/components/auth/auth-screens";
import { Button } from "@/components/ui/button";
import { getAgentsClient } from "@/lib/agents";
import { isLiveApi } from "@/lib/api-base";
import { ApiRequestError } from "@/lib/live/http";
import { fetchMe, meQueryKey } from "@/lib/me";
import {
  authorizationIdFrom,
  browser,
  forgetPendingConsent,
  getConsentClient,
  hostOf,
  isFollowable,
  rememberPendingConsent,
  scopeWords,
  type ConsentRequest,
} from "@/lib/oauth-consent";
import { isSupabaseConfigured } from "@/lib/supabase";

/**
 * `/oauth/consent` (agent-access plan §4.4): an agent Chris is connecting to LifeOS's MCP server
 * asks to read his life, and he approves or denies it here. It stands outside the app's sign-in
 * gate because it has its own: signed out, it keeps the request and signs in; signed in with an
 * account LifeOS doesn't allow, it says so and lets the request be turned down.
 */
export function OAuthConsentPage() {
  const live = isLiveApi();
  const id = useMemo(() => authorizationIdFrom(window.location.search), []);
  const { session, ready, signInWithGoogle, signOut } = useAuth();
  const client = useMemo(() => getConsentClient(), []);
  const agents = useMemo(() => getAgentsClient(), []);
  const [busy, setBusy] = useState<"approve" | "deny" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [leavingTo, setLeavingTo] = useState<string | null>(null);

  const me = useQuery({
    queryKey: meQueryKey,
    queryFn: fetchMe,
    enabled: live && ready && !!session && isSupabaseConfigured(),
    retry: false,
  });
  const meStatus = me.error instanceof ApiRequestError ? me.error.status : undefined;
  const allowed = !live || !!me.data;

  const lookup = useQuery({
    queryKey: ["oauth-consent", id],
    queryFn: () => client.lookup(id!),
    enabled: !!id && allowed,
    retry: false,
    staleTime: Infinity,
  });

  // Signing in leaves for Google and comes back to the site's root; the sign-in gate there
  // sends the browser back here with the request.
  useEffect(() => {
    if (live && ready && !session && id) rememberPendingConsent(id);
  }, [live, ready, session, id]);

  useEffect(() => {
    if (meStatus === 401) void signOut();
  }, [meStatus, signOut]);

  const follow = (url: string) => {
    if (!isFollowable(url)) {
      setError(`LifeOS won't send you to ${url}: it isn't a web address.`);
      setBusy(null);
      return;
    }
    forgetPendingConsent();
    setLeavingTo(hostOf(url));
    browser.go(url);
  };

  // Approved before: Supabase answers with the way back at once.
  const redirect = lookup.data?.kind === "redirect" ? lookup.data.url : null;
  const redirectFollowable = redirect !== null && isFollowable(redirect);
  useEffect(() => {
    if (!redirect || !isFollowable(redirect)) return;
    forgetPendingConsent();
    browser.replace(redirect);
  }, [redirect]);

  const decide = async (decision: "approve" | "deny") => {
    if (!id) return;
    setBusy(decision);
    setError(null);
    // Approving adds the agent to LifeOS's own list first (agent-access plan §4.4, fix 3): /mcp
    // answers only agents on it, and only Chris's web session can add one. If that fails, the
    // request isn't approved at Supabase either.
    const request = lookup.data?.kind === "ask" ? lookup.data.request : null;
    if (decision === "approve") {
      if (!request) {
        setError("The request hasn't loaded yet.");
        setBusy(null);
        return;
      }
      try {
        await agents.allow(request.clientId, request.clientName, request.clientUri ?? null);
      } catch (e) {
        setError(
          `LifeOS couldn't add ${request.clientName} to its agents, so it wasn't approved: ${
            e instanceof Error ? e.message : "the API didn't answer."
          }`,
        );
        setBusy(null);
        return;
      }
    }
    try {
      follow(decision === "approve" ? await client.approve(id) : await client.deny(id));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Supabase didn't answer.");
      setBusy(null);
    }
  };

  if (!id) {
    return (
      <Notice icon={<TriangleAlertIcon className="size-10 text-destructive" aria-hidden />} title="This link has no request in it">
        Start connecting again from your agent. It sends you here with the request it&apos;s making.
      </Notice>
    );
  }
  if (live && !isSupabaseConfigured()) return <AuthConfigError />;
  if (live && !ready) return <AuthSplash />;
  if (live && !session) {
    return (
      <AuthChrome>
        <Mark />
        <h1 className="mt-6 font-heading text-3xl">Connect an agent to LifeOS</h1>
        <p className="mt-2 max-w-sm text-center text-sm text-muted-foreground">
          An agent is asking to read your LifeOS. Sign in with the Google account LifeOS allows to
          see what it wants and approve or deny it.
        </p>
        <Button className="mt-8 min-w-52" size="lg" onClick={() => void signInWithGoogle()}>
          Sign in with Google
        </Button>
      </AuthChrome>
    );
  }
  if (meStatus === 403) {
    return (
      <Notice icon={<ShieldXIcon className="size-10 text-destructive" aria-hidden />} title="This account can't connect agents to LifeOS">
        <p>
          {session?.user.email
            ? `${session.user.email} is signed in, but it isn't the account LifeOS allows.`
            : "This Google account isn't the account LifeOS allows."}{" "}
          Turn the request down, or sign out and sign in with the right account.
        </p>
        {error ? <ErrorLine message={error} /> : null}
        <div className="mt-6 flex flex-wrap justify-center gap-2">
          <Button variant="destructive" disabled={busy !== null} onClick={() => void decide("deny")}>
            {busy === "deny" ? "Turning it down…" : "Turn the request down"}
          </Button>
          <Button variant="outline" disabled={busy !== null} onClick={() => void signOut()}>
            Sign out
          </Button>
        </div>
      </Notice>
    );
  }
  if (live && me.isError && meStatus !== 401) {
    return (
      <AuthReachError
        message={me.error instanceof Error ? me.error.message : "The API did not respond."}
        onRetry={() => void me.refetch()}
      />
    );
  }
  if (live && !me.data) return <AuthSplash />;

  if (lookup.isError) {
    return (
      <Notice icon={<TriangleAlertIcon className="size-10 text-destructive" aria-hidden />} title="This request can't be found">
        It may have expired or already been answered. Start connecting again from your agent.
        <span className="mt-2 block text-xs">({lookup.error instanceof Error ? lookup.error.message : "unknown error"})</span>
      </Notice>
    );
  }
  if (redirect && !redirectFollowable) {
    return (
      <Notice icon={<TriangleAlertIcon className="size-10 text-destructive" aria-hidden />} title="LifeOS won't follow this request">
        It sends you to {redirect}, which isn&apos;t a web address.
      </Notice>
    );
  }
  if (leavingTo || redirect) {
    return <Waiting label={`Returning you to ${leavingTo ?? hostOf(redirect!)}…`} />;
  }
  if (!lookup.data || lookup.data.kind !== "ask") return <Waiting label="Loading the request…" />;

  return <ConsentCard request={lookup.data.request} busy={busy} error={error} onDecide={(d) => void decide(d)} />;
}

function ConsentCard({
  request,
  busy,
  error,
  onDecide,
}: {
  request: ConsentRequest;
  busy: "approve" | "deny" | null;
  error: string | null;
  onDecide: (decision: "approve" | "deny") => void;
}) {
  const asks = scopeWords(request.scopes);
  return (
    <AuthChrome>
      <section
        aria-labelledby="consent-title"
        className="w-full max-w-md rounded-xl border border-border bg-card p-6 text-card-foreground shadow-sm"
      >
        <div className="flex items-center gap-3">
          <span className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <BotIcon className="size-5" aria-hidden />
          </span>
          <h1 id="consent-title" className="font-heading text-2xl leading-tight">
            Connect {request.clientName} to LifeOS?
          </h1>
        </div>
        <p className="mt-4 text-sm text-muted-foreground">
          {request.clientName} wants to read your LifeOS: your identity statements, goals, projects,
          tasks, habits, calendar, people, journal entries and notes, and the areas you&apos;ve
          switched on for agents. Agents can only read; nothing they do changes LifeOS.
        </p>
        <dl className="mt-5 space-y-3 text-sm">
          <Detail term="Signed in as">{request.email}</Detail>
          <Detail term="Sends you back to">
            <span className="font-mono text-xs">{hostOf(request.redirectUri)}</span>
          </Detail>
          {request.clientUri ? (
            <Detail term="Agent's website">
              <span className="font-mono text-xs">{hostOf(request.clientUri)}</span>
            </Detail>
          ) : null}
          {asks.length > 0 ? (
            <Detail term="Also asks for">
              <ul className="list-disc space-y-0.5 pl-4">
                {asks.map((a) => (
                  <li key={a}>{a}</li>
                ))}
              </ul>
            </Detail>
          ) : null}
        </dl>
        <p className="mt-5 rounded-lg bg-muted px-3 py-2 text-xs text-muted-foreground">
          Only approve an agent you started connecting yourself, just now. Approving adds it to
          Connected agents on the People page, where you can see what it reads and revoke it.
        </p>
        {error ? <ErrorLine message={error} /> : null}
        <div className="mt-6 flex flex-wrap justify-end gap-2">
          <Button variant="outline" disabled={busy !== null} onClick={() => onDecide("deny")}>
            {busy === "deny" ? "Denying…" : "Deny"}
          </Button>
          <Button disabled={busy !== null} onClick={() => onDecide("approve")}>
            {busy === "approve" ? "Approving…" : "Approve"}
          </Button>
        </div>
      </section>
    </AuthChrome>
  );
}

function Detail({ term, children }: { term: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[8.5rem_1fr] gap-2">
      <dt className="text-muted-foreground">{term}</dt>
      <dd className="min-w-0 break-words">{children}</dd>
    </div>
  );
}

function ErrorLine({ message }: { message: string }) {
  return (
    <p role="alert" className="mt-4 rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive">
      {message}
    </p>
  );
}

function Notice({ icon, title, children }: { icon: ReactNode; title: string; children: ReactNode }) {
  return (
    <AuthChrome>
      {icon}
      <h1 className="mt-6 text-center font-heading text-3xl">{title}</h1>
      <div className="mt-2 max-w-sm text-center text-sm text-muted-foreground">{children}</div>
    </AuthChrome>
  );
}

function Waiting({ label }: { label: string }) {
  return (
    <AuthChrome>
      <div role="status" className="flex flex-col items-center gap-3">
        <Loader2Icon className="size-6 animate-spin text-muted-foreground" aria-hidden />
        <p className="text-sm text-muted-foreground">{label}</p>
      </div>
    </AuthChrome>
  );
}
