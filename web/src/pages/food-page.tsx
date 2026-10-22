import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { ChevronLeftIcon, ChevronRightIcon, PlusIcon, UtensilsIcon } from "lucide-react";
import { useOuraSyncOnOpen } from "@/components/activity/use-oura-sync-on-open";
import { FastingCard } from "@/components/food/fasting-card";
import { FoodPeriodView } from "@/components/food/food-period-view";
import { LogFoodSheet } from "@/components/food/log-food-sheet";
import { MealGallery } from "@/components/food/meal-gallery";
import { SummaryCard } from "@/components/food/summary-card";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { Button } from "@/components/ui/button";
import { formatDayFull, todayIso } from "@/lib/dates";
import { foodPeriod, foodPeriodTitle, holdsToday, stepFoodDate, type FoodView } from "@/lib/food-period";
import type { MealRow } from "@/lib/production-ui-types";
import { useFoodDay, useWrites } from "@/lib/queries";

const VIEWS: { id: FoodView; label: string }[] = [
  { id: "day", label: "Day" },
  { id: "week", label: "Week" },
  { id: "month", label: "Month" },
];

/**
 * The Food Log (FOOD-1..4, food-log plan §3.7): a day at a time by default, or the week or
 * month around `date` (`?view=week|month`).
 *
 * - Day: the fasting card (a running fast, Begin or End, past fasts), the day's diet score
 *   and its entries as a gallery; tapping one opens it for editing.
 * - Week and Month: the period's totals and each day's diet XP; picking a day opens it.
 * - Log food opens the sheet; on a phone it sits at the bottom of the screen, under the thumb.
 *
 * Opening the page settles the food days that are due (`POST /api/food/settle`): a day that
 * ended unclosed pays then, and a fast or entry changed elsewhere is re-scored.
 */
export function FoodPage() {
  const search = useSearch({ from: "/food" });
  const navigate = useNavigate({ from: "/food" });
  const view: FoodView = search.view ?? "day";
  // The Day view reads its day; the Week and Month views read today, for Log food.
  const food = useFoodDay(view === "day" ? search.date : undefined);
  const writes = useWrites();
  const settled = useRef(false);
  useOuraSyncOnOpen();
  const [sheet, setSheet] = useState<{ open: boolean; meal: MealRow | null }>({ open: false, meal: null });

  useEffect(() => {
    if (settled.current) return;
    settled.current = true;
    // A failed settle changes nothing on screen: the reads still show what each day is on track for.
    void writes.settleFood().catch(() => undefined);
  }, [writes]);

  const day = food.data;
  const today = day?.today ?? todayIso();
  // Step from the date asked for, not the day on screen: the last day stays up while the next loads.
  const period = foodPeriod(view, search.date ?? today);
  const atToday = holdsToday(period, today);
  const title = view === "day" && !day ? "Food Log" : foodPeriodTitle(period, today);

  const show = (next: { view?: FoodView; date?: string }) =>
    void navigate({
      search: {
        view: next.view && next.view !== "day" ? next.view : undefined,
        date: next.date && !holdsToday(foodPeriod(next.view ?? "day", next.date), today) ? next.date : undefined,
      },
    });

  const openNew = () => setSheet({ open: true, meal: null });

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-4 py-4 md:px-6">
        <p className="type-scale-section text-primary">Food Log</p>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="font-heading text-2xl" aria-live="polite">
            {title}
          </h1>
          <div className="flex items-center gap-1">
            <nav aria-label={`${view[0].toUpperCase()}${view.slice(1)}s`} className="flex items-center gap-1">
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Previous ${view}`}
                onClick={() => show({ view, date: stepFoodDate(view, period.from, -1) })}
              >
                <ChevronLeftIcon />
              </Button>
              <Button variant="outline" size="sm" disabled={atToday} onClick={() => show({ view })}>
                Today
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label={`Next ${view}`}
                disabled={atToday || period.from > today}
                onClick={() => show({ view, date: stepFoodDate(view, period.from, 1) })}
              >
                <ChevronRightIcon />
              </Button>
            </nav>
            <Button className="ml-2 hidden md:inline-flex" disabled={!day} onClick={openNew}>
              <PlusIcon />
              Log food
            </Button>
          </div>
        </div>
        <div className="mt-3">
          <ToggleGroup label="View" value={view} options={VIEWS} onChange={(next) => show({ view: next, date: search.date })} />
        </div>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-5 md:px-6">
        {view !== "day" ? (
          <FoodPeriodView period={period} today={today} onOpenDay={(date) => show({ date })} />
        ) : (
          <>
            {food.isLoading ? (
              <div className="grid gap-4 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)]">
                <CardSkeleton />
                <CardSkeleton />
              </div>
            ) : null}
            {food.isError ? (
              <EmptyState
                tone="error"
                icon={UtensilsIcon}
                title="The Food Log failed to load"
                description="This is not an empty day — the reader never returned."
              />
            ) : null}
            {day ? (
              <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,22rem)_minmax(0,1fr)]">
                <div className="space-y-4">
                  <FastingCard day={day} />
                  <SummaryCard summary={day.summary} isToday={day.day === day.today} />
                </div>
                <section aria-labelledby="entries-title">
                  <h2 id="entries-title" className="type-scale-section mb-3 text-muted-foreground">
                    Entries
                  </h2>
                  <MealGallery
                    meals={day.meals}
                    dayLabel={day.day === day.today ? "today" : formatDayFull(day.day)}
                    onOpen={(meal) => setSheet({ open: true, meal })}
                  />
                </section>
              </div>
            ) : null}
          </>
        )}
      </div>

      <div className="shrink-0 border-t bg-background px-4 py-3 md:hidden">
        <Button className="min-h-11 w-full" disabled={!day} onClick={openNew}>
          <PlusIcon />
          Log food
        </Button>
      </div>

      {day ? (
        <LogFoodSheet
          open={sheet.open}
          onOpenChange={(open) => setSheet((s) => ({ ...s, open }))}
          meal={sheet.meal}
          day={day.day}
          today={day.today}
          running={day.running}
        />
      ) : null}
    </div>
  );
}
