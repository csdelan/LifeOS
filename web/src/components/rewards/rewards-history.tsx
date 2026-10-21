import { useEffect, useMemo, useState } from "react";
import { ChevronLeftIcon, ChevronRightIcon } from "lucide-react";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { TypeIcon } from "@/components/primitives/type-icon";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { addMonths, formatDayFull, localDateFromTimestamp, startOfMonth, todayIso } from "@/lib/dates";
import type { LedgerDay, LedgerRow } from "@/lib/production-ui-types";
import { LEDGER_RANGE_LIMIT, useLedgerDaily, useLedgerRange } from "@/lib/queries";
import { currency as currencyDef, CURRENCIES, formatAmounts, formatUsd } from "@/lib/rewards";
import {
  chartAnchor,
  compact,
  dayPeriod,
  dollarsMoved,
  entryDetail,
  entryTitle,
  filterLedger,
  groupByDay,
  heatLevel,
  historyRange,
  inPeriod,
  localMidnightIso,
  monthCells,
  monthLabel,
  monthPeriod,
  negate,
  netEarned,
  niceMax,
  periodLabel,
  periodsEnding,
  samePeriod,
  shortPeriodLabel,
  totalsFor,
  totalsInWords,
  viewerTimeZone,
  type EntryFilter,
  type HistoryBucket,
  type Period,
  type PeriodTotals,
} from "@/lib/rewards-history";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

const VIEW_KEY = "rewards.history";
const WEEKDAYS = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
const EMPTY_DAY: Pick<LedgerDay, "earned" | "reversed" | "spent"> = { earned: {}, reversed: {}, spent: {} };

/** Sequential heat for the calendar: one hue, more is darker (level 0 is no fill). */
const HEAT = ["", "bg-chart-earned/10", "bg-chart-earned/20", "bg-chart-earned/32", "bg-chart-earned/45"];

const ENTRY_FILTERS: { id: EntryFilter; label: string }[] = [
  { id: "all", label: "All" },
  { id: "earn", label: "Earned" },
  { id: "reversal", label: "Given back" },
  { id: "redeem", label: "Spent" },
];

/** The currency and bucket last picked on this device. */
function usePersistedView() {
  const [view, setView] = useState(() => {
    const stored = loadView(VIEW_KEY, { currency: "xp", bucket: "week" as HistoryBucket });
    return {
      currency: currencyDef(stored.currency)?.key ?? "xp",
      bucket: stored.bucket === "month" ? "month" : ("week" as HistoryBucket),
    };
  });
  useEffect(() => {
    try {
      saveView(VIEW_KEY, view);
    } catch {
      // Storage can be unavailable (private windows); the choice just isn't remembered.
    }
  }, [view]);
  return [view, setView] as const;
}

/**
 * The Rewards History (REWARD-5): a month calendar of what each day earned, earned vs spent
 * by week or month, and the ledger behind any day, week or month. Pick a day on the
 * calendar or a bar on the chart to read its entries; each links to the item that earned
 * it or the Reward it bought.
 */
