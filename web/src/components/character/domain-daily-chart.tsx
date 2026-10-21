import { useId, useState } from "react";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { formatDay } from "@/lib/dates";
import type { DomainDayRow } from "@/lib/production-ui-types";
import { useDomainDays } from "@/lib/queries";
import { loadView, saveView } from "@/lib/view-state";
import { cn } from "@/lib/utils";

const RANGES = [
  { id: "30", label: "30 days" },
  { id: "90", label: "90 days" },
  { id: "365", label: "1 year" },
] as const;
type Range = (typeof RANGES)[number]["id"];

const VIEW = "character.chart";

/** A clean axis top at or above `n`: 100, 150, 200, 250, 300, 400, 500, 600, 800, 1,000… */
function niceTop(n: number): number {
  const steps = [1, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10];
  const scale = 10 ** Math.floor(Math.log10(Math.max(n, 1)));
  return (steps.find((s) => s * scale >= n) ?? 10) * scale;
}

function describe(d: DomainDayRow, target: number | null): string {
  const paid = d.paid !== d.base ? ` (${d.paid.toLocaleString()} paid)` : "";
  const verdict =
    d.succeeded == null ? "" : d.succeeded ? " · a successful day" : target != null ? ` · short of ${target}` : "";
  return `${formatDay(d.day)}: ${d.base.toLocaleString()} base XP${paid}${verdict}`;
}

/**
 * A domain's daily chart (xp-domains plan §3.12): bars of base XP per day with the success
 * line (70 for Health) across them, the last 30 days by default. Successful days are the
 * full tone; days short of the line a lighter step of it, so position and tone agree. Hover
 * or arrow keys read any day; the table view lists them all.
 */
