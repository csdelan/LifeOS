import { Input } from "@/components/ui/input";
import { todayIso, weekdayIndex } from "@/lib/dates";
import type { IntervalUnit, RecurrenceFreq, RecurrenceSpec } from "@/lib/production-ui-types";
import {
  INTERVAL_UNITS,
  parseRecurrence,
  recurrenceLabel,
  serializeRecurrence,
  WEEKDAY_TOKENS,
  WEEKDAYS,
} from "@/lib/recurrence";
import { cn } from "@/lib/utils";

const MODES: readonly (readonly [RecurrenceFreq, string])[] = [
  ["daily", "Daily"],
  ["weekly", "Weekly"],
  ["interval", "Every…"],
  ["monthly", "Monthly"],
];

const WEEKDAY_NAMES = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

/** The pattern a mode starts from, keeping what still applies from the current one. */
function startFor(freq: RecurrenceFreq, current: RecurrenceSpec | null): RecurrenceSpec {
  if (current?.freq === freq) return current;
  const today = todayIso();
  if (freq === "weekly") return { freq, on: [WEEKDAY_TOKENS[weekdayIndex(today)]] };
  if (freq === "interval") return { freq, every: 2, unit: "days" };
  if (freq === "monthly") return { freq, day: Number(today.slice(8, 10)) };
  return { freq: "daily" };
}

/**
 * Edits a repeat pattern in the kernel's shape (D8). `value` and `onChange` carry the pattern
 * as text (`serializeRecurrence`), `""` for none; with `allowNone` the first choice is
 * "Doesn't repeat". A cue pattern (`bsk recur --freq trigger`) shows as text; choosing another
 * mode replaces it.
 */
export function RecurrenceEditor({
  value,
  onChange,
  disabled,
  allowNone,
}: {
  value?: string;
  onChange: (next: string) => void;
  disabled?: boolean;
  allowNone?: boolean;
}) {
  const spec = parseRecurrence(value);
  const set = (next: RecurrenceSpec) => onChange(serializeRecurrence(next));
  const chip = (on: boolean) =>
    cn(
      "rounded-md px-2 py-1 text-xs font-medium",
      on ? "bg-primary/12 text-primary" : "text-muted-foreground hover:bg-muted",
      disabled && "opacity-60",
    );

  return (
    <div className="space-y-2">
      <div role="group" aria-label="Repeat" className="flex flex-wrap gap-1">
        {allowNone ? (
          <button
            type="button"
            disabled={disabled}
            aria-pressed={!spec}
            onClick={() => onChange("")}
            className={chip(!spec)}
          >
            Doesn't repeat
          </button>
        ) : null}
        {MODES.map(([freq, label]) => (
          <button
            key={freq}
            type="button"
            disabled={disabled}
            aria-pressed={spec?.freq === freq}
            onClick={() => set(startFor(freq, spec))}
            className={chip(spec?.freq === freq)}
          >
            {label}
          </button>
        ))}
      </div>

      {spec?.freq === "weekly" ? (
        <div role="group" aria-label="On these days" className="flex flex-wrap gap-1">
          {WEEKDAY_TOKENS.map((token, i) => {
            const on = spec.on?.includes(token) ?? false;
            const only = on && spec.on?.length === 1;
            return (
              <button
                key={token}
                type="button"
                disabled={disabled}
                aria-pressed={on}
                aria-label={WEEKDAY_NAMES[i]}
                title={only ? "A weekly repeat needs at least one day" : WEEKDAY_NAMES[i]}
                onClick={() => {
                  if (only) return;
                  const next = on ? (spec.on ?? []).filter((d) => d !== token) : [...(spec.on ?? []), token];
                  set({ freq: "weekly", on: next });
                }}
                className={cn(
                  "size-8 rounded-md text-[0.6875rem] font-medium",
                  on ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground",
                )}
              >
                {WEEKDAYS[i].slice(0, 1)}
              </button>
            );
          })}
        </div>
      ) : null}

      {spec?.freq === "interval" ? (
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-xs text-muted-foreground">Every</span>
          <Input
            type="number"
            min={1}
            inputMode="numeric"
            aria-label="How many"
            disabled={disabled}
            className="w-20"
            value={spec.every ?? 1}
            onChange={(e) => set({ ...spec, every: Math.max(1, Math.floor(Number(e.target.value)) || 1) })}
          />
          <div role="group" aria-label="Unit" className="flex gap-1">
            {INTERVAL_UNITS.map((unit: IntervalUnit) => (
              <button
                key={unit}
                type="button"
                disabled={disabled}
                aria-pressed={spec.unit === unit}
                onClick={() => set({ ...spec, unit })}
                className={chip(spec.unit === unit)}
              >
                {unit}
              </button>
            ))}
          </div>
          <span className="w-full text-xs text-muted-foreground">Counted from the start date.</span>
        </div>
      ) : null}

      {spec?.freq === "monthly" ? (
        <div className="flex flex-wrap items-center gap-2">
          <button
            type="button"
            disabled={disabled}
            aria-pressed={!!spec.day}
            onClick={() => set({ freq: "monthly", day: spec.day ?? Number(todayIso().slice(8, 10)) })}
            className={chip(!!spec.day)}
          >
            On day
          </button>
          {spec.day ? (
            <Input
              type="number"
              min={1}
              max={31}
              inputMode="numeric"
              aria-label="Day of the month"
              disabled={disabled}
              className="w-20"
              value={spec.day}
              onChange={(e) =>
                set({ freq: "monthly", day: Math.min(31, Math.max(1, Math.floor(Number(e.target.value)) || 1)) })
              }
            />
          ) : null}
          <button
            type="button"
            disabled={disabled}
            aria-pressed={!spec.day}
            onClick={() => set({ freq: "monthly", on: ["last"] })}
            className={chip(!spec.day)}
          >
            Last day
          </button>
          {spec.day && spec.day > 28 ? (
            <span className="w-full text-xs text-muted-foreground">Months without day {spec.day} are skipped.</span>
          ) : null}
        </div>
      ) : null}

      {spec?.freq === "trigger" ? <p className="text-xs text-muted-foreground">{recurrenceLabel(spec)}</p> : null}
    </div>
  );
}
