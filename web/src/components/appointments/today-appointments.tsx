import { MapPinIcon } from "lucide-react";
import { StatusPill } from "@/components/primitives/status-pill";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { appointmentWhen } from "@/lib/appointments";
import { useAppointments } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { ReadFailed } from "@/components/primitives/read-state";

/**
 * Today's appointments on Focus (CAL-1): in calendar order, each with its time, title and
 * status at a glance. Choosing one opens its detail.
 */
export function TodayAppointments({
  today,
  onOpen,
  className,
}: {
  today: string;
  onOpen: (id: string) => void;
  className?: string;
}) {
  const appointments = useAppointments(today);
  const { data, isLoading, isError } = appointments;

  return (
    <section className={cn("rounded-xl bg-card p-4 ring-1 ring-foreground/10", className)}>
      <h2 className="type-scale-section mb-3 text-muted-foreground">Today's appointments</h2>
      {isLoading ? (
        <ListSkeleton rows={2} />
      ) : isError ? (
        <ReadFailed read={appointments} what="Appointments" />
      ) : !data || data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No appointments today.</p>
      ) : (
        <ul className="space-y-1">
          {data.map((a) => (
            <li key={a.id}>
              <button
                type="button"
                onClick={() => onOpen(a.id)}
                className="flex min-h-11 w-full items-center gap-3 rounded-lg px-2 py-1.5 text-left transition-colors ease-hearth hover:bg-muted/60"
              >
                <span className="w-36 shrink-0 text-sm tabular-nums text-muted-foreground">
                  {appointmentWhen(a) || "No time set"}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate text-sm font-medium">{a.title}</span>
                  {a.location ? (
                    <span className="flex items-center gap-1 truncate text-[0.6875rem] text-muted-foreground">
                      <MapPinIcon className="size-3 shrink-0" aria-hidden />
                      {a.location}
                    </span>
                  ) : null}
                </span>
                <StatusPill status={a.status} className="shrink-0" />
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
