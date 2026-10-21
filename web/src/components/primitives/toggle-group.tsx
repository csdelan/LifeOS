import { cn } from "@/lib/utils";

/**
 * A small segmented control: one pressed button among a few, e.g. Chart / Table or a
 * range. Each option is a toggle button (`aria-pressed`), grouped under `label`.
 */
export function ToggleGroup<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: { id: T; label: string; symbol?: string }[];
  onChange: (id: T) => void;
}) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-xs text-muted-foreground" aria-hidden>
        {label}
      </span>
      <div role="group" aria-label={label} className="flex flex-wrap rounded-lg bg-muted p-0.5">
        {options.map((o) => (
          <button
            key={o.id}
            type="button"
            aria-pressed={value === o.id}
            onClick={() => onChange(o.id)}
            className={cn(
              "min-h-11 rounded-md px-2.5 py-1 text-xs font-medium focus-visible:ring-2 focus-visible:ring-ring/50 md:min-h-0",
              value === o.id ? "bg-background shadow-sm" : "text-muted-foreground",
            )}
          >
            {o.symbol ? <span aria-hidden>{o.symbol} </span> : null}
            {o.label}
          </button>
        ))}
      </div>
    </div>
  );
}
