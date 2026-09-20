import { cn } from "@/lib/utils";
import { typeTone, typeLabel } from "@/lib/subject-meta";
import type { SubjectType } from "@/lib/production-ui-types";
import { TypeIcon } from "@/components/primitives/type-icon";

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
      {withGlyph ? <TypeIcon type={type} className="size-3" /> : null}
      {typeLabel(type)}
    </span>
  );
}
