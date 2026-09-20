import { useNavigate, useSearch } from "@tanstack/react-router";
import { RepeatIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { AdherenceButtons } from "@/components/habits/adherence-buttons";
import { useDirty } from "@/components/shell/app-shell";
import { PeekPanel } from "@/components/shell/panels";
import { HABIT_STATE_ROW } from "@/components/habits/habit-state-styles";
import { useDashboard, useHabits, useWrites } from "@/lib/queries";
import { todayIso } from "@/lib/dates";
import { cn } from "@/lib/utils";

export function HabitsPage() {
  const search = useSearch({ from: "/habits" });
  const navigate = useNavigate({ from: "/habits" });
  const selected = search.selected;
  const { data, isLoading, isError } = useHabits();
  const { data: dash } = useDashboard();
  const writes = useWrites();
  const { setDirty, requestNavigation } = useDirty();
  const today = todayIso();
  const todayById = new Map((dash?.todayHabits ?? []).map((h) => [h.habitId, h]));

  const habits = (data ?? []).filter((h) => !h.archived);

  return (
    <div className="flex h-full min-h-0">
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="shrink-0 border-b px-4 py-4 md:px-6">
          <p className="type-scale-section text-primary">Habits</p>
          <h1 className="font-heading text-2xl">Cue, routine, streak</h1>
          <p className="mt-1 text-xs text-muted-foreground">
            One-click record. Correcting a past day recomputes the streak. Partial is distinct
            from missed.
          </p>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">
          {isLoading ? <ListSkeleton rows={6} /> : null}
          {isError ? (
            <EmptyState
              tone="error"
              icon={RepeatIcon}
              title="Habits failed to load"
              description="This is not an empty list — the reader never returned."
            />
          ) : null}
          {habits.length === 0 && !isLoading ? (
            <EmptyState
              icon={RepeatIcon}
              title="No habits yet"
              description="Create a Habit from New and relate it to a Goal or Identity Statement."
            />
          ) : null}
          <ul className="space-y-2">
            {habits.map((h) => {
              const todayRow = todayById.get(h.id);
              return (
                <li key={h.id}>
                  <div
                    className={cn(
                      "flex items-center justify-between gap-3 rounded-xl px-3 py-2.5 ring-1 ring-foreground/10",
                      todayRow ? HABIT_STATE_ROW[todayRow.state] : null,
                      selected === h.id && "bg-primary/8 ring-primary/20",
                    )}
                  >
                    <button
                      type="button"
                      className="min-h-11 min-w-0 flex-1 text-left"
                      onClick={() =>
                        requestNavigation(() =>
                          void navigate({ search: { selected: h.id } }),
                        )
                      }
                    >
                      <p className="truncate text-sm font-medium">{h.name}</p>
                      <p className="text-[0.6875rem] text-muted-foreground">
                        Streak {h.currentStreak}
                        {h.lastState ? ` · ${h.lastState.replace("_", " ")}` : ""}
                        {h.cue ? ` · Cue: ${h.cue}` : ""}
                      </p>
                    </button>
                    {todayRow ? (
                      <AdherenceButtons
                        state={todayRow.state}
                        allowsPartial={todayRow.allowsPartial}
                        onPick={(s) =>
                          void writes.adhere(h.id, s === "not_followed" ? "missed" : s, {
                            on: today,
                          })
                        }
                      />
                    ) : null}
                  </div>
                </li>
              );
            })}
          </ul>
        </div>
      </div>
      <PeekPanel
        open={!!selected}
        title="Habit detail"
        onClose={() =>
          requestNavigation(() =>
            void navigate({ search: { selected: undefined } }),
          )
        }
      >
        {selected ? (
          <SubjectDetail
            subjectId={selected}
            onDirtyChange={setDirty}
            onClose={() =>
              requestNavigation(() =>
                void navigate({ search: { selected: undefined } }),
              )
            }
          />
        ) : null}
      </PeekPanel>
    </div>
  );
}
