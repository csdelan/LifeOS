import { useId, useState } from "react";
import { CalendarOffIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { addDays, formatDayFull } from "@/lib/dates";
import { useExceptionDays, useWrites } from "@/lib/queries";

/** The longest comment, as the API takes it. */
export const EXCEPTION_COMMENT_MAX = 140;

/**
 * Exception days (REWARD-8, xp-domains plan §7.4.9): a day Chris was sick, injured or
 * travelling, marked with a comment, neither helps nor hurts his domains. The last 90 days'
 * marks are listed, each with Unmark; marking or unmarking answers what it moved, which toasts.
 */
export function ExceptionDays({ today }: { today: string }) {
  const headingId = useId();
  const list = useExceptionDays(90);
  const writes = useWrites();
  const [open, setOpen] = useState(false);
  const [unmarking, setUnmarking] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const marked = list.data?.exceptions ?? [];

  async function unmark(day: string) {
    setError(null);
    setUnmarking(day);
    try {
      await writes.unmarkException(day);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unmarking failed. Try again.");
    } finally {
      setUnmarking(null);
    }
  }

  return (
    <section aria-labelledby={headingId} className="rounded-xl bg-card ring-1 ring-foreground/10">
      <div className="flex flex-wrap items-center justify-between gap-2 px-4 pt-4">
        <h2 id={headingId} className="font-heading text-lg">
          Exception days
        </h2>
        <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
          <CalendarOffIcon aria-hidden />
          Mark a day as an exception
        </Button>
      </div>
      <p className="px-4 pt-1 text-xs text-muted-foreground">
        A day you were sick, injured or travelling doesn't count for or against your domains. Its XP still counts
        toward your lifetime level.
      </p>
      <div className="px-4 pt-3 pb-4">
        {list.isError ? (
          <p className="text-sm text-destructive">Your exception days failed to load.</p>
        ) : marked.length === 0 ? (
          <p className="text-sm text-muted-foreground">{list.isLoading ? "Loading…" : "None in the last 90 days."}</p>
        ) : (
          <ul aria-label="Exception days, last 90 days" className="divide-y rounded-lg ring-1 ring-foreground/10">
            {marked.map((e) => (
              <li key={e.day} className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 px-3 py-2 text-sm">
                <span className="min-w-0">
                  <span className="font-medium">{formatDayFull(e.day)}</span>
                  <span className="text-muted-foreground"> · {e.comment}</span>
                </span>
                <Button
                  variant="ghost"
                  size="sm"
                  disabled={unmarking === e.day}
                  aria-label={`Unmark ${formatDayFull(e.day)}`}
                  onClick={() => void unmark(e.day)}
                >
                  Unmark
                </Button>
              </li>
            ))}
          </ul>
        )}
        {error ? (
          <p role="alert" className="mt-2 text-sm text-destructive">
            {error}
          </p>
        ) : null}
      </div>
      <MarkExceptionDialog open={open} onOpenChange={setOpen} today={today} />
    </section>
  );
}

type Which = "today" | "yesterday" | "earlier";

/** The dialog: today, yesterday or an earlier day, and a required one-line comment. */
export function MarkExceptionDialog({
  open,
  onOpenChange,
  today,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  today: string;
}) {
  const writes = useWrites();
  const commentId = useId();
  const dateId = useId();
  const countId = useId();
  const [which, setWhich] = useState<Which>("today");
  const [earlier, setEarlier] = useState(() => addDays(today, -2));
  const [comment, setComment] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const yesterday = addDays(today, -1);

  function reset() {
    setWhich("today");
    setEarlier(addDays(today, -2));
    setComment("");
    setError(null);
  }

  async function submit() {
    const text = comment.trim();
    if (!text) {
      setError("Say why the day is an exception: sick, injured, travelling…");
      return;
    }
    const day = which === "today" ? today : which === "yesterday" ? yesterday : earlier;
    if (!day || day > today) {
      setError("Pick today or an earlier day.");
      return;
    }
    setError(null);
    setPending(true);
    try {
      await writes.markException(text, day);
      reset();
      onOpenChange(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Marking failed. Try again.");
    } finally {
      setPending(false);
    }
  }

  const options: { value: Which; label: string }[] = [
    { value: "today", label: "Today" },
    { value: "yesterday", label: "Yesterday" },
    { value: "earlier", label: "An earlier day" },
  ];

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset();
        onOpenChange(next);
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Mark a day as an exception</DialogTitle>
          <DialogDescription>
            It won't count for or against your domains, and a falling level waits. You can unmark it later.
          </DialogDescription>
        </DialogHeader>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            void submit();
          }}
        >
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium">Day</legend>
            <div className="flex flex-wrap gap-x-4 gap-y-2">
              {options.map((o) => (
                <label key={o.value} className="flex items-center gap-1.5 text-sm">
                  <input
                    type="radio"
                    name="exception-day"
                    value={o.value}
                    checked={which === o.value}
                    onChange={() => setWhich(o.value)}
                    className="accent-primary"
                  />
                  {o.label}
                </label>
              ))}
            </div>
            {which === "earlier" ? (
              <div className="space-y-1">
                <Label htmlFor={dateId}>Date</Label>
                <Input
                  id={dateId}
                  type="date"
                  max={today}
                  value={earlier}
                  required
                  onChange={(e) => setEarlier(e.target.value)}
                  className="w-44"
                />
              </div>
            ) : null}
          </fieldset>
          <div className="space-y-1">
            <Label htmlFor={commentId}>Why</Label>
            <Input
              id={commentId}
              value={comment}
              maxLength={EXCEPTION_COMMENT_MAX}
              placeholder="Sick with a cold"
              required
              aria-describedby={countId}
              onChange={(e) => setComment(e.target.value.replace(/[\r\n]+/g, " "))}
            />
            <p id={countId} className="text-right text-[0.6875rem] text-muted-foreground">
              {comment.length} of {EXCEPTION_COMMENT_MAX} characters
            </p>
          </div>
          {error ? (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          ) : null}
          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)} disabled={pending}>
              Cancel
            </Button>
            <Button type="submit" disabled={pending}>
              Mark as exception
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
