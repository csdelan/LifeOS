import {
  AppleIcon,
  BeerIcon,
  ChevronRightIcon,
  CupSodaIcon,
  EggIcon,
  HourglassIcon,
  PlusIcon,
  SandwichIcon,
  SoupIcon,
  UtensilsIcon,
  type LucideIcon,
} from "lucide-react";
import { useMemo, useState } from "react";
import { MealPhoto, PhotoViewer } from "@/components/food/meal-photo";
import { EmptyState } from "@/components/primitives/empty-state";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { scoreLabel } from "@/lib/diet-score";
import { formatClock, mealTypeWord, SCORE_TILE } from "@/lib/food";
import { groupMeals, groupName, groupTimeRange, type MealGroup } from "@/lib/meal-groups";
import type { FoodMealType, MealRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

const MEAL_ICONS: Record<FoodMealType, LucideIcon> = {
  breakfast: EggIcon,
  lunch: SandwichIcon,
  dinner: SoupIcon,
  snack: AppleIcon,
  drink: CupSodaIcon,
};

function nutrients(group: MealGroup): string | null {
  const parts = [
    group.proteinG !== null ? `${group.proteinG} g protein` : null,
    group.carbsG !== null ? `${group.carbsG} g carbs` : null,
    group.fatG !== null ? `${group.fatG} g fat` : null,
  ].filter(Boolean);
  return parts.length > 0 ? parts.join(" · ") : null;
}

/**
 * The day's entries as a gallery (FOOD-4), oldest first (FOOD-7): the photo's thumbnail
 * (FOOD-5), or a tile coloured by score with the meal type's icon (a glass of beer for an
 * alcoholic drink, FOOD-6); the name, time and calories, and the score as a number and a word,
 * so colour is never the only cue. An entry eaten inside a recorded fast is flagged. Tapping a
 * thumbnail shows the photo full size; tapping the rest opens the entry for editing.
 *
 * Identical entries on the day show as one card (FOOD-7, plan §7.4), "Beer ×3", with the
 * calories and nutrients summed and the time as a range. Tapping it lists the copies, each
 * opening to change or delete, with Have another.
 */
export function MealGallery({
  meals,
  dayLabel,
  onOpen,
  onAnother,
}: {
  meals: MealRow[];
  dayLabel: string;
  /** Change or delete one entry. */
  onOpen: (meal: MealRow) => void;
  /** Log food filled in with this entry, as Log again does. */
  onAnother: (meal: MealRow) => void;
}) {
  const groups = useMemo(() => groupMeals(meals), [meals]);
  // The last photo or list shown stays named while its dialog closes.
  const [viewing, setViewing] = useState<{ meal: MealRow; open: boolean } | null>(null);
  const [copies, setCopies] = useState<{ group: MealGroup; open: boolean } | null>(null);
  // A write while the list is open re-reads the day: show the group as it is now.
  const listed = copies ? (groups.find((g) => g.key === copies.group.key) ?? copies.group) : null;

  if (meals.length === 0) {
    return (
      <EmptyState
        icon={UtensilsIcon}
        title="Nothing logged"
        description="Log what you eat and drink with calories: the day's diet XP comes from its quality, its calories and your fasting."
        className="rounded-xl ring-1 ring-foreground/10"
      />
    );
  }

  const closeCopies = () => setCopies((c) => (c ? { ...c, open: false } : c));

  return (
    <>
      <ul aria-label={`Entries for ${dayLabel}`} className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {groups.map((group) => {
          const meal = group.first;
          const Icon = meal.alcoholic ? BeerIcon : (MEAL_ICONS[meal.mealType] ?? UtensilsIcon);
          const extra = nutrients(group);
          const photo = group.photo;
          const range = groupTimeRange(group);
          const name = groupName(group);
          const drink = meal.alcoholic ? " (alcoholic drink)" : "";
          return (
            <li key={group.key} className="relative">
              <button
                type="button"
                onClick={() => (group.count > 1 ? setCopies({ group, open: true }) : onOpen(meal))}
                aria-label={
                  group.count > 1
                    ? `${name}${drink}, ${range}: show the ${group.count} entries`
                    : `${name}${drink}, ${range}: change or delete`
                }
                className="flex h-full w-full items-stretch gap-3 rounded-xl bg-card p-3 text-left ring-1 ring-foreground/10 transition-colors hover:bg-muted/50 focus-visible:ring-2 focus-visible:ring-ring/50"
              >
                {photo ? (
                  // The thumbnail is its own button, laid over this spot.
                  <span aria-hidden className="size-16 shrink-0" />
                ) : (
                  <span
                    aria-hidden
                    className={cn("flex size-16 shrink-0 items-center justify-center rounded-lg", SCORE_TILE[meal.score])}
                  >
                    <Icon className="size-7" />
                  </span>
                )}
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{name}</span>
                  <span className="block text-xs text-muted-foreground">
                    {range} · {mealTypeWord(meal.mealType)} · {group.calories.toLocaleString()} cal
                  </span>
                  <span className="mt-1 inline-flex rounded-full bg-muted px-2 py-0.5 text-xs font-medium whitespace-nowrap">
                    {meal.alcoholic ? `${meal.score} · alcoholic` : scoreLabel(meal.score)}
                  </span>
                  {extra ? <span className="mt-1 block text-xs text-muted-foreground">{extra}</span> : null}
                  {group.duringFast ? (
                    <span className="mt-1 flex items-center gap-1 text-xs text-attention">
                      <HourglassIcon className="size-3" aria-hidden />
                      {group.count > 1 ? "One was eaten during a recorded fast" : "Eaten during a recorded fast"}
                    </span>
                  ) : null}
                </span>
              </button>
              {photo ? (
                <button
                  type="button"
                  onClick={() => setViewing({ meal: photo.meal, open: true })}
                  aria-label={`Show the photo of ${photo.meal.name} full size`}
                  className="absolute top-3 left-3 size-16 overflow-hidden rounded-lg ring-1 ring-foreground/10 focus-visible:ring-2 focus-visible:ring-ring/50"
                >
                  <MealPhoto artifactId={photo.artifactId} alt="" className="size-full" />
                </button>
              ) : null}
            </li>
          );
        })}
      </ul>
      <PhotoViewer
        artifactId={viewing?.meal.photoArtifactId}
        name={viewing?.meal.name ?? ""}
        open={!!viewing?.open}
        onOpenChange={(open) => setViewing((v) => (v ? { ...v, open } : v))}
      />
      <Dialog open={!!copies?.open} onOpenChange={(open) => setCopies((c) => (c ? { ...c, open } : c))}>
        {listed ? (
          <DialogContent>
            <DialogHeader>
              <DialogTitle className="pr-8">{groupName(listed)}</DialogTitle>
              <DialogDescription>
                Each is its own entry, shown as one card because they're the same. Pick one to change or delete it.
              </DialogDescription>
            </DialogHeader>
            <ul aria-label={`Each ${listed.first.name}`} className="-mx-2 divide-y">
              {listed.meals.map((m) => (
                <li key={m.id}>
                  <button
                    type="button"
                    onClick={() => {
                      closeCopies();
                      onOpen(m);
                    }}
                    aria-label={`${m.name}, ${formatClock(m.eatenAt)}: change or delete`}
                    className="flex min-h-11 w-full items-center justify-between gap-3 rounded-md px-2 py-2 text-left hover:bg-muted focus-visible:ring-2 focus-visible:ring-ring/50 md:min-h-0"
                  >
                    <span className="font-medium tabular-nums">{formatClock(m.eatenAt)}</span>
                    <span className="flex items-center gap-1 text-xs text-muted-foreground">
                      {mealTypeWord(m.mealType)} · {m.calories.toLocaleString()} cal
                      <ChevronRightIcon className="size-3.5" aria-hidden />
                    </span>
                  </button>
                </li>
              ))}
            </ul>
            <DialogFooter>
              <Button
                className="min-h-11 md:min-h-0"
                onClick={() => {
                  closeCopies();
                  onAnother(listed.last);
                }}
              >
                <PlusIcon />
                Have another
              </Button>
            </DialogFooter>
          </DialogContent>
        ) : null}
      </Dialog>
    </>
  );
}
