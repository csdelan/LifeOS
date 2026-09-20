import { Link, useNavigate } from "@tanstack/react-router";
import { CheckIcon, CircleDashedIcon, MinusIcon } from "lucide-react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListRow } from "@/components/primitives/list-row";
import { CardSkeleton, ListSkeleton } from "@/components/primitives/skeletons";
import { useDashboard, useWrites } from "@/lib/queries";
import { todayIso } from "@/lib/dates";
import type { AdherenceState } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

export function FocusPage() {
  const { data, isLoading, isError } = useDashboard();
  const writes = useWrites();
  const navigate = useNavigate();

  if (isLoading) {
    return (
      <div className="h-full overflow-y-auto px-8 py-8">
        <div className="mx-auto grid max-w-5xl gap-4 md:grid-cols-2">
          <CardSkeleton />
          <CardSkeleton />
          <CardSkeleton />
          <CardSkeleton />
        </div>
      </div>
    );
  }

  if (isError || !data) {
    return (
      <EmptyState
        tone="error"
        title="Focus failed to load"
        description="This is not a clear day — the composed read never returned."
      />
    );
  }

  const today = todayIso();
  const overdue = data.dueAndOverdue.filter((t) => t.due && t.due < today);
  const dueToday = data.dueAndOverdue.filter((t) => !overdue.includes(t));

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-5xl px-8 py-8">
        <p className="type-scale-section text-primary">Today</p>
        <h1 className="type-scale-display mt-1">What deserves attention</h1>
        <p className="mt-2 max-w-xl text-muted-foreground">
          A command center, not a second brain. Counts stay quiet so the plan can speak.
        </p>

        <div className="mt-8 grid gap-4 lg:grid-cols-[1.4fr_1fr]">
          <Card className="shadow-(--shadow-lift)">
            <CardHeader>
              <p className="type-scale-section text-attention">Primary focus</p>
              <CardTitle className="font-heading text-3xl">
                {data.primaryFocus.title}
              </CardTitle>
              <CardDescription>
                Manually set. Suggestions never silently replace this.
              </CardDescription>
            </CardHeader>
            <CardContent>
              <ol className="space-y-2">
                {data.objectives.map((o, i) => (
                  <li key={o.id} className="flex items-start gap-3 text-sm">
                    <span className="mt-0.5 flex size-5 items-center justify-center rounded-full bg-primary/10 text-[0.6875rem] font-medium text-primary">
                      {i + 1}
                    </span>
                    {o.title}
                  </li>
                ))}
              </ol>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <p className="type-scale-section text-muted-foreground">Inbox</p>
              <CardTitle className="font-heading text-4xl tabular-nums">
                {data.inboxCount}
              </CardTitle>
              <CardDescription>
                {data.inboxCount === 0
                  ? "Genuinely clear — nothing needs a decision."
                  : "Items waiting on a triage decision."}
              </CardDescription>
            </CardHeader>
            <CardContent>
              <Button asChild variant="outline" size="sm">
                <Link to="/inbox">Open Inbox</Link>
              </Button>
            </CardContent>
          </Card>
        </div>

        <div className="mt-4 grid gap-4 lg:grid-cols-2">
          <section className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            <div className="mb-3 flex items-center justify-between">
              <h2 className="type-scale-section text-attention">Due & overdue</h2>
              <Link to="/map" search={{ lens: "Task" }} className="text-xs text-muted-foreground hover:text-foreground">
                Open in Map
              </Link>
            </div>
            {overdue.length === 0 && dueToday.length === 0 ? (
              <EmptyState
                title="Nothing overdue"
                description="No Task is due today or late. Genuinely clear."
                className="py-8"
              />
            ) : (
              <div className="space-y-1">
                {overdue.map((t) => (
                  <ListRow
                    key={t.id}
                    item={t}
                    onSelect={() =>
                      void navigate({ to: "/map", search: { selected: t.id } })
                    }
                  />
                ))}
                {dueToday.map((t) => (
                  <ListRow
                    key={t.id}
                    item={t}
                    onSelect={() =>
                      void navigate({ to: "/map", search: { selected: t.id } })
                    }
                  />
                ))}
              </div>
            )}
          </section>

          <section className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            <h2 className="type-scale-section mb-3 text-muted-foreground">Today's habits</h2>
            <ul className="space-y-2">
              {data.todayHabits.map((h) => (
                <li
                  key={h.habitId}
                  className="flex items-center justify-between rounded-lg px-2 py-1.5"
                >
                  <div>
                    <p className="text-sm font-medium">{h.habitName}</p>
                    <p className="text-[0.6875rem] text-muted-foreground">
                      {h.state === "unrecorded" ? "Unrecorded" : h.state.replace("_", " ")}
                    </p>
                  </div>
                  <AdherenceButtons
                    state={h.state}
                    allowsPartial={h.allowsPartial}
                    onPick={(s) =>
                      void writes.adhere(
                        h.habitId,
                        s === "not_followed" ? "missed" : s,
                      )
                    }
                  />
                </li>
              ))}
            </ul>
          </section>
        </div>

        <div className="mt-4 grid gap-4 lg:grid-cols-2">
          <section className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            <h2 className="type-scale-section mb-3 text-muted-foreground">Active goals</h2>
            {data.activeGoals.length === 0 ? (
              <EmptyState title="No active goals" className="py-6" />
            ) : (
              data.activeGoals.map((g) => (
                <ListRow
                  key={g.id}
                  item={g}
                  onSelect={() =>
                    void navigate({ to: "/map", search: { selected: g.id } })
                  }
                />
              ))
            )}
          </section>
          <section className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            <h2 className="type-scale-section mb-3 text-muted-foreground">Active projects</h2>
            {data.activeProjects.map((p) => (
              <ListRow
                key={p.id}
                item={p}
                onSelect={() =>
                  void navigate({ to: "/map", search: { selected: p.id } })
                }
              />
            ))}
          </section>
        </div>

        {data.nextActions.length > 0 ? (
          <section className="mt-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
            <h2 className="type-scale-section mb-3 text-muted-foreground">In progress</h2>
            {isLoading ? <ListSkeleton rows={3} /> : data.nextActions.map((t) => (
              <ListRow
                key={t.id}
                item={t}
                onSelect={() =>
                  void navigate({ to: "/map", search: { selected: t.id } })
                }
              />
            ))}
          </section>
        ) : null}
      </div>
    </div>
  );
}

