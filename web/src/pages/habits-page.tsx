import { useNavigate, useSearch } from "@tanstack/react-router";
import { PlayIcon, RepeatIcon } from "lucide-react";
import { CoverThumb } from "@/components/cover/cover-thumb";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { AdherenceButtons } from "@/components/habits/adherence-buttons";
import { MaintainButtons, type MaintainPick } from "@/components/habits/maintain-buttons";
import { ReadyToEstablish } from "@/components/habits/establish-dialog";
import { BountyChip } from "@/components/rewards/bounty-chip";
import { useDirty } from "@/components/shell/app-shell";
import { PeekPanel } from "@/components/shell/panels";
import { HABIT_STATE_ROW } from "@/components/habits/habit-state-styles";
import { useDashboard, useHabits, useSubjects, useWrites } from "@/lib/queries";
import { todayIso } from "@/lib/dates";
import { habitStage, isMaintainedToday, isReadyToEstablish, maintainedDayLabel } from "@/lib/establish";
import type { CoverRead, HabitOccurrenceRow, HabitRow } from "@/lib/production-ui-types";
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
  const maintainedById = new Map((dash?.maintainingHabits ?? []).map((h) => [h.habitId, h]));
  // The habits read has no covers; the subject list does (COVER-3), so each row takes its
  // cover from there by id. A habit with no cover, or a list still loading, shows none.
  const { data: habitSubjects } = useSubjects("Habit");
  const coverById = new Map(
    (habitSubjects ?? []).flatMap((s) => (s.cover ? [[s.id, s.cover] as const] : [])),
  );

  const habits = (data ?? []).filter((h) => !h.archived);
  // Planned habits aren't tracked yet. Graduated habits leave daily tracking (REWARD-2) but
  // keep earning for logged days; abandoned ones stop.
  const planned = habits.filter((h) => habitStage(h) === "planned");
  const building = habits.filter((h) => habitStage(h) === "building");
  const established = habits.filter((h) => habitStage(h) === "established");
  const abandoned = habits.filter((h) => habitStage(h) === "abandoned");

  const select = (id: string) =>
    requestNavigation(() => void navigate({ search: { selected: id } }));

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
        <div className="min-h-0 flex-1 space-y-6 overflow-y-auto px-4 py-4">
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
          {building.length > 0 ? (
            <ul className="space-y-2" aria-label="Building">
              {building.map((h) => (
                <HabitItem
                  key={h.id}
                  habit={h}
                  cover={coverById.get(h.id)}
                  today={todayById.get(h.id)}
                  selected={selected === h.id}
                  onSelect={() => select(h.id)}
                  onPick={(s) =>
                    void writes.adhere(h.id, s === "not_followed" ? "missed" : s, { on: today })
                  }
                />
              ))}
            </ul>
          ) : null}
          <PlannedSection
            habits={planned}
            covers={coverById}
            selected={selected}
            onSelect={select}
            onStart={(id) => void writes.setStatus(id, "Building")}
          />
          <GraduatedSection
            title="Established"
            hint="Graduated: no daily schedule, so no day is ever missed. Check a day off when you keep it up; each logged day still pays."
            habits={established}
            covers={coverById}
            selected={selected}
            onSelect={select}
            maintain={{
              todayById: maintainedById,
              onPick: (id, pick) => void writes.adhere(id, pick, { on: today }),
            }}
          />
          <GraduatedSection
            title="Abandoned"
            hint="No longer tracked. What their days earned stays earned."
            habits={abandoned}
            covers={coverById}
            selected={selected}
            onSelect={select}
          />
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

/** A habit row's cover thumbnail: 36 px, as on Today's rows (COVER-3). */
const HABIT_THUMB = 36;

/** A habit's name, with its cover thumbnail first when it has one. */
function HabitName({ habit: h, cover, children }: { habit: HabitRow; cover?: CoverRead; children?: React.ReactNode }) {
  return (
    <span className="flex min-w-0 items-center gap-3">
      {cover ? <CoverThumb cover={cover} size={HABIT_THUMB} /> : null}
      <span className="min-w-0">
        <span className="block truncate text-sm font-medium">{h.name}</span>
        {children}
      </span>
    </span>
  );
}

function HabitItem({
  habit: h,
  cover,
  today,
  selected,
  onSelect,
  onPick,
}: {
  habit: HabitRow;
  cover?: CoverRead;
  today?: HabitOccurrenceRow;
  selected: boolean;
  onSelect: () => void;
  onPick: (s: "followed" | "partial" | "not_followed") => void;
}) {
  return (
    <li>
      <div
        className={cn(
          "flex flex-wrap items-center justify-between gap-3 rounded-xl px-3 py-2.5 ring-1 ring-foreground/10 sm:flex-nowrap",
          today ? HABIT_STATE_ROW[today.state] : null,
          selected && "bg-primary/8 ring-primary/20",
        )}
      >
        <button type="button" className="min-h-11 min-w-0 flex-1 text-left" onClick={onSelect}>
          <HabitName habit={h} cover={cover}>
            <span className="block text-[0.6875rem] text-muted-foreground">
              Streak {h.currentStreak}
              {h.lastState ? ` · ${h.lastState.replace("_", " ")}` : ""}
              {h.cue ? ` · Cue: ${h.cue}` : ""}
            </span>
          </HabitName>
        </button>
        <div className="flex shrink-0 flex-wrap items-center gap-2">
          {isReadyToEstablish(h) ? <ReadyToEstablish habit={h} /> : null}
          <BountyChip amounts={h.bounty} suffix="a day" />
          {today ? (
            <AdherenceButtons state={today.state} allowsPartial={today.allowsPartial} onPick={onPick} />
          ) : null}
        </div>
      </div>
    </li>
  );
}

