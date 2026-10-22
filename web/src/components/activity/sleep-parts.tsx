import {
  bedToWake,
  formatDuration,
  ouraScoreWords,
  SCORE_TONE,
  scoreBand,
  sleepStages,
  STAGE_NAMES,
  STAGE_TONE,
} from "@/lib/activity";
import type { SleepPeriodRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/** A count tile: a label, the number, and a line under it. */
export function StatTile({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="rounded-lg bg-muted/60 p-3">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-lg font-semibold tabular-nums">{value}</dd>
      {note ? <dd className="text-xs text-muted-foreground">{note}</dd> : null}
    </div>
  );
}

/** A sleep or readiness score with its band: a swatch and Oura's word, so colour isn't the only cue. */
export function ScoreFigure({ label, value, large = false }: { label: string; value: number | null; large?: boolean }) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn("font-semibold tabular-nums", large ? "text-2xl" : "text-xl")}>{value ?? "—"}</dd>
      {value != null ? (
        <dd className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <span className={cn("size-2.5 shrink-0 rounded-[2px]", SCORE_TONE[scoreBand(value)])} aria-hidden />
          {ouraScoreWords(value)}
        </dd>
      ) : null}
    </div>
  );
}

/** "73 Good": a score with its band, for a table cell. */
export function ScoreCell({ value }: { value: number | null }) {
  if (value == null) return <span className="text-muted-foreground">—</span>;
  return (
    <span className="inline-flex flex-col items-end leading-tight">
      <span className="inline-flex items-center gap-1.5">
        <span className={cn("size-2 shrink-0 rounded-[2px]", SCORE_TONE[scoreBand(value)])} aria-hidden />
        {value}
      </span>
      <span className="text-[0.6875rem] text-muted-foreground">{ouraScoreWords(value)}</span>
    </span>
  );
}

/**
 * A period's time in bed by stage, deepest first: a bar split by share, with a 2px gap between
 * the parts, and the list under it that says each one in words.
 */
export function StageBar({ period }: { period: SleepPeriodRow }) {
  const stages = sleepStages(period);
  if (stages.length === 0) {
    return <p className="text-sm text-muted-foreground">Oura has no stages for this night.</p>;
  }
  return (
    <div>
      <div className="flex h-3 gap-[2px]" aria-hidden>
        {stages
          .filter((s) => s.seconds > 0)
          .map((s, i, shown) => (
            <div
              key={s.stage}
              data-metric-bar=""
              className={cn(
                "min-w-[2px]",
                STAGE_TONE[s.stage],
                i === 0 && "rounded-l-[4px]",
                i === shown.length - 1 && "rounded-r-[4px]",
              )}
              style={{ flexGrow: s.seconds, flexBasis: 0 }}
            />
          ))}
      </div>
      <ul aria-label="Stages" className="mt-2 grid grid-cols-2 gap-x-6 gap-y-1 text-xs">
        {stages.map((s) => (
          <li key={s.stage} data-testid="sleep-stage" className="flex items-center gap-1.5">
            <span className={cn("size-2.5 shrink-0 rounded-[2px]", STAGE_TONE[s.stage])} aria-hidden />
            <span>{STAGE_NAMES[s.stage]}</span>
            <span className="ml-auto whitespace-nowrap tabular-nums">{formatDuration(s.seconds)}</span>
            <span className="w-8 text-right tabular-nums text-muted-foreground">{Math.round(s.share * 100)}%</span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** One contributor (0–100) as a small meter toned by Oura's band, with the number and the word. */
export function ContributorMeter({ name, value }: { name: string; value: number | null }) {
  if (value == null) {
    return (
      <li className="flex items-center justify-between gap-3 text-sm">
        <span>{name}</span>
        <span className="text-xs text-muted-foreground">Not given</span>
      </li>
    );
  }
  const words = ouraScoreWords(value);
  return (
    <li className="space-y-1">
      <div className="flex items-center justify-between gap-3 text-sm">
        <span>{name}</span>
        <span className="text-xs tabular-nums text-muted-foreground">
          {value} · {words}
        </span>
      </div>
      <div
        role="meter"
        aria-label={name}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={value}
        aria-valuetext={`${value}, ${words.toLowerCase()}`}
        className="h-1.5 rounded-full bg-muted"
      >
        <div
          data-metric-bar=""
          className={cn("h-full rounded-full", SCORE_TONE[scoreBand(value)])}
          style={{ width: `${Math.min(Math.max(value, 0), 100)}%` }}
        />
      </div>
    </li>
  );
}

function Fact({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="flex justify-between gap-4 py-1.5">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right tabular-nums">{value ?? "—"}</dd>
    </div>
  );
}

/** The night's numbers: asleep against in bed, bed and wake, how fast, how well, and the heart. */
export function NightFacts({ night }: { night: SleepPeriodRow }) {
  return (
    <dl className="divide-y text-sm">
      <Fact
        label="Asleep"
        value={
          night.asleepS != null
            ? `${formatDuration(night.asleepS)}${night.inBedS != null ? ` of ${formatDuration(night.inBedS)} in bed` : ""}`
            : null
        }
      />
      <Fact label="Bed to wake" value={bedToWake(night)} />
      <Fact label="Fell asleep in" value={night.latencyS != null ? formatDuration(night.latencyS) : null} />
      <Fact label="Efficiency" value={night.efficiency != null ? `${night.efficiency}%` : null} />
      <Fact label="Average HRV" value={night.averageHrv != null ? `${night.averageHrv} ms` : null} />
      <Fact label="Average heart rate" value={night.averageHr != null ? `${Math.round(night.averageHr)} bpm` : null} />
      <Fact label="Lowest heart rate" value={night.lowestHr != null ? `${night.lowestHr} bpm` : null} />
    </dl>
  );
}
