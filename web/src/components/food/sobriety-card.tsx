import { formatDay } from "@/lib/dates";
import type { SobrietyRow } from "@/lib/production-ui-types";
import { sobrietyDetail, sobrietyHeadline } from "@/lib/sobriety";

/**
 * The sobriety streak as of the day on screen (FOOD-6, food-log plan §7.3): drink-free days in
 * a row, counted from the day after the last drink or from the Food Log's first day, and the
 * best run. Nothing shows before the Food Log's first day.
 */
export function SobrietyCard({ sobriety, today }: { sobriety: SobrietyRow | null; today: string }) {
  if (!sobriety) return null;
  return (
    <section aria-labelledby="sobriety-title" className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <h2 id="sobriety-title" className="type-scale-section text-muted-foreground">
        Sobriety streak{sobriety.asOf === today ? "" : `, as of ${formatDay(sobriety.asOf)}`}
      </h2>
      <p className="mt-1 font-heading text-2xl">{sobrietyHeadline(sobriety, today)}</p>
      <p className="text-xs text-muted-foreground">{sobrietyDetail(sobriety)}</p>
      <p className="mt-1 text-[0.6875rem] text-muted-foreground">
        Since {formatDay(sobriety.firstDay)}, when the Food Log began; a day with no drink logged counts as drink-free.
      </p>
    </section>
  );
}