function AdherenceButtons({
  state,
  allowsPartial,
  onPick,
}: {
  state: AdherenceState;
  allowsPartial: boolean;
  onPick: (s: "followed" | "partial" | "not_followed") => void;
}) {
  return (
    <div className="flex gap-1">
      <HabitBtn
        label="Followed"
        active={state === "followed"}
        onClick={() => onPick("followed")}
      >
        <CheckIcon className="size-3.5" />
      </HabitBtn>
      {allowsPartial ? (
        <HabitBtn
          label="Partial"
          active={state === "partial"}
          onClick={() => onPick("partial")}
        >
          <MinusIcon className="size-3.5" />
        </HabitBtn>
      ) : null}
      <HabitBtn
        label="Not followed"
        active={state === "not_followed"}
        onClick={() => onPick("not_followed")}
      >
        <CircleDashedIcon className="size-3.5" />
      </HabitBtn>
    </div>
  );
}

function HabitBtn({
  children,
  label,
  active,
  onClick,
}: {
  children: React.ReactNode;
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-label={label}
      onClick={onClick}
      className={cn(
        "flex size-7 items-center justify-center rounded-md border text-muted-foreground transition-colors",
        active
          ? "border-primary bg-primary/12 text-primary"
          : "border-transparent hover:bg-muted",
      )}
    >
      {children}
    </button>
  );
}
