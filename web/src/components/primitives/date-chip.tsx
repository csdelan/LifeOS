import { cn } from "@/lib/utils";
import { formatDay, relativeDayLabel, todayIso } from "@/lib/dates";

export function DateChip({
  date,
  kind = "due",
  className,
}: {
  date?: string | null;
  kind?: "due" | "scheduled" | "target" | "plain";
  className?: string;
}) {
  if (!date) return null;
  const today = todayIso();
  const overdue = kind === "due" && date < today;
  const isToday = date === today;
  const label =
    kind === "scheduled"
      ? `Do ${relativeDayLabel(date, today)}`
      : kind === "target"
        ? `Target ${relativeDayLabel(date, today)}`
        : kind === "plain"
          ? formatDay(date)
          : relativeDayLabel(date, today);

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
