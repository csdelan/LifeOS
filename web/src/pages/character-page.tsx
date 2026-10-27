import { useEffect, useId, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { toast } from "sonner";
import { CheckIcon, ChartColumnIcon, ChevronDownIcon, MedalIcon, XIcon } from "lucide-react";
import { useOuraSyncOnOpen } from "@/components/activity/use-oura-sync-on-open";
import { DomainDailyChart } from "@/components/character/domain-daily-chart";
import { DomainReports } from "@/components/character/domain-reports";
import { ExceptionDays } from "@/components/character/exception-days";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { celebrate } from "@/lib/celebrate";
import { formatDay } from "@/lib/dates";
import { newBest } from "@/lib/domain-best";
import {
  barPlus,
  bestText,
  conditionText,
  domainSymbol,
  fallHistoryText,
  isHomeHeld,
  multiplierChipText,
  multiplierLabel,
  needParts,
  rankHoldText,
  stateLine,
  todayText,
} from "@/lib/domains";
import { shownPoints } from "@/lib/home-score";
import type { DomainConsistency, DomainCurrent, DomainLifetime, DomainRow } from "@/lib/production-ui-types";
import { useDomains, useWrites } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

/**
 * The Character page (REWARD-7 / REWARD-8, xp-domains plan §7.4.9): each configured life
 * domain's level, which follows its last 90 days, beside its best, what sets it now, its two
 * levels (lifetime and consistency), today against the active minimum, the multiplier, its
 * daily chart and its rank ladder; then the exception days. Domains without rules show their
 * XP. The Reports tab (§7.4.10) shows each domain's rank over time.
 *
 * Opening the page settles any rank time alone has reached (`POST /api/domains/settle`), which
 * celebrates like any other rank-up; once it's done, a new best level or rank reached as days
 * settled is celebrated once (§7.4.7).
 */
export function CharacterPage() {
  const search = useSearch({ from: "/character" });
  const navigate = useNavigate({ from: "/character" });
  const view = search.view === "reports" ? "reports" : "domains";
  const domains = useDomains();
  const writes = useWrites();
  const started = useRef(false);
  const [settled, setSettled] = useState(false);
  useOuraSyncOnOpen();

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    // A failed settle changes nothing on screen: the reads still show where each domain stands.
    void writes
      .settleDomains()
      .catch(() => undefined)
      .finally(() => setSettled(true));
  }, [writes]);

  const rows = domains.data?.domains ?? [];
  const configured = rows.filter((d) => d.configured);
  const others = rows.filter((d) => !d.configured);

  useBestCelebration(configured, settled && !domains.isFetching);

  const showView = (next: string) =>
    void navigate({ search: (prev) => ({ ...prev, view: next === "reports" ? "reports" : undefined }) });

  return (
    <Tabs value={view} onValueChange={showView} className="flex h-full min-h-0 flex-col gap-0">
      <header className="shrink-0 border-b px-4 pt-4 md:px-6">
        <p className="type-scale-section text-primary">Character</p>
        <h1 className="font-heading text-2xl">Your life domains</h1>
        <p className="mt-1 max-w-prose text-xs text-muted-foreground">
          Your level follows your last 90 days. It rises at once, falls slowly, and your best is kept.
        </p>
        <TabsList variant="line" className="mt-3 -mb-px h-auto">
          <TabsTrigger value="domains">Domains</TabsTrigger>
          <TabsTrigger value="reports">Reports</TabsTrigger>
        </TabsList>
      </header>

      <TabsContent value="reports" className="min-h-0 flex-1 overflow-y-auto px-4 py-5 md:px-6">
        <DomainReports />
      </TabsContent>

      <TabsContent value="domains" className="min-h-0 flex-1 space-y-8 overflow-y-auto px-4 py-5 md:px-6">
        {domains.isLoading ? (
          <div className="grid gap-3 md:grid-cols-2">
            <CardSkeleton />
            <CardSkeleton />
          </div>
        ) : null}
        {domains.isError ? (
          <EmptyState
            tone="error"
            icon={MedalIcon}
            title="Your domains failed to load"
            description="This is not an empty character — the reader never returned."
          />
        ) : null}

        {configured.map((d) => (
          <div key={d.key} className="space-y-6">
            <DomainCard domain={d} />
            <RankLadder domain={d} />
          </div>
        ))}

        {configured.length > 0 && domains.data ? <ExceptionDays today={domains.data.today} /> : null}

        {others.length > 0 ? (
          <section aria-labelledby="character-other">
            <h2 id="character-other" className="type-scale-section mb-3 text-muted-foreground">
              Other domains
            </h2>
            <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
              {others.map((d) => (
                <li key={d.key} className="rounded-xl bg-card px-4 py-3 ring-1 ring-foreground/10">
                  <p className="flex items-baseline justify-between gap-2">
                    <span className="font-medium">
                      <span aria-hidden>{domainSymbol(d.key)} </span>
                      {d.name}
                    </span>
                    <span className="text-sm">{d.totalXp.toLocaleString()} XP</span>
                  </p>
                  <p className="mt-1 text-xs text-muted-foreground">Levels start once this domain has its own rules.</p>
                </li>
              ))}
            </ul>
          </section>
        ) : null}
      </TabsContent>
    </Tabs>
  );
}

