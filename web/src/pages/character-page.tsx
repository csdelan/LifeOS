import { useEffect, useId, useRef, useState } from "react";
import { ChartColumnIcon, ChevronDownIcon, MedalIcon } from "lucide-react";
import { DomainDailyChart } from "@/components/character/domain-daily-chart";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { formatDay } from "@/lib/dates";
import {
  domainSymbol,
  multiplierLabel,
  nextRankText,
  percent,
  rankRequirement,
} from "@/lib/domains";
import type { DomainCurrent, DomainLifetime, DomainRow } from "@/lib/production-ui-types";
import { useDomains, useWrites } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

/**
 * The Character page (REWARD-7, xp-domains plan §3.12): each configured life domain's
 * lifetime advancement beside how it's going now, its daily chart, and its rank ladder;
 * domains without rules show their XP. Opening it settles any rank time alone has reached
 * (`POST /api/domains/settle`), which celebrates like any other rank-up.
 */
export function CharacterPage() {
  const domains = useDomains();
  const writes = useWrites();
  const settled = useRef(false);

  useEffect(() => {
    if (settled.current) return;
    settled.current = true;
    // A failed settle changes nothing on screen: the reads still show where each domain stands.
    void writes.settleDomains().catch(() => undefined);
  }, [writes]);

  const rows = domains.data?.domains ?? [];
  const configured = rows.filter((d) => d.configured);
  const others = rows.filter((d) => !d.configured);

  return (
    <div className="h-full overflow-y-auto">
      <header className="border-b px-4 py-4 md:px-6">
        <p className="type-scale-section text-primary">Character</p>
        <h1 className="font-heading text-2xl">Your life domains</h1>
        <p className="mt-1 max-w-prose text-xs text-muted-foreground">
          Levels and ranks are for life: a bad month never takes them back. Today's ring, the last 30 days
          and the multiplier show how it's going now.
        </p>
      </header>

      <div className="space-y-8 px-4 py-5 md:px-6">
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
      </div>
    </div>
  );
}

