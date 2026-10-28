import { BotIcon, ChevronDownIcon, ShieldAlertIcon } from "lucide-react";
import { useState, type ReactNode } from "react";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { agentRows, type AgentCallRow, type AgentRow, type AgentStatus } from "@/lib/agents";
import { formatTimestamp } from "@/lib/dates";
import { hostOf } from "@/lib/oauth-consent";
import {
  useAgentAction,
  useAgentCalls,
  useAgentGrants,
  useAgentsOverview,
  usePeople,
  type AgentAction,
} from "@/lib/queries";

const NOT_LINKED = "none";

const STATUS_LABEL: Record<AgentStatus, string> = {
  connected: "Connected",
  "not-allowed": "Not allowed",
  "no-grant": "Disconnected",
  gone: "Revoked",
};

/** What a confirm dialog asks before an action runs. */
type Pending = { action: AgentAction; title: string; description: string; confirm: string; destructive: boolean };

/**
 * Connected agents (agent-access plan §4.5): each agent Chris connected, whether LifeOS lets it
 * in, what it called, and the Person it acts as. Supabase lists the agents he approved; LifeOS
 * answers only the ones on its own list, so an agent approved at Supabase by anything but the
 * consent page waits here until he allows it.
 */
export function ConnectedAgents() {
  const overview = useAgentsOverview();
  const grants = useAgentGrants();
  const people = usePeople();
  const action = useAgentAction();
  const [pending, setPending] = useState<Pending | null>(null);
  const [error, setError] = useState<string | null>(null);

  const rows = agentRows(grants.data ?? [], overview.data?.agents ?? []);
  const aiPeople = (people.data ?? []).filter((p) => p.personKind === "ai" && !p.archived);

  const run = (next: AgentAction) => {
    setError(null);
    action.mutate(next, { onError: (e) => setError(e instanceof Error ? e.message : "That didn't work.") });
  };

  return (
    <section aria-labelledby="connected-agents-title" className="mt-12">
      <h2 id="connected-agents-title" className="type-scale-title">
        Connected agents
      </h2>
      <p className="mt-1 text-sm text-muted-foreground">
        Agents you connected to LifeOS. LifeOS answers only the ones allowed here, and revoking one
        shuts it out on its next call.
      </p>

      {overview.data ? (
        <ul className="mt-3 space-y-1 text-sm text-muted-foreground">
          {!overview.data.mcpEnabled ? (
            <li className="text-destructive">
              The MCP server is off (Mcp:Enabled), so no agent can call LifeOS right now.
            </li>
          ) : null}
          <li>
            {overview.data.writes === "off"
              ? "Read-only: agents can't change anything in LifeOS."
              : `Agents may write: ${overview.data.writes}.`}
          </li>
        </ul>
      ) : null}

      {overview.isLoading ? <ListSkeleton /> : null}
      {overview.isError ? (
        <EmptyState
          tone="error"
          icon={BotIcon}
          title="Connected agents failed to load"
          description={overview.error instanceof Error ? overview.error.message : "LifeOS didn't answer."}
        />
      ) : null}
      {grants.isError ? (
        <p role="alert" className="mt-4 rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive">
          Supabase didn&apos;t list the agents you approved (
          {grants.error instanceof Error ? grants.error.message : "no answer"}), so this shows LifeOS&apos;s
          list only.
        </p>
      ) : null}
      {error ? (
        <p role="alert" className="mt-4 rounded-lg bg-destructive/10 px-3 py-2 text-sm text-destructive">
          {error}
        </p>
      ) : null}

      {overview.data && !grants.isLoading && rows.length === 0 ? (
        <EmptyState
          icon={BotIcon}
          title="No agents yet"
          description="When you connect one and approve it on LifeOS's consent page, it shows here."
        />
      ) : null}

      <ul className="mt-6 space-y-3">
        {rows.map((row) => (
          <li key={row.clientId}>
            <AgentCard
              row={row}
              aiPeople={aiPeople}
              busy={action.isPending}
              onAsk={setPending}
              onLink={(personId) => run({ kind: "link", clientId: row.clientId, personId })}
            />
          </li>
        ))}
      </ul>

      <AlertDialog open={pending !== null} onOpenChange={(open) => (open ? null : setPending(null))}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{pending?.title}</AlertDialogTitle>
            <AlertDialogDescription>{pending?.description}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              variant={pending?.destructive ? "destructive" : "default"}
              onClick={() => {
                if (pending) run(pending.action);
                setPending(null);
              }}
            >
              {pending?.confirm}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}

function AgentCard({
  row,
  aiPeople,
  busy,
  onAsk,
  onLink,
}: {
  row: AgentRow;
  aiPeople: { id: string; title: string }[];
  busy: boolean;
  onAsk: (pending: Pending) => void;
  onLink: (personId: string | null) => void;
}) {
  const [open, setOpen] = useState(false);
  const record = row.record;
  const titleId = `agent-${row.clientId}`;
  const linked = record?.personId ?? NOT_LINKED;
  // A linked Person that's since been archived or changed kind still shows by name.
  const choices =
    record?.personId && !aiPeople.some((p) => p.id === record.personId)
      ? [...aiPeople, { id: record.personId, title: record.personName ?? "A Person no longer listed" }]
      : aiPeople;
  // A client LifeOS never allowed is known only by the calls it refused.
  const label = row.status === "gone" && !record?.revokedAt ? "Refused" : STATUS_LABEL[row.status];

  return (
    <article
      aria-labelledby={titleId}
      className="rounded-xl bg-card px-4 py-3 ring-1 ring-foreground/10"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 id={titleId} className="font-medium">
            {row.name}
          </h3>
          <p className="truncate font-mono text-xs text-muted-foreground">
            {row.uri ? hostOf(row.uri) : row.clientId}
          </p>
        </div>
        <Badge variant={row.status === "connected" ? "secondary" : row.status === "not-allowed" ? "destructive" : "outline"}>
          {label}
        </Badge>
      </div>

      <StatusText row={row} />

      {record && (record.lastCallAt || record.callsThisWeek > 0 || row.status === "connected") ? (
        <p className="mt-2 text-sm text-muted-foreground">
          {record.lastCallAt ? `Last call ${formatTimestamp(record.lastCallAt)}` : "No calls yet"}
          {" · "}
          {record.callsThisWeek === 1 ? "1 call this week" : `${record.callsThisWeek} calls this week`}
        </p>
      ) : null}

      {record?.allowed ? (
        <div className="mt-3 flex flex-wrap items-center gap-2 text-sm">
          <label htmlFor={`${titleId}-person`} className="text-muted-foreground">
            Acts as
          </label>
          <Select
            value={linked}
            disabled={busy}
            onValueChange={(v) => onLink(v === NOT_LINKED ? null : v)}
          >
            <SelectTrigger id={`${titleId}-person`} className="h-9 min-w-44" aria-label={`Person ${row.name} acts as`}>
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={NOT_LINKED}>No Person yet</SelectItem>
              {choices.map((p) => (
                <SelectItem key={p.id} value={p.id}>
                  {p.title}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {choices.length === 0 ? (
            <span className="text-xs text-muted-foreground">Add a Person whose kind is AI to link one.</span>
          ) : null}
        </div>
      ) : null}

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <Actions row={row} busy={busy} onAsk={onAsk} />
        {record ? (
          <Button
            variant="ghost"
            size="sm"
            aria-expanded={open}
            aria-controls={`${titleId}-calls`}
            onClick={() => setOpen((o) => !o)}
          >
            <ChevronDownIcon className={open ? "rotate-180 transition-transform" : "transition-transform"} aria-hidden />
            Recent calls
          </Button>
        ) : null}
      </div>
      {open ? <RecentCalls id={`${titleId}-calls`} clientId={row.clientId} /> : null}
    </article>
  );
}

function StatusText({ row }: { row: AgentRow }) {
  const record = row.record;
  const refused =
    record && record.lastRefusedAt
      ? ` LifeOS refused it ${record.refusedThisWeek === 1 ? "once" : `${record.refusedThisWeek} times`} this week, last ${formatTimestamp(record.lastRefusedAt)}.`
      : "";
  switch (row.status) {
    case "connected":
      return (
        <p className="mt-2 text-sm text-muted-foreground">
          Allowed {record?.allowedAt ? formatTimestamp(record.allowedAt) : ""}. It can read LifeOS.
        </p>
      );
    case "not-allowed":
      return (
        <p className="mt-2 flex gap-2 text-sm">
          <ShieldAlertIcon className="mt-0.5 size-4 shrink-0 text-destructive" aria-hidden />
          <span>
            Supabase lists it as approved, but LifeOS refuses it until you allow it here. Allow it
            only if you connected it yourself.{refused}
          </span>
        </p>
      );
    case "no-grant":
      return (
        <p className="mt-2 text-sm text-muted-foreground">
          Allowed in LifeOS, but Supabase no longer lists it, so it can&apos;t get new access. Revoke it
          here to shut out the access it still holds.
        </p>
      );
    case "gone":
      return (
        <p className="mt-2 text-sm text-muted-foreground">
          {record?.revokedAt
            ? `Revoked ${formatTimestamp(record.revokedAt)}. Connect it again to use it.`
            : `Never allowed in LifeOS.${refused}`}
        </p>
      );
  }
}

function Actions({ row, busy, onAsk }: { row: AgentRow; busy: boolean; onAsk: (pending: Pending) => void }) {
  switch (row.status) {
    case "connected":
      return (
        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          onClick={() =>
            onAsk({
              action: { kind: "revoke", clientId: row.clientId, removeGrant: true },
              title: `Revoke ${row.name}?`,
              description: `LifeOS refuses its next call, and Supabase removes its access. To use ${row.name} again, connect it again.`,
              confirm: "Revoke",
              destructive: true,
            })
          }
        >
          Revoke
        </Button>
      );
    case "not-allowed":
      return (
        <>
          <Button
            size="sm"
            disabled={busy}
            onClick={() =>
              onAsk({
                action: { kind: "allow", clientId: row.clientId, name: row.name, uri: row.uri },
                title: `Allow ${row.name} to read LifeOS?`,
                description:
                  "Only allow an agent you connected yourself. An agent with access to LifeOS can approve another one at Supabase, but only you can allow it here.",
                confirm: "Allow",
                destructive: false,
              })
            }
          >
            Allow
          </Button>
          <Button
            variant="outline"
            size="sm"
            disabled={busy}
            onClick={() =>
              onAsk({
                action: { kind: "remove-grant", clientId: row.clientId },
                title: `Remove ${row.name}?`,
                description: "Supabase removes its access, and it leaves this list unless it's connected again.",
                confirm: "Remove",
                destructive: true,
              })
            }
          >
            Remove
          </Button>
        </>
      );
    case "no-grant":
      return (
        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          onClick={() =>
            onAsk({
              action: { kind: "revoke", clientId: row.clientId, removeGrant: false },
              title: `Revoke ${row.name}?`,
              description: "LifeOS refuses its next call.",
              confirm: "Revoke",
              destructive: true,
            })
          }
        >
          Revoke
        </Button>
      );
    case "gone":
      return null;
  }
}

function RecentCalls({ id, clientId }: { id: string; clientId: string }) {
  const calls = useAgentCalls(clientId, true);
  return (
    <div id={id} className="mt-3 border-t border-border pt-3">
      {calls.isLoading ? <p className="text-sm text-muted-foreground">Loading calls…</p> : null}
      {calls.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The calls didn&apos;t load: {calls.error instanceof Error ? calls.error.message : "no answer"}
        </p>
      ) : null}
      {calls.data?.length === 0 ? <p className="text-sm text-muted-foreground">No calls in the last 90 days.</p> : null}
      {calls.data && calls.data.length > 0 ? (
        <ol className="space-y-2" aria-label="Recent calls, newest first">
          {calls.data.map((call) => (
            <li key={call.id}>
              <CallLine call={call} />
            </li>
          ))}
        </ol>
      ) : null}
    </div>
  );
}

function CallLine({ call }: { call: AgentCallRow }) {
  return (
    <div className="text-sm">
      <div className="flex flex-wrap items-baseline gap-x-2">
        <span className="font-mono">{call.tool ?? "(refused)"}</span>
        <Outcome outcome={call.outcome} />
        <span className="text-xs text-muted-foreground">
          {formatTimestamp(call.calledAt)}
          {call.items !== null ? ` · ${call.items === 1 ? "1 item" : `${call.items} items`}` : ""}
          {call.outcome !== "refused" ? ` · ${call.durationMs} ms` : ""}
        </span>
      </div>
      {call.arguments ? (
        <p className="mt-0.5 break-all font-mono text-xs text-muted-foreground">{call.arguments}</p>
      ) : null}
    </div>
  );
}

function Outcome({ outcome }: { outcome: AgentCallRow["outcome"] }): ReactNode {
  if (outcome === "ok") return <span className="text-xs text-muted-foreground">ok</span>;
  return (
    <span className="text-xs font-medium text-destructive">{outcome === "error" ? "error" : "refused"}</span>
  );
}