/**
 * Celebrates, once, a new best level or rank reached by time alone (§7.4.7): when `ready` (the
 * settle is done and the read is fresh), each domain's best is compared with the last one kept.
 */
function useBestCelebration(rows: DomainRow[], ready: boolean) {
  useEffect(() => {
    if (!ready) return;
    let scale: "full" | "grand" | null = null;
    for (const d of rows) {
      if (!d.best) continue;
      const kind = newBest(d.key, { level: d.best.level, rank: d.best.rank });
      if (kind === "rank") {
        toast(`You're now a ${d.name} ${d.best.rankTitle}.`, {
          icon: "🏅",
          description: "A new best rank, reached as your days settled.",
        });
        scale = "grand";
      } else if (kind === "level") {
        toast(`${domainSymbol(d.key)} ${d.name} level ${d.best.level}!`.trim(), {
          icon: "⬆️",
          description: "A new best level, reached as your days settled.",
        });
        scale ??= "full";
      }
    }
    if (scale) celebrate(scale);
  }, [rows, ready]);
}

function DomainCard({ domain }: { domain: DomainRow }) {
  const headingId = useId();
  const chartId = useId();
  const view = `character.${domain.key}.chart`;
  const [chartOpen, setChartOpenState] = useState(() => loadView<{ open: boolean }>(view, { open: false }).open);
  const shown = domain.shown!;
  const best = domain.best!;
  const fallHistory = fallHistoryText(domain);

  const setChartOpen = (open: boolean) => {
    setChartOpenState(open);
    try {
      saveView(view, { open });
    } catch {
      // Storage can be full or blocked; the chart still opens for this view.
    }
  };

  return (
    <section aria-labelledby={headingId} className="rounded-xl bg-card ring-1 ring-foreground/10">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 pt-4">
        <h2 id={headingId} className="font-heading text-xl">
          <span aria-hidden>{domainSymbol(domain.key)} </span>
          {domain.name}
        </h2>
        <RankBadge title={shown.rankTitle} />
      </div>
      <div className="space-y-1.5 px-4 pt-2 pb-4">
        <p className="flex flex-wrap items-baseline gap-x-3">
          <span className="font-heading text-4xl">Level {shown.level}</span>
          {shown.falling || shown.paused ? (
            <span className="text-xs tabular-nums text-muted-foreground">{shown.exact.toFixed(2)}</span>
          ) : null}
        </p>
        <p className="text-xs text-muted-foreground">
          {bestText(best)}
          {best.levelOn ? ` · ${formatDay(best.levelOn)}` : ""}
        </p>
        <p className="max-w-prose text-sm">{stateLine(domain)}</p>
        {fallHistory ? <p className="text-xs text-muted-foreground">{fallHistory}</p> : null}
      </div>
      <div className="grid border-t md:grid-cols-2">
        <LevelsHalf domain={domain} lifetime={domain.lifetime!} consistency={domain.consistency!} />
        <CurrentHalf domain={domain} current={domain.current!} />
      </div>
      <div className="border-t px-2 py-1.5">
        <Button
          variant="ghost"
          size="sm"
          aria-expanded={chartOpen}
          aria-controls={chartId}
          onClick={() => setChartOpen(!chartOpen)}
        >
          <ChartColumnIcon aria-hidden />
          {chartOpen ? "Hide the daily chart" : "Show the daily chart"}
          <ChevronDownIcon aria-hidden className={cn("transition-transform", chartOpen && "rotate-180")} />
        </Button>
      </div>
      <div id={chartId}>{chartOpen ? <DomainDailyChart domain={domain.key} name={domain.name} /> : null}</div>
    </section>
  );
}

