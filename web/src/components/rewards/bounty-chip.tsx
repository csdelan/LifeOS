import type { Amounts } from "@/lib/production-ui-types";
import { currency, formatAmounts, orderedAmounts } from "@/lib/rewards";
import { cn } from "@/lib/utils";

/**
 * What an item pays, compact: "🪙2 ⏳10 ⭐10". Screen readers hear the words ("Pays 2 Coins ·
 * 10 Freedom · 10 XP"). Renders nothing when it pays nothing.
 */
export function BountyChip({
  amounts,
  prefix = "Pays",
  suffix,
  className,
}: {
  amounts?: Amounts | null;
  prefix?: string;
  /** e.g. "a day", read after the amounts. */
  suffix?: string;
  className?: string;
}) {
  const parts = amounts ? orderedAmounts(amounts) : [];
  if (!amounts || parts.length === 0) return null;
  const words = [prefix, formatAmounts(amounts), suffix].filter(Boolean).join(" ");
  return (
    <span
      title={words}
      className={cn(
        "inline-flex shrink-0 items-center gap-1.5 rounded-full bg-[var(--type-reward)]/12 px-2 py-0.5 text-[0.6875rem] tabular-nums text-foreground",
        className,
      )}
    >
      <span className="sr-only">{words}</span>
      <span aria-hidden className="inline-flex items-center gap-1.5">
        {parts.map(([key, n]) => (
          <span key={key}>
            {currency(key)?.symbol ?? key}
            {n.toLocaleString()}
          </span>
        ))}
        {suffix ? <span className="text-muted-foreground">/{suffix.replace(/^an? /, "")}</span> : null}
      </span>
    </span>
  );
}