/**
 * Planned habits: made, not tracked yet, so nothing is scheduled or missed. Start tracking
 * moves one to Building, and its days count from that day.
 */
function PlannedSection({
  habits,
  covers,
  selected,
  onSelect,
  onStart,
}: {
  habits: HabitRow[];
  covers: Map<string, CoverRead>;
  selected?: string;
  onSelect: (id: string) => void;
  onStart: (id: string) => void;
}) {
  if (habits.length === 0) return null;
  return (
    <section aria-labelledby="habits-planned">
      <h2 id="habits-planned" className="type-scale-section text-muted-foreground">
        Planned
      </h2>
      <p className="mt-0.5 text-xs text-muted-foreground">
        Not tracked yet: no day is scheduled or missed. Start tracking one when you're ready;
        its days count from then.
      </p>
      <ul className="mt-2 space-y-2">
        {habits.map((h) => (
          <li key={h.id}>
            <div
              className={cn(
                "flex flex-wrap items-center justify-between gap-3 rounded-xl px-3 py-2.5 ring-1 ring-foreground/10 sm:flex-nowrap",
                selected === h.id && "bg-primary/8 ring-primary/20",
              )}
            >
              <button type="button" className="min-h-11 min-w-0 flex-1 text-left" onClick={() => onSelect(h.id)}>
                <HabitName habit={h} cover={covers.get(h.id)}>
                  {h.cue ? (
                    <span className="block text-[0.6875rem] text-muted-foreground">Cue: {h.cue}</span>
                  ) : null}
                </HabitName>
              </button>
              <div className="flex shrink-0 flex-wrap items-center gap-2">
                <BountyChip amounts={h.bounty} suffix="a day" />
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  className="min-h-11 md:min-h-7"
                  aria-label={`Start tracking ${h.name}`}
                  onClick={() => onStart(h.id)}
                >
                  <PlayIcon aria-hidden /> Start tracking
                </Button>
              </div>
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}

function GraduatedSection({
  title,
  hint,
  habits,
  covers,
  selected,
  onSelect,
  maintain,
}: {
  title: string;
  hint: string;
  habits: HabitRow[];
  covers: Map<string, CoverRead>;
  selected?: string;
  onSelect: (id: string) => void;
  /** Established habits: today's logged state and the one-tap check-off. */
  maintain?: {
    todayById: Map<string, HabitOccurrenceRow>;
    onPick: (id: string, pick: MaintainPick) => void;
  };
}) {
  if (habits.length === 0) return null;
  const headingId = `habits-${title.toLowerCase()}`;
  return (
    <section aria-labelledby={headingId}>
      <h2 id={headingId} className="type-scale-section text-muted-foreground">
        {title}
      </h2>
      <p className="mt-0.5 text-xs text-muted-foreground">{hint}</p>
      <ul className="mt-2 space-y-2">
        {habits.map((h) => {
          const today = maintain?.todayById.get(h.id);
          if (!maintain || !today) {
            return (
              <li key={h.id}>
                <button
                  type="button"
                  onClick={() => onSelect(h.id)}
                  className={cn(
                    "min-h-11 w-full rounded-xl px-3 py-2.5 text-left ring-1 ring-foreground/10 hover:bg-muted/70",
                    selected === h.id && "bg-primary/8 ring-primary/20",
                  )}
                >
                  <HabitName habit={h} cover={covers.get(h.id)}>
                    {h.cue ? (
                      <span className="block text-[0.6875rem] text-muted-foreground">Cue: {h.cue}</span>
                    ) : null}
                  </HabitName>
                </button>
              </li>
            );
          }
          return (
            <li key={h.id}>
              <div
                className={cn(
                  "flex flex-wrap items-center justify-between gap-3 rounded-xl px-3 py-2.5 ring-1 ring-foreground/10 sm:flex-nowrap",
                  isMaintainedToday(today.state) ? HABIT_STATE_ROW[today.state] : null,
                  selected === h.id && "bg-primary/8 ring-primary/20",
                )}
              >
                <button type="button" className="min-h-11 min-w-0 flex-1 text-left" onClick={() => onSelect(h.id)}>
                  <HabitName habit={h} cover={covers.get(h.id)}>
                    <span className="block text-[0.6875rem] text-muted-foreground">
                      {maintainedDayLabel(today.state)}
                      {h.cue ? ` · Cue: ${h.cue}` : ""}
                    </span>
                  </HabitName>
                </button>
                <div className="flex shrink-0 flex-wrap items-center gap-2">
                  <BountyChip amounts={h.bounty} suffix="a day" />
                  <MaintainButtons
                    habitName={h.name}
                    state={today.state}
                    allowsPartial={today.allowsPartial}
                    onPick={(pick) => maintain.onPick(h.id, pick)}
                  />
                </div>
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
