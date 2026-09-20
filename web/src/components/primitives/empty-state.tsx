import { cn } from "@/lib/utils";
import type { LucideIcon } from "lucide-react";

export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  tone = "clear",
  className,
}: {
  icon?: LucideIcon;
  title: string;
  description?: string;
  action?: React.ReactNode;
  tone?: "clear" | "error" | "idle";
  className?: string;
}) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center rounded-xl px-6 py-12 text-center",
        tone === "error" && "bg-destructive/6",
        className,
      )}
    >
      {Icon ? (
        <Icon
          className={cn(
            "mb-3 size-7",
            tone === "error" ? "text-destructive" : "text-muted-foreground/70",
          )}
        />
      ) : null}
      <p className="font-heading text-lg">{title}</p>
      {description ? (
        <p className="mt-1 max-w-sm text-sm text-muted-foreground">{description}</p>
      ) : null}
      {action ? <div className="mt-4">{action}</div> : null}
    </div>
  );
}