function RankBadge({ title }: { title: string }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--type-reward)]/15 px-2.5 py-0.5 text-xs font-medium text-foreground">
      <MedalIcon className="size-3.5" aria-hidden />
      {title}
    </span>
  );
}

function Bar({ label, value, max, text }: { label: string; value: number; max: number; text: string }) {
  const fill = max > 0 ? Math.min(Math.max(value / max, 0), 1) * 100 : 0;
  return (
    <div
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={max || 1}
      aria-valuenow={Math.min(value, max || 1)}
      aria-valuetext={text}
      className="h-2 overflow-hidden rounded-full bg-muted"
    >
      <div className="h-full rounded-full bg-chart-earned" style={{ width: `${fill}%` }} />
    </div>
  );
}

/** The two levels the shown level follows: lifetime (XP) and consistency (the last 90 days). */
function LevelsHalf({
  domain,
  lifetime,
  consistency,
}: {
  domain: DomainRow;
  lifetime: DomainLifetime;
  consistency: DomainConsistency;
}) {
  const top = lifetime.xpToNext === 0;
  const next = consistency.next;
  const parts = next && next.windowDays > 0 ? needParts(next) : [];
  const home = isHomeHeld(domain);

  return (
    <div className="space-y-4 p-4">
      <div className="space-y-1">
        <p className="flex items-baseline justify-between gap-2 text-sm">
          <span className="font-medium">Lifetime level {lifetime.level}</span>
          {domain.shown?.limitedBy === "lifetime" ? (
            <span className="text-xs text-muted-foreground">sets your level</span>
          ) : null}
        </p>
        <Bar
          label={top ? `${domain.name} lifetime: top level` : `Progress to ${domain.name} lifetime level ${lifetime.level + 1}`}
          value={top ? 1 : lifetime.xpIntoLevel}
          max={top ? 1 : lifetime.levelXp}
          text={top ? "The top level" : `${lifetime.xpIntoLevel.toLocaleString()} of ${lifetime.levelXp.toLocaleString()} XP`}
        />
        <p className="text-xs">
          {top ? (
            "The top level"
          ) : (
            <>
              {lifetime.xpIntoLevel.toLocaleString()} of {lifetime.levelXp.toLocaleString()} XP
              <span className="text-muted-foreground">
                {" "}
                · {lifetime.xpToNext.toLocaleString()} to level {lifetime.level + 1}
              </span>
            </>
          )}
        </p>
      </div>

      <div className="space-y-1">
        <p className="flex items-baseline justify-between gap-2 text-sm">
          <span className="font-medium">
            {home ? "Condition" : "Consistency"} level {consistency.level}
          </span>
          {domain.shown?.limitedBy === "consistency" ? (
            <span className="text-xs text-muted-foreground">sets your level</span>
          ) : null}
        </p>
        {home ? <p className="text-xs">{conditionText(domain)}</p> : null}
        {next ? (
          <>
            {home ? (
              <Bar
                label={`${domain.name} kept-up days toward condition level ${next.level}`}
                value={next.activeDays}
                max={next.activeDaysNeeded}
                text={`${next.activeDays} of ${next.activeDaysNeeded} days at ${barPlus(next.bar)}`}
              />
            ) : (
              <Bar
                label={`${domain.name} average toward consistency level ${next.level}`}
                value={next.averageNow}
                max={next.average}
                text={`${next.averageNow.toLocaleString()} of ${next.average} base XP a day`}
              />
            )}
            {parts.length > 0 ? (
              <>
                <p className="text-xs">Level {next.level} needs:</p>
                <ul aria-label={`What ${home ? "condition" : "consistency"} level ${next.level} needs`} className="space-y-0.5 text-xs">
                  {parts.map((p) => (
                    <li key={p.key} className="flex items-start gap-1.5">
                      {p.met ? (
                        <CheckIcon aria-hidden className="mt-px size-3.5 shrink-0 text-chart-earned" />
                      ) : (
                        <XIcon aria-hidden className="mt-px size-3.5 shrink-0 text-muted-foreground" />
                      )}
                      <span>
                        {p.text}
                        <span className="sr-only">{p.met ? ": met" : ": not yet"}</span>
                      </span>
                    </li>
                  ))}
                </ul>
              </>
            ) : (
              <p className="text-xs text-muted-foreground">
                {home
                  ? "Your condition level starts once you rate your spaces and a day settles."
                  : "Your consistency level starts once a day settles."}
              </p>
            )}
          </>
        ) : (
          <p className="text-xs">The top level.</p>
        )}
      </div>

      <p className="text-[0.6875rem] text-muted-foreground">
        {domain.totalXp.toLocaleString()} {domain.label} in all
        {lifetime.start ? ` · since ${formatDay(lifetime.start)}` : ""}
        {consistency.windowDays > 0
          ? home
            ? ` · an average Home Score of ${shownPoints(consistency.averageHomeScore ?? 0)} over the last ${consistency.windowDays} days`
            : ` · ${consistency.averageBase.toLocaleString()} base XP a day over the last ${consistency.windowDays} days`
          : ""}
      </p>
    </div>
  );
}

