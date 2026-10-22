import {
  AppleIcon,
  BeerIcon,
  CupSodaIcon,
  EggIcon,
  HourglassIcon,
  SandwichIcon,
  SoupIcon,
  UtensilsIcon,
  type LucideIcon,
} from "lucide-react";
import { useState } from "react";
import { MealPhoto, PhotoViewer } from "@/components/food/meal-photo";
import { EmptyState } from "@/components/primitives/empty-state";
import { scoreLabel } from "@/lib/diet-score";
import { formatClock, mealTypeWord, SCORE_TILE } from "@/lib/food";
import type { FoodMealType, MealRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

const MEAL_ICONS: Record<FoodMealType, LucideIcon> = {
  breakfast: EggIcon,
  lunch: SandwichIcon,
  dinner: SoupIcon,
  snack: AppleIcon,
  drink: CupSodaIcon,
};

function nutrients(meal: MealRow): string | null {
  const parts = [
    meal.proteinG !== null ? `${meal.proteinG} g protein` : null,
    meal.carbsG !== null ? `${meal.carbsG} g carbs` : null,
    meal.fatG !== null ? `${meal.fatG} g fat` : null,
  ].filter(Boolean);
  return parts.length > 0 ? parts.join(" · ") : null;
}

/**
 * The day's entries as a gallery (FOOD-4), oldest first (FOOD-7): the photo's thumbnail
 * (FOOD-5), or a tile coloured by score with the meal type's icon (a glass of beer for an
 * alcoholic drink, FOOD-6); the name, time and calories, and the score as a number and a word,
 * so colour is never the only cue. An entry eaten inside a recorded fast is flagged. Tapping a
 * thumbnail shows the photo full size; tapping the rest opens the entry for editing.
 */
export function MealGallery({
  meals,
  dayLabel,
  onOpen,
}: {
  meals: MealRow[];
  dayLabel: string;
  onOpen: (meal: MealRow) => void;
}) {
  // The last photo shown stays named while the viewer closes.
  const [viewing, setViewing] = useState<{ meal: MealRow; open: boolean } | null>(null);
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
  return (
    <>
      <ul aria-label={`Entries for ${dayLabel}`} className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {meals.map((meal) => {
          const Icon = meal.alcoholic ? BeerIcon : (MEAL_ICONS[meal.mealType] ?? UtensilsIcon);
          const extra = nutrients(meal);
          const photo = meal.photoArtifactId;
          return (
            <li key={meal.id} className="relative">
              <button
                type="button"
                onClick={() => onOpen(meal)}
                aria-label={`${meal.name}${meal.alcoholic ? " (alcoholic drink)" : ""}, ${formatClock(meal.eatenAt)}: change or delete`}
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
                  <span className="block truncate font-medium">{meal.name}</span>
                  <span className="block text-xs text-muted-foreground">
                    {formatClock(meal.eatenAt)} · {mealTypeWord(meal.mealType)} · {meal.calories.toLocaleString()} cal
                  </span>
                  <span className="mt-1 inline-flex rounded-full bg-muted px-2 py-0.5 text-xs font-medium">
                    {meal.alcoholic ? `${meal.score} · alcoholic` : scoreLabel(meal.score)}
                  </span>
                  {extra ? <span className="mt-1 block text-xs text-muted-foreground">{extra}</span> : null}
                  {meal.duringFast ? (
                    <span className="mt-1 flex items-center gap-1 text-xs text-attention">
                      <HourglassIcon className="size-3" aria-hidden />
                      Eaten during a recorded fast
                    </span>
                  ) : null}
                </span>
              </button>
              {photo ? (
                <button
                  type="button"
                  onClick={() => setViewing({ meal, open: true })}
                  aria-label={`Show the photo of ${meal.name} full size`}
                  className="absolute top-3 left-3 size-16 overflow-hidden rounded-lg ring-1 ring-foreground/10 focus-visible:ring-2 focus-visible:ring-ring/50"
                >
                  <MealPhoto artifactId={photo} alt="" className="size-full" />
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
    </>
  );
}