export function RewardsHistory({ onOpenSubject }: { onOpenSubject: (subjectId: string) => void }) {
  const today = todayIso();
  const thisMonth = startOfMonth(today);
  const tz = useMemo(() => viewerTimeZone(), []);
  const [month, setMonth] = useState(thisMonth);
  const [selection, setSelection] = useState<Period | null>(null);
  const [view, setView] = usePersistedView();

  const range = historyRange(month, today);
  const daily = useLedgerDaily(range.from, range.to, tz);
  const days = useMemo(() => daily.data ?? [], [daily.data]);
  const viewed = monthPeriod(month);
  const period = selection ?? viewed;
  const periods = useMemo(
    () => periodsEnding(view.bucket, chartAnchor(month, today)).map((p) => totalsFor(days, p)),
    [days, month, today, view.bucket],
  );

  function goToMonth(next: string) {
    setMonth(next);
    setSelection(null);
  }

  // A pick made again goes back to the whole month.
  function pick(next: Period) {
    setSelection((current) => (current && samePeriod(current, next) ? null : next));
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-3" role="group" aria-label="History filters">
        <div className="flex items-center gap-1">
          <Button variant="ghost" size="icon-sm" aria-label="Previous month" onClick={() => goToMonth(addMonths(month, -1))}>
            <ChevronLeftIcon aria-hidden />
          </Button>
          <h2 className="min-w-36 text-center font-heading text-xl" aria-live="polite">
            {monthLabel(month)}
          </h2>
          <Button
            variant="ghost"
            size="icon-sm"
            aria-label="Next month"
            disabled={month >= thisMonth}
            onClick={() => goToMonth(addMonths(month, 1))}
          >
            <ChevronRightIcon aria-hidden />
          </Button>
          {month !== thisMonth ? (
            <Button variant="outline" size="sm" className="ml-1" onClick={() => goToMonth(thisMonth)}>
              This month
            </Button>
          ) : null}
        </div>
        <ToggleGroup
          label="Currency"
          value={view.currency}
          onChange={(currency) => setView((v) => ({ ...v, currency }))}
          options={CURRENCIES.map((c) => ({ id: c.key, label: c.label, symbol: c.symbol }))}
        />
        <ToggleGroup
          label="Chart by"
          value={view.bucket}
          onChange={(bucket) => setView((v) => ({ ...v, bucket }))}
          options={[
            { id: "week", label: "Weeks" },
            { id: "month", label: "Months" },
          ]}
        />
      </div>

      {daily.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The history failed to load.
        </p>
      ) : null}

      <div
        className={cn(
          "grid gap-6 transition-opacity xl:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]",
          daily.isFetching && daily.isPlaceholderData ? "opacity-60" : "",
        )}
      >
        <HistoryCalendar
          month={month}
          days={days}
          currency={view.currency}
          today={today}
          selected={selection?.kind === "day" ? selection.start : null}
          picked={selection?.kind === "day" ? null : selection}
          onSelect={(date) => pick(dayPeriod(date))}
        />
        <EarnedSpentChart
          periods={periods}
          currency={view.currency}
          bucket={view.bucket}
          selected={selection}
          today={today}
          onSelect={pick}
        />
      </div>

      <PeriodReport
        period={period}
        totals={totalsFor(days, period)}
        wholeMonth={selection === null ? null : viewed}
        onShowMonth={() => setSelection(null)}
        onOpenSubject={onOpenSubject}
      />
    </div>
  );
}


