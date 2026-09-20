import { useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { addDays, addMonths, daysInMonth, formatDay, startOfMonth, todayIso, weekdayIndex } from "@/lib/dates";
import { isOccurrenceDate, parseRecurrence } from "@/lib/recurrence";
import type { AdherenceState, HabitOccurrenceRow, RecurrenceSpec } from "@/lib/production-ui-types";
import { HABIT_STATE_SURFACE } from "@/components/habits/habit-state-styles";
import { cn } from "@/lib/utils";

const WEEK = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

export function StreakGrid({
  occurrences,
  recurrence,
  startDate,
  endDate,
  allowsPartial,
  onRecord,
}: {
  occurrences: HabitOccurrenceRow[];
  recurrence?: string | null;
  startDate?: string | null;
  endDate?: string | null;
  allowsPartial: boolean;
  onRecord: (date: string, state: "followed" | "partial" | "missed", note?: string) => void;
}) {
  const today = todayIso();
  const [month, setMonth] = useState(startOfMonth(today));
  const spec = parseRecurrence(recurrence);
  const byDate = useMemo(() => {
    const map = new Map<string, HabitOccurrenceRow>();
    for (const o of occurrences) map.set(o.occurrenceDate, o);
    return map;
  }, [occurrences]);

  const dim = daysInMonth(month);
  const firstWeekday = weekdayIndex(month);
  const cells: (string | null)[] = [
    ...Array.from({ length: firstWeekday }, () => null),
    ...Array.from({ length: dim }, (_, i) => addDays(month, i)),
  ];
  while (cells.length % 7 !== 0) cells.push(null);

  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <p className="type-scale-section text-muted-foreground">Streak</p>
        <div className="flex items-center gap-1">
          <Button variant="ghost" size="xs" aria-label="Previous month" onClick={() => setMonth(addMonths(month, -1))}>
            Prev
          </Button>
          <span className="min-w-28 text-center text-xs font-medium">
            {formatMonth(month)}
          </span>
          <Button variant="ghost" size="xs" aria-label="Next month" onClick={() => setMonth(addMonths(month, 1))}>
            Next
          </Button>
        </div>
      </div>
      <div className="mb-2 flex flex-wrap gap-3 text-[0.6875rem] text-muted-foreground">
        <Legend className={HABIT_STATE_SURFACE.unrecorded} label="Unrecorded" />
        <Legend className={HABIT_STATE_SURFACE.followed} label="Followed" />
        <Legend className={HABIT_STATE_SURFACE.partial} label="Partial" />
        <Legend className={HABIT_STATE_SURFACE.not_followed} label="Not followed" />
      </div>
      <div className="grid grid-cols-7 gap-1">
        {WEEK.map((d) => (
          <div key={d} className="text-center text-[0.625rem] text-muted-foreground">
            {d}
          </div>
        ))}
        {cells.map((iso, i) =>
          iso ? (
            <DayCell
              key={iso}
              iso={iso}
              today={today}
              spec={spec}
              startDate={startDate}
              endDate={endDate}
              row={byDate.get(iso)}
              allowsPartial={allowsPartial}
              onRecord={onRecord}
            />
          ) : (
            <div key={`e-${i}`} />
          ),
        )}
      </div>
    </div>
  );
}

function DayCell({
  iso,
  today,
  spec,
  startDate,
  endDate,
  row,
  allowsPartial,
  onRecord,
}: {
  iso: string;
  today: string;
  spec: RecurrenceSpec;
  startDate?: string | null;
  endDate?: string | null;
  row?: HabitOccurrenceRow;
  allowsPartial: boolean;
  onRecord: (date: string, state: "followed" | "partial" | "missed", note?: string) => void;
}) {
  const expected = isOccurrenceDate(spec, iso, startDate, endDate);
  const future = iso > today;
  const state: AdherenceState = row?.state ?? (expected && iso < today ? "unrecorded" : "unrecorded");
  const [note, setNote] = useState(row?.note ?? "");
  const [open, setOpen] = useState(false);
  const dayNum = Number(iso.slice(-2));

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger
        disabled={future}
        aria-label={`${formatDay(iso)}: ${state.replace("_", " ")}`}
        className={cn(
          "flex aspect-square min-h-11 min-w-11 items-center justify-center rounded-md text-[0.6875rem] font-medium tabular-nums",
          HABIT_STATE_SURFACE[state],
          !expected && "opacity-40",
          future && "cursor-not-allowed opacity-30",
          iso === today && "ring-2 ring-ring/50",
        )}
      >
        {dayNum}
      </PopoverTrigger>
      <PopoverContent className="w-56" align="start">
        <p className="text-xs font-medium">{formatDay(iso)}</p>
        <p className="text-[0.6875rem] text-muted-foreground capitalize">
          {state.replace("_", " ")}
          {row?.note ? ` · ${row.note}` : ""}
        </p>
        <div className="mt-2 flex flex-col gap-1">
          <Button
            size="sm"
            variant={state === "followed" ? "default" : "outline"}
            onClick={() => {
              onRecord(iso, "followed", note || undefined);
              setOpen(false);
            }}
          >
            Followed
          </Button>
          {allowsPartial ? (
            <Button
              size="sm"
              variant={state === "partial" ? "default" : "outline"}
              onClick={() => {
                onRecord(iso, "partial", note || undefined);
                setOpen(false);
              }}
            >
              Partial
            </Button>
          ) : null}
          <Button
            size="sm"
            variant={state === "not_followed" ? "default" : "outline"}
            onClick={() => {
              onRecord(iso, "missed", note || undefined);
              setOpen(false);
            }}
          >
            Missed
          </Button>
        </div>
        <div className="mt-2 space-y-1">
          <Label className="text-[0.6875rem]">Optional note</Label>
          <Input value={note} onChange={(e) => setNote(e.target.value)} placeholder="Context…" />
        </div>
      </PopoverContent>
    </Popover>
  );
}

function Legend({ className, label }: { className: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={cn("size-3 rounded-sm", className)} />
      {label}
    </span>
  );
}

function formatMonth(iso: string): string {
  const [y, m] = iso.split("-").map(Number);
  return new Date(y, m - 1, 1).toLocaleDateString(undefined, { month: "long", year: "numeric" });
}
