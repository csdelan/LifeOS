import { useMemo, useState } from "react";
import { FlameIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { DetailSection } from "@/components/primitives/detail-section";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { addDays, addMonths, daysInMonth, formatDay, startOfMonth, weekdayIndex } from "@/lib/dates";
import { useHomeToday } from "@/lib/home-day";
import { useOccurrences } from "@/lib/queries";
import type { AdherenceState, HabitOccurrenceRow } from "@/lib/production-ui-types";
import { HABIT_STATE_SURFACE } from "@/components/habits/habit-state-styles";
import { cn } from "@/lib/utils";

const WEEK = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

/**
 * A month of a habit's days. The days it has are the kernel's (`occurrences`, `lib/habit-days.ts`):
 * the ones its pattern schedules, up to today, and any with something recorded. Any other day up
 * to today is faded, and can log a session done anyway but not be missed. It reads the month it
 * shows, not the habit's whole history (code review CR107).
 */
export function StreakGrid({
  habitId,
  allowsPartial,
  onRecord,
}: {
  habitId: string;
  allowsPartial: boolean;
  /** Records a day; left out, the grid only shows the days (a Planned habit takes none). */
  onRecord?: (date: string, state: "followed" | "partial" | "missed", note?: string) => void;
}) {
  const today = useHomeToday();
  const [month, setMonth] = useState(startOfMonth(today));
  const dim = daysInMonth(month);
  const { data: occurrences } = useOccurrences(habitId, month, addDays(month, dim - 1));
  const byDate = useMemo(() => {
    const map = new Map<string, HabitOccurrenceRow>();
    for (const o of occurrences ?? []) map.set(o.occurrenceDate, o);
    return map;
  }, [occurrences]);

  const firstWeekday = weekdayIndex(month);
  const cells: (string | null)[] = [
    ...Array.from({ length: firstWeekday }, () => null),
    ...Array.from({ length: dim }, (_, i) => addDays(month, i)),
  ];
  while (cells.length % 7 !== 0) cells.push(null);

  return (
    <DetailSection
      title="Streak"
      tone="ember"
      icon={FlameIcon}
      action={
        <>
          <Button variant="ghost" size="xs" aria-label="Previous month" onClick={() => setMonth(addMonths(month, -1))}>
            Prev
          </Button>
          <span className="min-w-28 text-center text-xs font-medium">{formatMonth(month)}</span>
          <Button variant="ghost" size="xs" aria-label="Next month" onClick={() => setMonth(addMonths(month, 1))}>
            Next
          </Button>
        </>
      }
    >
      <div className="flex flex-wrap gap-3 text-[0.6875rem] text-muted-foreground">
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
              row={byDate.get(iso)}
              allowsPartial={allowsPartial}
              onRecord={onRecord}
            />
          ) : (
            <div key={`e-${i}`} />
          ),
        )}
      </div>
    </DetailSection>
  );
}

function DayCell({
  iso,
  today,
  row,
  allowsPartial,
  onRecord,
}: {
  iso: string;
  today: string;
  /** The kernel's day, when the habit has one here. */
  row?: HabitOccurrenceRow;
  allowsPartial: boolean;
  onRecord?: (date: string, state: "followed" | "partial" | "missed", note?: string) => void;
}) {
  const future = iso > today;
  const state: AdherenceState = row?.state ?? "unrecorded";
  const said = row ? state.replace("_", " ") : future ? "to come" : "not due";
  const [note, setNote] = useState(row?.note ?? "");
  const [open, setOpen] = useState(false);
  const dayNum = Number(iso.slice(-2));
  const cell = cn(
    "flex aspect-square min-h-11 min-w-11 items-center justify-center rounded-md text-[0.6875rem] font-medium tabular-nums",
    HABIT_STATE_SURFACE[state],
    !row && "opacity-40",
    future && "cursor-not-allowed opacity-30",
    iso === today && "ring-2 ring-ring/50",
  );

  if (!onRecord) {
    return (
      <div role="img" aria-label={`${formatDay(iso)}: ${said}`} className={cell}>
        {dayNum}
      </div>
    );
  }

  const record = (s: "followed" | "partial" | "missed") => {
    onRecord(iso, s, note || undefined);
    setOpen(false);
  };
  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger disabled={future} aria-label={`${formatDay(iso)}: ${said}`} className={cell}>
        {dayNum}
      </PopoverTrigger>
      <PopoverContent className="w-56" align="start">
        <p className="text-xs font-medium">{formatDay(iso)}</p>
        <p className="text-[0.6875rem] text-muted-foreground capitalize">
          {said}
          {row?.note ? ` · ${row.note}` : ""}
        </p>
        <div className="mt-2 flex flex-col gap-1">
          <Button size="sm" variant={state === "followed" ? "default" : "outline"} onClick={() => record("followed")}>
            Followed
          </Button>
          {allowsPartial ? (
            <Button size="sm" variant={state === "partial" ? "default" : "outline"} onClick={() => record("partial")}>
              Partial
            </Button>
          ) : null}
          {/* Only a day the habit has can be missed: AdherenceService refuses any other. */}
          {row ? (
            <Button size="sm" variant={state === "not_followed" ? "default" : "outline"} onClick={() => record("missed")}>
              Missed
            </Button>
          ) : null}
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