function CurrentHalf({ domain, current }: { domain: DomainRow; current: DomainCurrent }) {
  const kind = isHomeHeld(domain) ? "home" : "xp";
  // "kept-up days", "active days": the word before a noun.
  const word = kind === "home" ? "kept-up" : "active";
  const lapsed = current.activeDays === 0 && (kind === "home" ? (domain.consistency?.windowDays ?? 0) > 0 : domain.totalXp > 0);

  return (
    <div className="space-y-3 border-t p-4 md:border-t-0 md:border-l">
      <p className="text-xs font-medium text-muted-foreground">Now</p>
      <div className="flex items-center gap-4">
        <TodayRing current={current} kind={kind} />
        <div className="min-w-0 space-y-1.5">
          <p className="text-sm">{todayText(current, kind)}</p>
          <p>
            <span className="inline-flex rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium">
              {multiplierChipText(current, kind)}
            </span>
          </p>
          <p className="text-xs text-muted-foreground">
            {current.nextTier
              ? `${current.nextTier.daysNeeded} more ${word} ${current.nextTier.daysNeeded === 1 ? "day" : "days"} in the window for ${multiplierLabel(current.nextTier.multiplier)}`
              : "The top multiplier."}
          </p>
        </div>
      </div>
      {lapsed ? (
        <p className="text-xs text-muted-foreground">
          No {word} days in the last {current.windowDays}: the multiplier is back to ×1.0, and your level falls slowly
          until you're back on track. Your best stays.
        </p>
      ) : null}
      <p className="text-[0.6875rem] text-muted-foreground">
        {kind === "home" ? `A kept-up day has ${current.rule}.` : `An active day is ${current.rule}.`} Days count once they
        settle at 4 a.m.
      </p>
    </div>
  );
}

