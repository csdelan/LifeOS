import { Link, useNavigate } from "@tanstack/react-router";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListRow } from "@/components/primitives/list-row";
import { CardSkeleton, ListSkeleton } from "@/components/primitives/skeletons";
import { AdherenceButtons } from "@/components/habits/adherence-buttons";
import { HABIT_STATE_ROW } from "@/components/habits/habit-state-styles";
import { cn } from "@/lib/utils";
import { useDashboard, useWrites } from "@/lib/queries";
import { todayIso } from "@/lib/dates";

export function FocusPage() {
  const { data, isLoading, isError } = useDashboard();
  const writes = useWrites();
  const navigate = useNavigate();

  if (isLoading) {
    return (
      <div className="h-full overflow-y-auto px-4 py-8 md:px-8">
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
      <div className="mx-auto max-w-5xl px-4 py-8 md:px-8">
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
              <Link to="/tasks" className="text-xs text-muted-foreground hover:text-foreground">
                Open Tasks
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
            <div className="mb-3 flex items-center justify-between">
              <h2 className="type-scale-section text-muted-foreground">Today's habits</h2>
              <Link to="/habits" className="text-xs text-muted-foreground hover:text-foreground">
                Open Habits
              </Link>
            </div>
            <ul className="space-y-2">
              {data.todayHabits.map((h) => (
                <li
                  key={h.habitId}
                  className={cn(
                    "flex items-center justify-between rounded-lg px-2 py-1.5",
                    HABIT_STATE_ROW[h.state],
                  )}
                >
                  <button
                    type="button"
                    className="min-h-11 text-left"
                    onClick={() =>
                      void navigate({ to: "/habits", search: { selected: h.habitId } })
                    }
                  >
                    <p className="text-sm font-medium">{h.habitName}</p>
                    <p className="text-[0.6875rem] text-muted-foreground">
                      {h.state === "unrecorded" ? "Unrecorded" : h.state.replace("_", " ")}
                    </p>
                  </button>
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
            {data.activeProjects.length === 0 ? (
              <EmptyState title="No active projects" className="py-6" />
            ) : (
              data.activeProjects.map((p) => (
                <ListRow
                  key={p.id}
                  item={p}
                  onSelect={() =>
                    void navigate({ to: "/map", search: { selected: p.id } })
                  }
                />
              ))
            )}
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
