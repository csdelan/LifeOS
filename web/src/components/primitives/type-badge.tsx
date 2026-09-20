import { cn } from "@/lib/utils";
import { typeTone, TYPE_GLYPH, typeLabel } from "@/lib/subject-meta";
import type { SubjectType } from "@/lib/production-ui-types";

export function TypeBadge({
  type,
  withGlyph = true,
  className,
}: {
  type: SubjectType;
  withGlyph?: boolean;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 text-[0.6875rem] font-medium tracking-wide",
        typeTone(type),
        className,
      )}
    >
      {withGlyph ? <span aria-hidden>{TYPE_GLYPH[type]}</span> : null}
      {typeLabel(type)}
    </span>
  );
}
