import { cn } from "@/lib/utils";
import { formatDay, relativeDayLabel, todayIso } from "@/lib/dates";

export function DateChip({
  date,
  kind = "due",
  closed = false,
  className,
}: {
  date?: string | null;
  kind?: "due" | "scheduled" | "target" | "plain";
  /** The item is finished (Completed, Missed, Cancelled…): its date isn't overdue or today, just a day. */
  closed?: boolean;
  className?: string;
}) {
  if (!date) return null;
  const today = todayIso();
  const overdue = !closed && kind === "due" && date < today;
  const isToday = !closed && date === today;
  const when = closed ? formatDay(date) : relativeDayLabel(date, today);
  const label =
    kind === "scheduled"
      ? `Do ${when}`
      : kind === "target"
        ? `Target ${when}`
        : kind === "plain"
          ? formatDay(date)
          : when;

  return (
    <span
      className={cn(
        "inline-flex h-5 items-center rounded-full px-2 text-[0.6875rem] font-medium tabular-nums",
        overdue && "bg-destructive/12 text-destructive",
        !overdue && isToday && "bg-attention/12 text-attention",
        !overdue && !isToday && "bg-muted text-muted-foreground",
        className,
      )}
    >
      {label}
    </span>
  );
}
