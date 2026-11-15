import { useId, type ComponentType, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * One group on an item's detail: a titled card. Its title is a heading and names the section,
 * so a screen reader (or a test) finds it as a region by that name; `action` sits beside the
 * title, outside it, so a button there doesn't change the name.
 */
export function DetailSection({
  title,
  icon: Icon,
  action,
  className,
  children,
}: {
  title: string;
  icon?: ComponentType<{ className?: string; "aria-hidden"?: boolean }>;
  action?: ReactNode;
  className?: string;
  children: ReactNode;
}) {
  const id = useId();
  return (
    <section
      aria-labelledby={id}
      className={cn("min-w-0 rounded-xl bg-muted/40 px-3.5 pt-2.5 pb-3 ring-1 ring-foreground/8", className)}
    >
      <div className="mb-2 flex min-h-7 flex-wrap items-center justify-between gap-x-2 gap-y-1">
        <h3 id={id} className="flex items-center gap-2 text-sm font-semibold text-foreground">
          {Icon ? <Icon className="size-4 text-primary/80" aria-hidden /> : null}
          {title}
        </h3>
        {action ? <div className="ml-auto flex flex-wrap items-center gap-1.5">{action}</div> : null}
      </div>
      <div className="space-y-3">{children}</div>
    </section>
  );
}

/** Rows of label and value, a hairline between each. */
export function PropertyList({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={cn("divide-y divide-foreground/8", className)}>{children}</div>;
}

/**
 * A label and its value. The label takes a column of its own once the detail is wide enough
 * (it sits in an `@container`), above the value when narrower, as on a phone. A `wide` value,
 * such as the repeat pattern's editor, keeps its label above it until there's room for both.
 */
export function PropertyRow({
  label,
  required,
  hint,
  error,
  wide = false,
  className,
  children,
}: {
  label: ReactNode;
  required?: boolean;
  hint?: ReactNode;
  error?: ReactNode;
  wide?: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <div
      className={cn(
        "grid gap-x-4 gap-y-1 py-2 first:pt-0.5 last:pb-0.5",
        wide
          ? "@lg:grid-cols-[9rem_minmax(0,1fr)] @lg:items-baseline"
          : "@sm:grid-cols-[9rem_minmax(0,1fr)] @sm:items-baseline",
        className,
      )}
    >
      <div className="text-[0.8125rem] leading-5 text-muted-foreground">
        {label}
        {required ? <span className="text-destructive"> *</span> : null}
      </div>
      <div className="min-w-0 space-y-1 text-sm leading-5">
        {children}
        {hint ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
        {error ? <p className="text-xs text-destructive">{error}</p> : null}
      </div>
    </div>
  );
}

/** A value with nothing in it, said quietly so the ones that are set stand out. */
export function EmptyValue({ children = "—" }: { children?: ReactNode }) {
  return <span className="text-muted-foreground/80">{children}</span>;
}
