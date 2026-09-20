import { cn } from "@/lib/utils";
import { statusTone } from "@/lib/subject-meta";

export function StatusPill({
  status,
  className,
}: {
  status?: string | null;
  className?: string;
}) {
  if (!status) return null;
  const tone = statusTone(status);
  return (
    <span
      className={cn(
        "inline-flex h-5 items-center rounded-full px-2 text-[0.6875rem] font-medium",
        tone === "active" && "bg-primary/12 text-primary",
        tone === "attention" && "bg-attention/12 text-attention",
        tone === "terminal" && "bg-muted text-muted-foreground",
        tone === "neutral" && "bg-secondary text-secondary-foreground",
        className,
      )}
    >
      {status}
    </span>
  );
}
