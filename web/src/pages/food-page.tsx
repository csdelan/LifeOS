import { useEffect, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { ChevronLeftIcon, ChevronRightIcon, PlusIcon, UtensilsIcon } from "lucide-react";
import { FastingCard } from "@/components/food/fasting-card";
import { LogFoodSheet } from "@/components/food/log-food-sheet";
import { MealGallery } from "@/components/food/meal-gallery";
import { SummaryCard } from "@/components/food/summary-card";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { addDays, formatDayFull } from "@/lib/dates";
import type { MealRow } from "@/lib/production-ui-types";
import { useFoodDay, useWrites } from "@/lib/queries";

/**
 * The Food Log (FOOD-1..4, food-log plan §3): one day at a time, today by default.
 *
 * - Left: the fasting card (a running fast, Begin or End, past fasts) and the day's diet score.
 * - Right: the day's entries as a gallery; tapping one opens it for editing.
 * - Log food opens the sheet; on a phone it sits at the bottom of the screen, under the thumb.
 *
 * Opening the page settles the food days that are due (`POST /api/food/settle`): a day that
 * ended unclosed pays then, and a fast or entry changed elsewhere is re-scored.
 */
export function FoodPage() {
  const search = useSearch({ from: "/food" });
  const navigate = useNavigate({ from: "/food" });
  const food = useFoodDay(search.date);
  const writes = useWrites();
  const settled = useRef(false);
  const [sheet, setSheet] = useState<{ open: boolean; meal: MealRow | null }>({ open: false, meal: null });

  useEffect(() => {
    if (settled.current) return;
    settled.current = true;
    // A failed settle changes nothing on screen: the reads still show what each day is on track for.
    void writes.settleFood().catch(() => undefined);
  }, [writes]);

  const day = food.data;
  const today = day?.today ?? search.date ?? "";
  // Step from the date asked for, not the day on screen: the last day stays up while the next loads.
  const shown = search.date ?? today;
  const isToday = !!day && day.day === day.today;
  const title = !day ? "Food Log" : isToday ? "Today" : formatDayFull(day.day);

  const goTo = (date: string | undefined) =>
    void navigate({ search: (prev) => ({ ...prev, date: date && date !== today ? date : undefined }) });

  const openNew = () => setSheet({ open: true, meal: null });

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-4 py-4 md:px-6">
        <p className="type-scale-section text-primary">Food Log</p>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h1 className="font-heading text-2xl">{title}</h1>
          <div className="flex items-center gap-1">
            <nav aria-label="Days" className="flex items-center gap-1">
              <Button
                variant="ghost"
                size="icon"
                aria-label="Previous day"
                disabled={!day}
                onClick={() => goTo(addDays(shown, -1))}
              >
                <ChevronLeftIcon />
              </Button>
              <Button variant="outline" size="sm" disabled={!day || isToday} onClick={() => goTo(undefined)}>
                Today
              </Button>
              <Button
                variant="ghost"
                size="icon"
                aria-label="Next day"
                disabled={!day || isToday}
                onClick={() => goTo(addDays(shown, 1))}
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
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto px-4 py-5 md:px-6">
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
              <SummaryCard summary={day.summary} isToday={isToday} />
            </div>
            <section aria-labelledby="entries-title">
              <h2 id="entries-title" className="type-scale-section mb-3 text-muted-foreground">
                Entries
              </h2>
              <MealGallery
                meals={day.meals}
                dayLabel={isToday ? "today" : formatDayFull(day.day)}
                onOpen={(meal) => setSheet({ open: true, meal })}
              />
            </section>
          </div>
        ) : null}
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
