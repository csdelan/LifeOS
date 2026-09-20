import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { parseRecurrence, serializeRecurrence, WEEKDAYS } from "@/lib/recurrence";
import { cn } from "@/lib/utils";

export function RecurrenceEditor({
  value,
  onChange,
  disabled,
}: {
  value?: string;
  onChange: (next: string) => void;
  disabled?: boolean;
}) {
  const spec = parseRecurrence(value);
  const weekdays = spec.weekdays ?? [];

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-1">
        {(
          [
            ["daily", "Daily"],
            ["weekly", "Weekly"],
            ["interval", "Interval"],
          ] as const
        ).map(([kind, label]) => (
          <button
            key={kind}
            type="button"
            disabled={disabled}
            onClick={() =>
              onChange(
                serializeRecurrence({
                  ...spec,
                  kind,
                  intervalDays: kind === "interval" ? spec.intervalDays ?? 2 : spec.intervalDays,
                  weekdays: kind === "weekly" ? (weekdays.length ? weekdays : [1, 2, 3, 4, 5]) : spec.weekdays,
                }),
              )
            }
            className={cn(
              "rounded-md px-2 py-1 text-xs font-medium",
              spec.kind === kind ? "bg-primary/12 text-primary" : "text-muted-foreground hover:bg-muted",
              disabled && "opacity-60",
            )}
          >
            {label}
          </button>
        ))}
      </div>
      {spec.kind === "weekly" ? (
        <div className="flex flex-wrap gap-1">
          {WEEKDAYS.map((label, i) => {
            const on = weekdays.includes(i);
            return (
              <button
                key={label}
                type="button"
                disabled={disabled}
                aria-pressed={on}
                onClick={() => {
                  const next = on ? weekdays.filter((d) => d !== i) : [...weekdays, i].sort();
                  onChange(serializeRecurrence({ ...spec, kind: "weekly", weekdays: next }));
                }}
                className={cn(
                  "size-8 rounded-md text-[0.6875rem] font-medium",
                  on ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground",
                )}
              >
                {label.slice(0, 1)}
              </button>
            );
          })}
        </div>
      ) : null}
      {spec.kind === "interval" ? (
        <div className="flex items-center gap-2">
          <Label className="text-xs text-muted-foreground">Every</Label>
          <Input
            type="number"
            min={1}
            disabled={disabled}
            className="w-20"
            value={spec.intervalDays ?? 2}
            onChange={(e) =>
              onChange(
                serializeRecurrence({
                  ...spec,
                  kind: "interval",
                  intervalDays: Math.max(1, Number(e.target.value) || 1),
                }),
              )
            }
          />
          <span className="text-xs text-muted-foreground">days</span>
        </div>
      ) : null}
    </div>
  );
}