function HistoryCalendar({
  month,
  days,
  currency,
  today,
  selected,
  picked,
  onSelect,
}: {
  month: string;
  days: readonly LedgerDay[];
  currency: string;
  today: string;
  selected: string | null;
  /** A week or month picked on the chart: its days are outlined. */
  picked: Period | null;
  onSelect: (date: string) => void;
}) {
  const c = currencyDef(currency);
  const byDate = useMemo(() => new Map(days.map((d) => [d.date, d])), [days]);
  const cells = monthCells(month);
  const max = Math.max(0, ...cells.map((date) => (date ? netEarned(byDate.get(date) ?? EMPTY_DAY, currency) : 0)));

  return (
    <section aria-labelledby="history-calendar" className="min-w-0">
      <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
        <h3 id="history-calendar" className="type-scale-section text-muted-foreground">
          {c?.label ?? currency} earned each day
        </h3>
        <div className="flex items-center gap-1.5 text-[0.6875rem] text-muted-foreground" aria-hidden>
          Less
          {HEAT.map((heat, i) => (
            <span key={i} className={cn("size-3 rounded-[3px] ring-1 ring-foreground/10", heat)} />
          ))}
          More
        </div>
      </div>
      <div className="grid grid-cols-7 gap-1" role="group" aria-label={`${monthLabel(month)}, by day`}>
        {WEEKDAYS.map((d) => (
          <div key={d} className="text-center text-[0.625rem] text-muted-foreground" aria-hidden>
            {d}
          </div>
        ))}
        {cells.map((date, i) => {
          if (!date) return <div key={`blank-${i}`} aria-hidden />;
          const day = byDate.get(date);
          const earned = day ? netEarned(day, currency) : 0;
          const spent = day?.spent[currency] ?? 0;
          const isToday = date === today;
          const label = `${formatDayFull(date)}${isToday ? " (today)" : ""}: ${totalsInWords(day ?? EMPTY_DAY)}`;
          return (
            <button
              key={date}
              type="button"
              aria-pressed={selected === date}
              aria-label={label}
              disabled={date > today}
              onClick={() => onSelect(date)}
              className={cn(
                "flex min-h-14 flex-col justify-between rounded-md p-1 text-left text-foreground ring-1 ring-foreground/8 transition-colors md:min-h-16 md:p-1.5",
                "hover:ring-foreground/30 focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-40",
                HEAT[heatLevel(earned, max)],
                picked && inPeriod(picked, date) && "ring-foreground/45",
                selected === date && "ring-2 ring-foreground hover:ring-foreground",
              )}
            >
              <span className={cn("text-[0.625rem] leading-none", isToday && "font-bold underline underline-offset-2")}>
                {Number(date.slice(8))}
              </span>
              <span className="flex flex-col items-end leading-tight tabular-nums" aria-hidden>
                {earned !== 0 ? (
                  <span className="text-xs font-medium">
                    {earned > 0 ? "+" : "−"}
                    {compact(Math.abs(earned))}
                  </span>
                ) : null}
                {spent > 0 ? <span className="text-[0.625rem]">−{compact(spent)}</span> : null}
              </span>
            </button>
          );
        })}
      </div>
      <p className="mt-2 text-[0.6875rem] text-muted-foreground">
        Each day shows {c?.label ?? currency} earned (net of anything given back) and, below it, any spent.
      </p>
    </section>
  );
}