function DomainCard({ domain }: { domain: DomainRow }) {
  const headingId = useId();
  const chartId = useId();
  const view = `character.${domain.key}.chart`;
  const [chartOpen, setChartOpenState] = useState(() => loadView<{ open: boolean }>(view, { open: false }).open);
  const lifetime = domain.lifetime!;
  const current = domain.current!;

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
        <RankBadge title={lifetime.rankTitle} />
      </div>
      <div className="grid md:grid-cols-2">
        <LifetimeHalf domain={domain} lifetime={lifetime} />
        <CurrentHalf domain={domain} current={current} />
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

function LifetimeHalf({ domain, lifetime }: { domain: DomainRow; lifetime: DomainLifetime }) {
  const next = domain.nextRank;
  const top = lifetime.level === 100 && !lifetime.capped;
  const fill = lifetime.capped || top ? 100 : lifetime.levelXp > 0 ? (lifetime.xpIntoLevel / lifetime.levelXp) * 100 : 0;

  return (
    <div className="space-y-3 p-4">
      <p className="text-xs font-medium text-muted-foreground">Lifetime</p>
      <p className="font-heading text-4xl">Level {lifetime.level}</p>
      <div className="space-y-1">
        <div
          role="progressbar"
          aria-label={top ? `${domain.name}: top level` : `Progress to ${domain.name} level ${lifetime.level + 1}`}
          aria-valuemin={0}
          aria-valuemax={lifetime.levelXp || 1}
          aria-valuenow={lifetime.capped ? lifetime.levelXp : lifetime.xpIntoLevel}
          aria-valuetext={
            lifetime.capped
              ? `Full: the top of ${lifetime.rankTitle}`
              : `${lifetime.xpIntoLevel.toLocaleString()} of ${lifetime.levelXp.toLocaleString()} XP`
          }
          className="h-2 overflow-hidden rounded-full bg-muted"
        >
          <div className="h-full rounded-full bg-chart-earned" style={{ width: `${fill}%` }} />
        </div>
        <p className="text-xs">
          {lifetime.capped ? (
            <>
              Level {lifetime.level} (top of {lifetime.rankTitle}) · {lifetime.bankedXp.toLocaleString()} XP banked
            </>
          ) : top ? (
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
      <p className="text-sm">{next ? nextRankText(next) : `${lifetime.rankTitle}: the top rank.`}</p>
      <p className="text-[0.6875rem] text-muted-foreground">
        {domain.totalXp.toLocaleString()} {domain.label} in all
        {lifetime.start ? ` · since ${formatDay(lifetime.start)}` : ""}
        {lifetime.qualifiedOn ? ` · ${lifetime.rankTitle} since ${formatDay(lifetime.qualifiedOn)}` : ""}
      </p>
    </div>
  );
}

function CurrentHalf({ domain, current }: { domain: DomainRow; current: DomainCurrent }) {
  const target = current.target ?? 0;
  const lapsed = current.successfulDays === 0 && domain.totalXp > 0;

  return (
    <div className="space-y-3 border-t p-4 md:border-t-0 md:border-l">
      <p className="text-xs font-medium text-muted-foreground">Now</p>
      <div className="flex items-center gap-4">
        <TodayRing base={current.todayBase} target={target} succeeded={current.todaySucceeded} />
        <div className="min-w-0 space-y-1.5">
          <p className="text-sm">
            <span className="font-medium">Today: {current.todayBase.toLocaleString()}</span>
            <span className="text-muted-foreground">
              {current.todaySucceeded || target === 0
                ? " base XP · a successful day"
                : ` of ${target} base XP · ${(target - current.todayBase).toLocaleString()} to go`}
            </span>
          </p>
          <p className="text-sm">
            {current.successfulDays} of the last {current.windowDays} days successful
          </p>
          <p>
            <span className="inline-flex rounded-full bg-muted px-2.5 py-0.5 text-xs font-medium">
              {multiplierLabel(current.multiplier)} · {percent(current.consistency)} of the last {current.windowDays} days
            </span>
          </p>
          <p className="text-xs text-muted-foreground">
            {current.nextTier
              ? `${current.nextTier.daysNeeded} more successful ${current.nextTier.daysNeeded === 1 ? "day" : "days"} in the window for ${multiplierLabel(current.nextTier.multiplier)}`
              : "The top multiplier."}
          </p>
        </div>
      </div>
      {lapsed ? (
        <p className="text-xs text-muted-foreground">
          No successful days in the last {current.windowDays}: the multiplier is back to ×1.0 for now. Your level and
          rank stay where they were.
        </p>
      ) : null}
      <p className="text-[0.6875rem] text-muted-foreground">A successful day is {current.rule}.</p>
    </div>
  );
}

/** Today's base XP against the success line, as a ring. */
function TodayRing({ base, target, succeeded }: { base: number; target: number; succeeded: boolean }) {
  const r = 28;
  const circumference = 2 * Math.PI * r;
  const share = target > 0 ? Math.min(base / target, 1) : 0;
  return (
    <svg
      viewBox="0 0 72 72"
      className="size-20 shrink-0"
      role="img"
      aria-label={`Today: ${base} of ${target} base XP${succeeded ? ", a successful day" : ""}`}
    >
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
          className={cn(succeeded ? "stroke-chart-earned" : "stroke-chart-earned/60")}
        />
      ) : null}
      <text x="36" y="35" textAnchor="middle" className="fill-foreground text-[15px] font-semibold">
        {succeeded ? "✓" : base}
      </text>
      <text x="36" y="49" textAnchor="middle" className="fill-muted-foreground text-[9px]">
        {succeeded ? `${base}/${target}` : `/${target}`}
      </text>
    </svg>
  );
}

function RankLadder({ domain }: { domain: DomainRow }) {
  const headingId = useId();
  const ranks = domain.ranks ?? [];
  const held = domain.lifetime?.rank ?? 0;
  return (
    <section aria-labelledby={headingId}>
      <h2 id={headingId} className="type-scale-section mb-3 text-muted-foreground">
        {domain.name} ranks
      </h2>
      <ol className="divide-y rounded-xl bg-card ring-1 ring-foreground/10">
        {ranks.map((r) => {
          const current = r.rank === held;
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
                {r.level === r.topLevel ? `Lv ${r.level}` : `Lv ${r.level}–${r.topLevel}`}
              </span>
              <span className={cn("font-medium", current && "text-primary")}>
                {r.title}
                {current ? <span className="ml-1.5 text-xs font-normal text-muted-foreground">(you)</span> : null}
              </span>
              <span className="col-start-2 text-xs text-muted-foreground sm:col-start-auto">
                {rankRequirement(r)}
              </span>
              <span className="col-start-2 text-xs text-muted-foreground sm:col-start-auto sm:text-right">
                {r.gems > 0 ? (
                  <>
                    <span aria-hidden>💎 </span>
                    {r.gems} {r.gems === 1 ? "Gem" : "Gems"}
                  </>
                ) : null}
                {r.qualifiedOn ? ` · reached ${formatDay(r.qualifiedOn)}` : ""}
              </span>
            </li>
          );
        })}
      </ol>
    </section>
  );
}
