import { Link } from "@tanstack/react-router";
import { DateChip } from "@/components/primitives/date-chip";
import type { DashboardRead } from "@/lib/reads";

/**
 * The Primary focus's milestones: its Active sub-goals, nearest target date first (focus plan
 * G2). Each opens on the Map, where its own Projects and Tasks hang under it. Nothing shows while
 * the focus has none.
 */
export function FocusMilestones({ milestones }: { milestones: DashboardRead["milestones"] }) {
  if (milestones.length === 0) return null;
  return (
    <div>
      <h2 className="type-scale-section mb-2 text-muted-foreground">Milestones</h2>
      <ul className="space-y-1">
        {milestones.map((m) => (
          <li key={m.id} className="flex items-center justify-between gap-3 text-sm">
            <Link
              to="/map"
              search={{ selected: m.id }}
              className="flex min-h-11 items-center text-left hover:underline md:min-h-8"
            >
              {m.title}
            </Link>
            <DateChip date={m.targetDate} kind="target" />
          </li>
        ))}
      </ul>
    </div>
  );
}
