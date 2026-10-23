import { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { toast } from "sonner";
import { ActivityIcon, Loader2Icon, RefreshCwIcon, TriangleAlertIcon } from "lucide-react";
import { ActivityBanner } from "@/components/activity/activity-banner";
import { ConnectCard } from "@/components/activity/connect-card";
import { DaySheet } from "@/components/activity/day-sheet";
import { ExerciseCard } from "@/components/activity/exercise-card";
import { SleepCard } from "@/components/activity/sleep-card";
import { StepsCard } from "@/components/activity/steps-card";
import { TagsCard } from "@/components/activity/tags-card";
import { TodayCard } from "@/components/activity/today-card";
import { errorText, returnError, useConnectOura } from "@/components/activity/use-connect-oura";
import { WorkoutSheet } from "@/components/activity/workout-sheet";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { asSentence, scopeWords, syncedAgo } from "@/lib/activity";
import type { OuraStatus, WorkoutRow } from "@/lib/production-ui-types";
import { useActivitySummary, useWrites } from "@/lib/queries";
import { useNow } from "@/lib/timer";

function Banner({ children, action }: { children: React.ReactNode; action?: React.ReactNode }) {
  return (
    <div
      role="alert"
      className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-attention/10 px-3 py-2.5 text-sm ring-1 ring-attention/25"
    >
      <p className="flex min-w-0 items-start gap-2">
        <TriangleAlertIcon className="mt-0.5 size-4 shrink-0 text-attention" aria-hidden />
        <span>{children}</span>
      </p>
      {action}
    </div>
  );
}

function syncWords(oura: OuraStatus, syncing: boolean, now: number): string {
  if (syncing) return "Syncing with Oura…";
  if (!oura.lastSyncedAt) return "Not synced with Oura yet";
  return `Synced from Oura ${syncedAgo(oura.lastSyncedAt, new Date(now))}`;
}

/**
 * The Activity page (ACTIVITY-1..4, activity plan §3.7): Oura's steps, the exercise Chris
 * records, and his sleep and readiness, read-only, under a drawn banner with the sync's status.
 * Each card has its own title, icon and colour; the Today card steps back a day at a time.
 * Before connecting it shows the Connect Oura card. Opening it syncs (skipped when LifeOS
 * synced under 5 minutes ago); Sync now always runs. A row opens the workout's sheet, where its
 * note is; a day on the sleep chart or table opens the day's.
 */
export function ActivityPage() {
  const summary = useActivitySummary();
  const writes = useWrites();
  const { connect, busy: connecting, error: connectError } = useConnectOura();
  const opened = useRef(false);
  const [syncing, setSyncing] = useState(false);
  const [syncError, setSyncError] = useState<string | null>(null);
  const [sheet, setSheet] = useState<{ open: boolean; workout: WorkoutRow | null }>({ open: false, workout: null });
  const [daySheet, setDaySheet] = useState<{ open: boolean; day: string | null }>({ open: false, day: null });
  const now = useNow(true, 60_000);

  const data = summary.data;
  const oura = data?.oura;

  const runSync = useCallback(
    async (force: boolean) => {
      setSyncing(true);
      setSyncError(null);
      try {
        await writes.syncActivity(force);
      } catch (e) {
        setSyncError(errorText(e));
      } finally {
        setSyncing(false);
      }
    },
    [writes],
  );

  useEffect(() => {
    if (opened.current || oura?.state !== "connected") return;
    opened.current = true;
    void runSync(false);
  }, [oura?.state, runSync]);

  const openWorkout = (workout: WorkoutRow) => setSheet({ open: true, workout });
  const openDay = (day: string) => setDaySheet({ open: true, day });
  const connected = oura?.connected ?? false;
  // Data synced before a disconnect stays: show it under the Connect card.
  const hasHistory = oura?.lastSyncedAt != null;

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 py-5 md:px-6">
        <ActivityBanner>
          {oura && connected ? (
            <>
              <p className="text-xs text-muted-foreground" aria-live="polite">
                {syncWords(oura, syncing, now)}
              </p>
              <Button variant="outline" size="sm" disabled={syncing} onClick={() => void runSync(true)}>
                <RefreshCwIcon className={syncing ? "animate-spin" : undefined} aria-hidden />
                Sync now
              </Button>
            </>
          ) : null}
        </ActivityBanner>

        {summary.isLoading ? (
          <div className="grid gap-4 lg:grid-cols-2">
            <CardSkeleton />
            <CardSkeleton />
          </div>
        ) : null}
        {summary.isError ? (
          <EmptyState
            tone="error"
            icon={ActivityIcon}
            title="Activity failed to load"
            description="This is not an empty record — the reader never returned."
          />
        ) : null}

        {data && oura ? (
          <>
            {oura.needsReconnect ? (
              <Banner
                action={
                  <Button size="sm" disabled={connecting} onClick={() => void connect()}>
                    Reconnect Oura
                  </Button>
                }
              >
                Oura needs reconnecting{oura.reconnectReason ? `: ${asSentence(oura.reconnectReason)}` : "."} What's
                here is from the last sync.
              </Banner>
            ) : null}
            {connected && oura.missingScopes.length > 0 ? (
              <Banner
                action={
                  <Button size="sm" variant="outline" disabled={connecting} onClick={() => void connect()}>
                    Reconnect Oura
                  </Button>
                }
              >
                Oura didn't allow {scopeWords(oura.missingScopes)}: reconnect and allow them so LifeOS can read
                everything.
              </Banner>
            ) : null}
            {syncError ? (
              <Banner>Couldn't sync with Oura: {syncError}</Banner>
            ) : connected && !syncing && oura.lastSyncError ? (
              <Banner>The last sync with Oura failed: {oura.lastSyncError}</Banner>
            ) : null}
            {connectError ? <Banner>Couldn't start connecting Oura: {connectError}</Banner> : null}

            {!connected ? <ConnectCard status={oura} busy={connecting} onConnect={() => void connect()} /> : null}

            {connected || hasHistory ? (
              <div className="grid items-start gap-4 lg:grid-cols-2">
                <div className="min-w-0 space-y-4">
                  <TodayCard summary={data} onOpenWorkout={openWorkout} onOpenDay={openDay} />
                  <StepsCard summary={data} />
                  <TagsCard summary={data} onOpenDay={openDay} />
                </div>
                <div className="min-w-0 space-y-4">
                  <SleepCard summary={data} onOpenDay={openDay} />
                  <ExerciseCard summary={data} onOpenWorkout={openWorkout} />
                </div>
              </div>
            ) : null}
          </>
        ) : null}
      </div>

      {data ? (
        <>
          <WorkoutSheet
            workout={sheet.workout}
            today={data.today}
            open={sheet.open}
            onOpenChange={(open) => setSheet((s) => ({ ...s, open }))}
          />
          <DaySheet
            day={daySheet.day}
            today={data.today}
            open={daySheet.open}
            onOpenChange={(open) => setDaySheet((s) => ({ ...s, open }))}
          />
        </>
      ) : null}
    </div>
  );
}

/**
 * `/activity/connected` (activity plan §3.1): Oura sends the browser back here with a code and
 * the state. It posts them once (guarded, so a second render can't spend the code twice), then
 * opens the Activity page; Oura's own error, or a refusal, shows with Try again.
 */
export function ActivityConnectedPage() {
  const search = useSearch({ from: "/activity/connected" });
  const navigate = useNavigate();
  const writes = useWrites();
  const { connect, busy, error: retryError } = useConnectOura();
  const posted = useRef(false);
  const [error, setError] = useState<string | null>(() => returnError(search));

  useEffect(() => {
    if (posted.current || !search.code || !search.state || search.error) return;
    posted.current = true;
    writes.connectOura(search.code, search.state).then(
      () => {
        toast.success("Oura connected");
        void navigate({ to: "/activity", replace: true });
      },
      (e: unknown) => setError(errorText(e)),
    );
  }, [search, writes, navigate]);

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 py-5 md:px-6">
        <ActivityBanner />
        {error || retryError ? (
          <EmptyState
            tone="error"
            icon={ActivityIcon}
            title="Oura isn't connected"
            description={retryError ?? error ?? undefined}
            action={
              <div className="flex flex-wrap justify-center gap-2">
                <Button disabled={busy} onClick={() => void connect()}>
                  Try again
                </Button>
                <Button variant="outline" onClick={() => void navigate({ to: "/activity", replace: true })}>
                  Back to Activity
                </Button>
              </div>
            }
          />
        ) : (
          <div role="status" className="flex flex-col items-center gap-3 py-12 text-center">
            <Loader2Icon className="size-6 animate-spin text-muted-foreground" aria-hidden />
            <p className="font-heading text-lg">Connecting Oura…</p>
            <p className="max-w-sm text-sm text-muted-foreground">
              LifeOS is reading your history from Oura. The first time can take a minute.
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