function EarnedSpentChart({
  periods,
  currency,
  bucket,
  selected,
  today,
  onSelect,
}: {
  periods: PeriodTotals[];
  currency: string;
  bucket: HistoryBucket;
  selected: Period | null;
  today: string;
  onSelect: (period: Period) => void;
}) {
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const c = currencyDef(currency);
  const name = c?.label ?? currency;
  const tracked = c ? !c.spendable : false;
  const rows = periods.map((p) => ({
    period: p,
    earned: netEarned(p, currency),
    givenBack: p.reversed[currency] ?? 0,
    spent: p.spent[currency] ?? 0,
  }));
  const top = niceMax(Math.max(...rows.map((r) => Math.max(r.earned, r.spent)), 0));
  const height = (n: number) => (n > 0 ? `max(2px, ${(n / top) * 100}%)` : "0");
  const unit = bucket === "week" ? "week" : "month";

  function describe(r: (typeof rows)[number]): string {
    const parts = [`earned ${r.earned.toLocaleString()} ${name}`];
    if (r.givenBack) parts.push(`after ${r.givenBack.toLocaleString()} given back`);
    if (!tracked) parts.push(`spent ${r.spent.toLocaleString()}`);
    return `${periodLabel(r.period)}: ${parts.join(", ")}`;
  }

  return (
    <section aria-labelledby="history-chart" className="min-w-0">
      <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
        <h3 id="history-chart" className="type-scale-section text-muted-foreground">
          {tracked ? `${name} earned by ${unit}` : `${name} earned vs spent by ${unit}`}
        </h3>
        <ToggleGroup
          label="Show as"
          value={mode}
          onChange={setMode}
          options={[
            { id: "chart", label: "Chart" },
            { id: "table", label: "Table" },
          ]}
        />
      </div>

      {tracked ? (
        <p className="mb-2 text-[0.6875rem] text-muted-foreground">{name} is tracked, never spent.</p>
      ) : (
        <ul className="mb-2 flex gap-3 text-[0.6875rem] text-muted-foreground" aria-label="Legend">
          <li className="flex items-center gap-1.5">
            <span className="size-2.5 rounded-[2px] bg-chart-earned" aria-hidden /> Earned
          </li>
          <li className="flex items-center gap-1.5">
            <span className="size-2.5 rounded-[2px] bg-chart-spent" aria-hidden /> Spent
          </li>
        </ul>
      )}

      {mode === "chart" ? (
        <div>
          <div className="flex gap-2">
            <div className="relative h-40 w-9 shrink-0 text-right text-[0.625rem] tabular-nums text-muted-foreground" aria-hidden>
              <span className="absolute right-0 top-0 -translate-y-1/2">{compact(top)}</span>
              <span className="absolute right-0 top-1/2 -translate-y-1/2">{compact(top / 2)}</span>
              <span className="absolute bottom-0 right-0 translate-y-1/2">0</span>
            </div>
            <div className="relative h-40 min-w-0 flex-1 border-b border-foreground/20">
              <div className="absolute inset-x-0 top-0 border-t border-foreground/8" aria-hidden />
              <div className="absolute inset-x-0 top-1/2 border-t border-foreground/8" aria-hidden />
              <div className="relative flex h-full items-end gap-1" role="group" aria-label={`${name} by ${unit}`}>
                {rows.map((r, i) => {
                  const isSelected = !!selected && samePeriod(selected, r.period);
                  const isLast = i === rows.length - 1;
                  const future = r.period.start > today;
                  return (
                    <Tooltip key={r.period.start}>
                      <TooltipTrigger asChild>
                        <button
                          type="button"
                          aria-pressed={isSelected}
                          aria-label={describe(r)}
                          disabled={future}
                          onClick={() => onSelect(r.period)}
                          className={cn(
                            "flex h-full min-w-0 flex-1 items-end justify-center gap-0.5 rounded-t-md px-0.5 transition-colors",
                            "hover:bg-foreground/5 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none",
                            isSelected && "bg-foreground/12 ring-1 ring-foreground/20 ring-inset hover:bg-foreground/15",
                          )}
                        >
                          <Bar value={r.earned} height={height(r.earned)} tone="earned" label={isLast} />
                          {tracked ? null : (
                            <Bar value={r.spent} height={height(r.spent)} tone="spent" label={isLast} />
                          )}
                        </button>
                      </TooltipTrigger>
                      <TooltipContent side="top">
                        <span className="font-medium">{periodLabel(r.period)}</span>
                        <span>
                          +{r.earned.toLocaleString()} earned
                          {r.givenBack ? ` (after ${r.givenBack.toLocaleString()} given back)` : ""}
                          {tracked ? "" : ` · ${r.spent.toLocaleString()} spent`}
                        </span>
                      </TooltipContent>
                    </Tooltip>
                  );
                })}
              </div>
            </div>
          </div>
          <div className="flex gap-2" aria-hidden>
            <div className="w-9 shrink-0" />
            <div className="flex min-w-0 flex-1 gap-1 pt-1">
              {rows.map((r) => (
                <span
                  key={r.period.start}
                  className={cn(
                    "min-w-0 flex-1 truncate text-center text-[0.625rem] tabular-nums text-muted-foreground",
                    (selected ? samePeriod(selected, r.period) : inPeriod(r.period, today)) &&
                      "font-semibold text-foreground",
                  )}
                >
                  {shortPeriodLabel(r.period)}
                </span>
              ))}
            </div>
          </div>
          <p className="mt-2 text-[0.6875rem] text-muted-foreground">
            Pick a {unit} to read its entries below.
          </p>
        </div>
      ) : (
        <table className="w-full text-sm">
          <caption className="sr-only">
            {tracked ? `${name} earned by ${unit}` : `${name} earned and spent by ${unit}`}
          </caption>
          <thead>
            <tr className="text-left text-xs text-muted-foreground">
              <th scope="col" className="py-1 font-medium">
                {bucket === "week" ? "Week of" : "Month"}
              </th>
              <th scope="col" className="py-1 text-right font-medium">
                Earned
              </th>
              <th scope="col" className="py-1 text-right font-medium">
                Given back
              </th>
              {tracked ? null : (
                <th scope="col" className="py-1 text-right font-medium">
                  Spent
                </th>
              )}
            </tr>
          </thead>
          <tbody className="divide-y">
            {[...rows].reverse().map((r) => (
              <tr key={r.period.start}>
                <th scope="row" className="py-1.5 text-left font-normal">
                  <button
                    type="button"
                    className="underline-offset-2 hover:underline disabled:no-underline disabled:opacity-60"
                    aria-pressed={!!selected && samePeriod(selected, r.period)}
                    disabled={r.period.start > today}
                    onClick={() => onSelect(r.period)}
                  >
                    {r.period.kind === "week" ? shortPeriodLabel(r.period) : periodLabel(r.period)}
                  </button>
                </th>
                <td className="py-1.5 text-right tabular-nums">{r.earned.toLocaleString()}</td>
                <td className="py-1.5 text-right tabular-nums">{r.givenBack.toLocaleString()}</td>
                {tracked ? null : <td className="py-1.5 text-right tabular-nums">{r.spent.toLocaleString()}</td>}
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

/** One bar: at most 24px wide, rounded at its data end, square at the baseline. */
function Bar({
  value,
  height,
  tone,
  label,
}: {
  value: number;
  height: string;
  tone: "earned" | "spent";
  label: boolean;
}) {
  return (
    <span className="relative flex h-full w-full max-w-6 items-end">
      <span
        className={cn("w-full rounded-t-[4px]", tone === "earned" ? "bg-chart-earned" : "bg-chart-spent")}
        style={{ height }}
      />
      {label && value > 0 ? (
        <span
          className="absolute inset-x-0 text-center text-[0.625rem] tabular-nums text-foreground"
          style={{ bottom: `calc(${height} + 2px)` }}
          aria-hidden
        >
          {compact(value)}
        </span>
      ) : null}
    </span>
  );
}

function PeriodReport({
  period,
  totals,
  wholeMonth,
  onShowMonth,
  onOpenSubject,
}: {
  period: Period;
  totals: PeriodTotals;
  /** The month to go back to, when a day or a week is picked. */
  wholeMonth: Period | null;
  onShowMonth: () => void;
  onOpenSubject: (subjectId: string) => void;
}) {
  const ledger = useLedgerRange(localMidnightIso(period.start), localMidnightIso(period.end));
  const [entry, setEntry] = useState<EntryFilter>("all");
  const [currency, setCurrency] = useState("all");
  // While a newly picked period loads, the last one's entries stay up (dimmed); keep only
  // those inside this period so nothing outside it is listed, or counted in the dollars.
  const stale = ledger.isPlaceholderData;
  const all = (ledger.data ?? []).filter((row) => inPeriod(period, localDateFromTimestamp(row.occurredAt)));
  const rows = filterLedger(all, entry, currency);
  const groups = groupByDay(rows);
  const dollars = stale ? 0 : dollarsMoved(all);
  const capped = !stale && (ledger.data?.length ?? 0) >= LEDGER_RANGE_LIMIT;
  const lines = [
    { label: "Earned", amounts: totals.earned, tone: "text-success" },
    { label: "Given back", amounts: negate(totals.reversed), tone: "" },
    { label: "Spent", amounts: negate(totals.spent), tone: "" },
  ].filter((l) => Object.keys(l.amounts).length > 0);

  return (
    <section aria-labelledby="history-period" className="space-y-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h3 id="history-period" className="font-heading text-xl">
          {periodLabel(period)}
        </h3>
        {wholeMonth ? (
          <Button variant="ghost" size="sm" onClick={onShowMonth}>
            Show all of {monthLabel(wholeMonth.start)}
          </Button>
        ) : null}
      </div>

      {lines.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nothing earned or spent.</p>
      ) : (
        <dl className="grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]" aria-label={`Totals for ${periodLabel(period)}`}>
          {lines.map((l) => (
            <div key={l.label} className="contents">
              <dt className="text-muted-foreground">{l.label}</dt>
              <dd className={cn("tabular-nums", l.tone)}>
                {formatAmounts(l.amounts, { signed: true })}
                {l.label === "Spent" && dollars > 0 ? (
                  <span className="text-muted-foreground"> · {formatUsd(dollars)} in real money</span>
                ) : null}
              </dd>
            </div>
          ))}
        </dl>
      )}

      <div className="flex flex-wrap items-center gap-3" role="group" aria-label="Entry filters">
        <ToggleGroup label="Show" value={entry} onChange={setEntry} options={ENTRY_FILTERS} />
        <Select value={currency} onValueChange={setCurrency}>
          <SelectTrigger aria-label="Currency of entries" className="h-8 w-40 text-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">Every currency</SelectItem>
            {CURRENCIES.map((c) => (
              <SelectItem key={c.key} value={c.key}>
                {c.symbol} {c.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {ledger.isLoading ? <ListSkeleton rows={4} /> : null}
      {ledger.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The entries failed to load.
        </p>
      ) : null}
      {ledger.data && !stale && rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">
          {all.length === 0 ? "No entries." : "No entries match these filters."}
        </p>
      ) : null}

      {groups.length > 0 ? (
        <ol
          className={cn("space-y-4 transition-opacity", stale && "opacity-60")}
          aria-label={`Entries for ${periodLabel(period)}`}
          aria-busy={stale || undefined}
        >
          {groups.map((g) => (
            <li key={g.date}>
              <h4 className="mb-1.5 text-xs font-medium text-muted-foreground">{formatDayFull(g.date)}</h4>
              <ul className="divide-y rounded-xl ring-1 ring-foreground/10">
                {g.rows.map((row) => (
                  <LedgerEntry key={row.id} row={row} onOpenSubject={onOpenSubject} />
                ))}
              </ul>
            </li>
          ))}
        </ol>
      ) : null}
      {capped ? (
        <p className="text-xs text-muted-foreground">
          Showing the newest {LEDGER_RANGE_LIMIT} entries; pick a single day or week to see the rest.
        </p>
      ) : null}
    </section>
  );
}

function LedgerEntry({ row, onOpenSubject }: { row: LedgerRow; onOpenSubject: (subjectId: string) => void }) {
  const subject = row.subject;
  const time = new Date(row.occurredAt).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
  const detail = [time, entryDetail(row), row.dollars != null ? formatUsd(row.dollars) : ""].filter(Boolean);
  return (
    <li className="flex items-start justify-between gap-3 px-3 py-2.5 text-sm">
      <div className="min-w-0">
        {subject ? (
          <button
            type="button"
            className="flex max-w-full items-center gap-1.5 text-left font-medium underline-offset-2 hover:underline focus-visible:underline"
            onClick={() => onOpenSubject(subject.id)}
          >
            <TypeIcon type={subject.type} className="size-3.5 shrink-0" />
            <span className="truncate">{subject.title}</span>
          </button>
        ) : (
          <p className="truncate font-medium">{entryTitle(row)}</p>
        )}
        <p className="text-[0.6875rem] text-muted-foreground">{detail.join(" · ")}</p>
      </div>
      <span
        className={cn(
          "shrink-0 pt-0.5 text-right text-xs tabular-nums",
          row.entry === "earn" ? "text-success" : "text-muted-foreground",
        )}
      >
        {formatAmounts(row.amounts, { signed: true })}
      </span>
    </li>
  );
}
