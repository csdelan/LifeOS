import type { LucideIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/** A read as a view holds it: a TanStack Query result has all of this. */
export interface Read {
  isError: boolean;
  isFetching?: boolean;
  refetch: () => unknown;
}

/**
 * A read that failed, said one way everywhere (code review CR85): what failed to load and a Retry
 * that reads it again, so a failed read is never left for a reload. Nothing shows while the read
 * hasn't failed, so a view puts it where the failure belongs. Given several reads, it shows when
 * any failed and Retry reads each that did.
 *
 * `page` is a view's whole body failing, with the record it isn't (`notEmpty`): "This is not an
 * empty list." `inline` is one card's or section's line.
 */
export function ReadFailed({
  read,
  what,
  notEmpty,
  detail,
  size = "inline",
  icon,
  className,
}: {
  read: Read | readonly Read[];
  /** What failed, as the title says it: "The steps" → "The steps failed to load." */
  what: string;
  /** For a page: what an empty answer would have been, "an empty list". */
  notEmpty?: string;
  /** What went wrong, when the view has words for it (an agent's or Supabase's answer). */
  detail?: string;
  size?: "page" | "inline";
  icon?: LucideIcon;
  className?: string;
}) {
  const reads: readonly Read[] = Array.isArray(read) ? read : [read as Read];
  const failed = reads.filter((r) => r.isError);
  if (failed.length === 0) return null;
  const retrying = failed.some((r) => r.isFetching);
  const retry = () => {
    for (const r of failed) void r.refetch();
  };
  const button = (
    <Button
      variant={size === "page" ? "outline" : "link"}
      size="sm"
      className={size === "page" ? undefined : "h-auto p-0 text-sm"}
      disabled={retrying}
      onClick={retry}
    >
      {retrying ? "Retrying…" : "Retry"}
    </Button>
  );

  if (size === "page") {
    return (
      <EmptyState
        tone="error"
        icon={icon}
        title={`${what} failed to load`}
        description={detail ?? (notEmpty ? `This is not ${notEmpty}: the read didn't come back.` : "The read didn't come back.")}
        action={button}
        className={className}
      />
    );
  }
  return (
    <p role="alert" className={cn("flex flex-wrap items-baseline gap-x-2 text-sm text-destructive", className)}>
      <span>{detail ? `${what} failed to load: ${detail}.` : `${what} failed to load.`}</span>
      {button}
    </p>
  );
}