export function DomainDailyChart({ domain, name }: { domain: string; name: string }) {
  const [range, setRangeState] = useState<Range>(() => loadView<{ range: Range }>(VIEW, { range: "30" }).range);
  const [mode, setMode] = useState<"chart" | "table">("chart");
  const [hovered, setHovered] = useState<number | null>(null);
  const days = useDomainDays(domain, Number(range));
  const titleId = useId();
  const readoutId = useId();

  const setRange = (next: Range) => {
    setRangeState(next);
    setHovered(null);
    try {
      saveView(VIEW, { range: next });
    } catch {
      // Storage can be full or blocked; the range still applies to this view.
    }
  };

  const data = days.data;
  const rows = data?.days ?? [];
  const target = data?.target ?? null;
  // Headroom above the tallest bar keeps the readout clear of the marks.
  const top = niceTop(Math.max(target ?? 0, ...rows.map((d) => d.base), 1) * 1.35);
  const successful = rows.filter((d) => d.succeeded).length;
  const label = RANGES.find((r) => r.id === range)!.label;
  const dense = rows.length > 90;
  const shown = hovered != null ? rows[hovered] : null;

  function pick(clientX: number, el: HTMLElement) {
    const box = el.getBoundingClientRect();
    if (rows.length === 0 || box.width === 0) return;
    const i = Math.floor(((clientX - box.left) / box.width) * rows.length);
    setHovered(Math.min(Math.max(i, 0), rows.length - 1));
  }

  function onKey(e: React.KeyboardEvent) {
    if (rows.length === 0) return;
    const last = rows.length - 1;
    const now = hovered ?? last;
    const next =
      e.key === "ArrowLeft" ? now - 1 : e.key === "ArrowRight" ? now + 1 : e.key === "Home" ? 0 : e.key === "End" ? last : null;
    if (next == null) return;
    e.preventDefault();
    setHovered(Math.min(Math.max(next, 0), last));
  }

  return (
    <section aria-labelledby={titleId} className="space-y-3 border-t px-4 py-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 id={titleId} className="type-scale-section text-muted-foreground">
          {name} base XP by day
        </h3>
        <div className="flex flex-wrap items-center gap-3">
          <ToggleGroup label="Range" value={range} onChange={setRange} options={[...RANGES]} />
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
      </div>

      {days.isLoading ? <ListSkeleton rows={3} /> : null}
      {days.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The daily chart failed to load.
        </p>
      ) : null}

      {data ? (
        <div className={cn("space-y-2 transition-opacity", days.isPlaceholderData && "opacity-60")}>
          {target != null ? (
            <p className="text-sm">
              <span className="font-medium">
                {successful} of the last {rows.length} days
              </span>{" "}
              <span className="text-muted-foreground">reached {target} base XP</span>
            </p>
          ) : null}

          {mode === "chart" ? (
            <div>
              <div className="flex gap-2">
                <div
                  className="relative h-44 w-9 shrink-0 text-right text-[0.625rem] tabular-nums text-muted-foreground"
                  aria-hidden
                >
                  <span className="absolute right-0 top-0 -translate-y-1/2">{top.toLocaleString()}</span>
                  {target != null ? (
                    <span
                      className="absolute right-0 translate-y-1/2 font-semibold text-foreground"
                      style={{ bottom: `${(target / top) * 100}%` }}
                    >
                      {target}
                    </span>
                  ) : null}
                  <span className="absolute bottom-0 right-0 translate-y-1/2">0</span>
                </div>
                <div
                  role="group"
                  aria-label={`${name} base XP by day, last ${label}. Use the arrow keys to read a day.`}
                  aria-describedby={readoutId}
                  tabIndex={0}
                  onKeyDown={onKey}
                  onFocus={() => setHovered((h) => h ?? rows.length - 1)}
                  onBlur={() => setHovered(null)}
                  onPointerMove={(e) => pick(e.clientX, e.currentTarget)}
                  onPointerLeave={() => setHovered(null)}
                  className="relative h-44 min-w-0 flex-1 touch-none rounded-sm border-b border-foreground/20 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                >
                  <div className="absolute inset-x-0 top-0 border-t border-foreground/8" aria-hidden />
                  <div
                    className={cn("absolute inset-0 flex items-end", dense ? "gap-0" : "gap-[2px]")}
                    aria-hidden
                  >
                    {rows.map((d, i) => (
                      <div key={d.day} className="flex h-full min-w-0 flex-1 items-end justify-center">
                        <div
                          data-testid="day-bar"
                          className={cn(
                            "w-full max-w-6",
                            !dense && "rounded-t-[4px]",
                            d.succeeded ? "bg-chart-earned" : "bg-chart-earned/35",
                            hovered === i && "brightness-110 ring-2 ring-foreground/40",
                          )}
                          style={{ height: d.base > 0 ? `max(2px, ${(d.base / top) * 100}%)` : 0 }}
                        />
                      </div>
                    ))}
                  </div>
                  {target != null ? (
                    <div
                      className="pointer-events-none absolute inset-x-0 border-t-2 border-dashed border-foreground/60"
                      style={{ bottom: `${(target / top) * 100}%` }}
                      aria-hidden
                    />
                  ) : null}
                  {shown ? (
                    <div
                      className="pointer-events-none absolute top-1 z-10 rounded-md bg-popover px-2 py-1 text-xs whitespace-nowrap text-popover-foreground shadow-md ring-1 ring-foreground/10"
                      style={{
                        left: `${((hovered! + 0.5) / rows.length) * 100}%`,
                        transform: `translateX(${hovered! < rows.length / 2 ? "-10%" : "-90%"})`,
                      }}
                      aria-hidden
                    >
                      <span className="font-semibold">{shown.base.toLocaleString()} base XP</span>
                      {shown.paid !== shown.base ? (
                        <span className="text-muted-foreground"> · {shown.paid.toLocaleString()} paid</span>
                      ) : null}
                      <span className="block text-muted-foreground">
                        {formatDay(shown.day)}
                        {shown.succeeded ? " · successful" : ""}
                      </span>
                    </div>
                  ) : null}
                </div>
              </div>
              <div className="flex gap-2 pt-1 text-[0.625rem] tabular-nums text-muted-foreground" aria-hidden>
                <div className="w-9 shrink-0" />
                <div className="flex min-w-0 flex-1 justify-between">
                  <span>{rows[0] ? formatDay(rows[0].day) : ""}</span>
                  <span>{rows.length > 2 ? formatDay(rows[Math.floor(rows.length / 2)].day) : ""}</span>
                  <span>Today</span>
                </div>
              </div>
              <p id={readoutId} className="sr-only" aria-live="polite">
                {shown ? describe(shown, target) : ""}
              </p>
              <ul className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-[0.6875rem] text-muted-foreground" aria-label="Legend">
                <li className="flex items-center gap-1.5">
                  <span className="size-2.5 rounded-[2px] bg-chart-earned" aria-hidden /> A successful day
                </li>
                <li className="flex items-center gap-1.5">
                  <span className="size-2.5 rounded-[2px] bg-chart-earned/35" aria-hidden />
                  {target != null ? `Short of ${target}` : "Base XP"}
                </li>
                {target != null ? (
                  <li className="flex items-center gap-1.5">
                    <span className="w-3 border-t-2 border-dashed border-foreground/60" aria-hidden /> {target} base XP
                  </li>
                ) : null}
              </ul>
            </div>
          ) : (
            // Focusable so a keyboard can scroll a year of rows.
            <div
              tabIndex={0}
              role="region"
              aria-label={`${name} base XP by day, as a table`}
              className="max-h-80 overflow-y-auto rounded-sm focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
            >
              <table className="w-full text-sm">
                <caption className="sr-only">
                  {name} base XP by day, last {label}, newest first
                </caption>
                <thead>
                  <tr className="text-left text-xs text-muted-foreground">
                    <th scope="col" className="py-1 font-medium">
                      Day
                    </th>
                    <th scope="col" className="py-1 text-right font-medium">
                      Base XP
                    </th>
                    <th scope="col" className="py-1 text-right font-medium">
                      Paid
                    </th>
                    {target != null ? (
                      <th scope="col" className="py-1 text-right font-medium">
                        Successful
                      </th>
                    ) : null}
                  </tr>
                </thead>
                <tbody className="divide-y">
                  {[...rows].reverse().map((d) => (
                    <tr key={d.day}>
                      <th scope="row" className="py-1.5 text-left font-normal">
                        {formatDay(d.day)}
                      </th>
                      <td className="py-1.5 text-right tabular-nums">{d.base.toLocaleString()}</td>
                      <td className="py-1.5 text-right tabular-nums">{d.paid.toLocaleString()}</td>
                      {target != null ? <td className="py-1.5 text-right">{d.succeeded ? "Yes" : "No"}</td> : null}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      ) : null}
    </section>
  );
}