/** Today's action XP against the active minimum, as a ring; for the home, today's Home Score against the bar. */
function TodayRing({ current, kind }: { current: DomainCurrent; kind: "xp" | "home" }) {
  const r = 28;
  const circumference = 2 * Math.PI * r;
  const { todayActive: active, todayException: exception } = current;
  const home = kind === "home";
  const action = home ? (current.todayHomeScore ?? 0) : current.todayAction;
  const minimum = home ? (current.bar ?? 0) : current.activeMinimum;
  const share = exception ? 0 : minimum > 0 ? Math.min(Math.max(action, 0) / minimum, 1) : 0;
  const shownAction = home ? (current.todayHomeScore == null ? "–" : shownPoints(action)) : String(action);
  const shownMinimum = home ? barPlus(minimum) : String(minimum);
  return (
    <svg viewBox="0 0 72 72" className="size-20 shrink-0" role="img" aria-label={todayText(current, kind)}>
      <circle cx="36" cy="36" r={r} fill="none" strokeWidth="8" className="stroke-muted" />
      {share > 0 ? (
        <circle
          cx="36"
          cy="36"
          r={r}
          fill="none"
          strokeWidth="8"
          strokeLinecap="round"
          strokeDasharray={`${share * circumference} ${circumference}`}
          transform="rotate(-90 36 36)"
          className={cn(active ? "stroke-chart-earned" : "stroke-chart-earned/60")}
        />
      ) : null}
      <text x="36" y="35" textAnchor="middle" className="fill-foreground text-[15px] font-semibold">
        {exception ? "–" : active ? "✓" : shownAction}
      </text>
      <text x="36" y="49" textAnchor="middle" className="fill-muted-foreground text-[9px]">
        {/* A Home Score and its bar don't fit side by side: a kept-up day shows the score alone. */}
        {exception ? "exception" : active ? (home ? shownAction : `${shownAction}/${shownMinimum}`) : `/${shownMinimum}`}
      </text>
    </svg>
  );
}

function RankLadder({ domain }: { domain: DomainRow }) {
  const headingId = useId();
  const ranks = domain.ranks ?? [];
  const held = domain.shown?.rank ?? 0;
  const best = domain.best?.rank ?? 0;
  return (
    <section aria-labelledby={headingId}>
      <h2 id={headingId} className="type-scale-section mb-3 text-muted-foreground">
        {domain.name} ranks
      </h2>
      <ol className="divide-y rounded-xl bg-card ring-1 ring-foreground/10">
        {ranks.map((r) => {
          const current = r.rank === held;
          const isBest = r.rank === best && best > held;
          return (
            <li
              key={r.rank}
              aria-current={current ? "step" : undefined}
              className={cn(
                "grid grid-cols-[4.5rem_1fr] items-baseline gap-x-3 gap-y-0.5 px-3 py-2 text-sm sm:grid-cols-[4.5rem_8rem_1fr_auto]",
                current && "bg-primary/8",
              )}
            >
              <span className="text-xs tabular-nums text-muted-foreground">
                {r.level === r.lastLevel ? `Lv ${r.level}` : `Lv ${r.level}–${r.lastLevel}`}
              </span>
              <span className={cn("font-medium", current && "text-primary")}>
                {r.title}
                {current ? <span className="ml-1.5 text-xs font-normal text-muted-foreground">(you)</span> : null}
                {isBest ? <span className="ml-1.5 text-xs font-normal text-muted-foreground">(best)</span> : null}
              </span>
              <span className="col-start-2 text-xs text-muted-foreground sm:col-start-auto">
                {rankHoldText(r)}
                {r.rank > 0 ? ` · ${r.totalXp.toLocaleString()} XP to reach` : ""}
              </span>
              <span className="col-start-2 text-xs text-muted-foreground sm:col-start-auto sm:text-right">
                {r.gems > 0 ? (
                  <>
                    <span aria-hidden>💎 </span>
                    {r.gems} {r.gems === 1 ? "Gem" : "Gems"}
                  </>
                ) : null}
                {r.reachedOn ? ` · reached ${formatDay(r.reachedOn)}` : ""}
              </span>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
