import { XIcon } from "lucide-react";
import { cn } from "@/lib/utils";

export function Tag({
  children,
  onRemove,
  className,
}: {
  children: React.ReactNode;
  onRemove?: () => void;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex h-6 items-center gap-1 rounded-full border border-border bg-card px-2 text-[0.75rem] text-foreground/80",
        className,
      )}
    >
      <span className="text-muted-foreground">#</span>
      {children}
      {onRemove ? (
        <button
          type="button"
          onClick={onRemove}
          className="rounded-full p-1.5 text-muted-foreground hover:bg-muted hover:text-foreground"
          aria-label="Remove tag"
        >
          <XIcon className="size-3" />
        </button>
      ) : null}
    </span>
  );
}
