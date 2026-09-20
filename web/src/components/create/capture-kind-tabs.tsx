import { CAPTURE_KINDS, type CaptureKind } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

export function CaptureKindTabs({
  value,
  onChange,
  disabled,
}: {
  value: CaptureKind;
  onChange: (kind: CaptureKind) => void;
  disabled?: boolean;
}) {
  return (
    <div className="flex gap-1" role="tablist" aria-label="Capture type">
      {CAPTURE_KINDS.map((kind) => (
        <button
          key={kind}
          type="button"
          role="tab"
          aria-selected={value === kind}
          tabIndex={0}
          disabled={disabled}
          onClick={() => onChange(kind)}
          className={cn(
            "min-h-11 rounded-md px-2.5 py-1 text-sm md:min-h-0",
            value === kind
              ? "bg-primary/12 font-medium text-primary"
              : "text-muted-foreground hover:bg-muted",
            disabled && "opacity-50",
          )}
        >
          {kind}
        </button>
      ))}
    </div>
  );
}
