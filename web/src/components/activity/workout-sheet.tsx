import { useEffect, useId, useRef, useState } from "react";
import { CircleCheckIcon, CircleSlashIcon } from "lucide-react";
import { WorkoutIcon } from "@/components/activity/workout-parts";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import {
  NOTE_MAX,
  dayName,
  formatDistance,
  formatDuration,
  formatTime,
  intensityWords,
  readableActivity,
  sourceWords,
  workoutName,
} from "@/lib/activity";
import { useIsDesktop } from "@/lib/media-query";
import type { WorkoutRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

type NoteStatus = "idle" | "saving" | "saved" | "error";

/**
 * A workout's one-line note (ACTIVITY-4): "Add a note", up to 140 characters with a counter.
 * It saves on Enter, when the box loses focus, or when the sheet closes, then says "Saved";
 * Clear removes it. Saves run one after another, so a blur and a Clear can't cross.
 */
function NoteEditor({ workout }: { workout: WorkoutRow }) {
  const writes = useWrites();
  const [text, setText] = useState(workout.note ?? "");
  const [savedNote, setSavedNote] = useState(workout.note ?? "");
  const [status, setStatus] = useState<NoteStatus>("idle");
  const [error, setError] = useState<string | null>(null);
  // The queued saves and the save on closing run outside a render, so they read refs.
  const saved = useRef(workout.note ?? "");
  const latest = useRef(text);
  const chain = useRef<Promise<void>>(Promise.resolve());
  const alive = useRef(true);
  const saveLater = useRef<(next: string) => void>(() => undefined);
  const inputId = useId();
  const countId = useId();
  const statusId = useId();

  useEffect(() => {
    latest.current = text;
  }, [text]);

  function save(next: string) {
    chain.current = chain.current.then(async () => {
      const value = next.trim();
      if (value === saved.current) return;
      if (alive.current) {
        setStatus("saving");
        setError(null);
      }
      try {
        const row = await writes.setWorkoutNote(workout.id, value);
        saved.current = row.note ?? "";
        if (alive.current) {
          setSavedNote(row.note ?? "");
          setText(row.note ?? "");
          setStatus("saved");
        }
      } catch (e) {
        if (alive.current) {
          setError(e instanceof Error ? e.message : String(e));
          setStatus("error");
        }
      }
    });
  }

  useEffect(() => {
    saveLater.current = save;
  });

  // Closing the sheet with Escape or the close button doesn't blur the box: keep what was typed.
  useEffect(() => {
    alive.current = true;
    return () => {
      alive.current = false;
      if (latest.current.trim() !== saved.current) saveLater.current(latest.current);
    };
  }, []);

  return (
    <div className="space-y-1.5">
      <Label htmlFor={inputId}>Note</Label>
      <div className="flex items-center gap-2">
        <Input
          id={inputId}
          value={text}
          placeholder="Add a note"
          maxLength={NOTE_MAX}
          autoComplete="off"
          enterKeyHint="done"
          aria-describedby={`${countId} ${statusId}`}
          aria-invalid={status === "error" || undefined}
          onChange={(e) => {
            setText(e.target.value);
            if (status === "saved" || status === "error") setStatus("idle");
          }}
          onKeyDown={(e) => {
            if (e.key === "Enter") {
              e.preventDefault();
              save(text);
            }
          }}
          onBlur={() => save(text)}
        />
        <Button
          variant="ghost"
          size="sm"
          disabled={!text && !savedNote}
          onClick={() => {
            setText("");
            save("");
          }}
        >
          Clear
        </Button>
      </div>
      <div className="flex justify-between gap-2 text-xs text-muted-foreground">
        <span id={statusId} aria-live="polite">
          {status === "saving" ? "Saving…" : status === "saved" ? "Saved" : ""}
        </span>
        <span id={countId} className="tabular-nums">
          {text.length}/{NOTE_MAX}
          <span className="sr-only"> characters</span>
        </span>
      </div>
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}

function Field({ label, value }: { label: string; value: string | null }) {
  return (
    <div className="flex justify-between gap-4 py-1.5">
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="text-right tabular-nums">{value ?? "—"}</dd>
    </div>
  );
}

function WorkoutDetail({ workout, today }: { workout: WorkoutRow; today: string }) {
  return (
    <>
      <SheetHeader className="pb-2">
        <SheetTitle className="flex items-center gap-2">
          <WorkoutIcon activity={workout.activity} className="size-5 text-primary" />
          {workoutName(workout)}
        </SheetTitle>
        <SheetDescription>
          {dayName(workout.day, today)} · {formatTime(workout.startedAt)} to {formatTime(workout.endedAt)}
        </SheetDescription>
      </SheetHeader>
      <div className="space-y-5 px-4 pb-6">
        <p
          className={cn(
            "flex items-start gap-2 rounded-lg p-3 text-sm",
            workout.counted ? "bg-primary/10" : "bg-muted text-muted-foreground",
          )}
        >
          {workout.counted ? (
            <CircleCheckIcon className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
          ) : (
            <CircleSlashIcon className="mt-0.5 size-4 shrink-0" aria-hidden />
          )}
          {workout.counted ? "Counts as exercise." : `Not counted as exercise: ${workout.reason ?? "Unknown source"}.`}
        </p>

        <NoteEditor workout={workout} />

        <dl className="divide-y text-sm">
          <Field label="Activity" value={readableActivity(workout.activity)} />
          <Field label="Duration" value={formatDuration(workout.seconds)} />
          <Field label="Calories" value={workout.calories != null ? workout.calories.toLocaleString() : null} />
          <Field label="Distance" value={workout.distanceM != null ? formatDistance(workout.distanceM) : null} />
          <Field label="Intensity" value={intensityWords(workout.intensity)} />
          <Field label="Source" value={sourceWords(workout.source)} />
          <Field
            label="Synced"
            value={new Date(workout.syncedAt).toLocaleString(undefined, {
              month: "short",
              day: "numeric",
              hour: "numeric",
              minute: "2-digit",
            })}
          />
        </dl>
        <p className="text-xs text-muted-foreground">Oura's fields are shown as Oura has them; change them in the Oura app.</p>
      </div>
    </>
  );
}

/** A workout in full (activity plan §3.7): every field, the source included, and its note. */
export function WorkoutSheet({
  workout,
  today,
  open,
  onOpenChange,
}: {
  workout: WorkoutRow | null;
  today: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const desktop = useIsDesktop();
  const content = useRef<HTMLDivElement>(null);
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        ref={content}
        side={desktop ? "right" : "bottom"}
        className={cn("gap-0 overflow-y-auto", desktop ? "w-full sm:max-w-md" : "max-h-[92dvh] rounded-t-2xl")}
        // Open on the workout, not in the note box: on a phone that would raise the keyboard.
        onOpenAutoFocus={(e) => {
          e.preventDefault();
          content.current?.focus();
        }}
      >
        {workout ? <WorkoutDetail key={workout.id} workout={workout} today={today} /> : null}
      </SheetContent>
    </Sheet>
  );
}
