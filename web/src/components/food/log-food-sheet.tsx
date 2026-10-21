import { useId, useMemo, useState } from "react";
import { HistoryIcon } from "lucide-react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Switch } from "@/components/ui/switch";
import {
  CALORIE_SIZES,
  CALORIES_MAX,
  formatDuration,
  GRAMS_MAX,
  guessMealType,
  MEAL_TYPES,
  NAME_MAX,
  scoreLabel,
  SCORES,
} from "@/lib/diet-score";
import { formatClock, fromLocalInput, mealTypeWord, SCORE_FILL, toLocalInput } from "@/lib/food";
import { useIsDesktop } from "@/lib/media-query";
import type { FastRow, FoodEntryRequest, FoodMealType, MealRow } from "@/lib/production-ui-types";
import { useFoodRecent, useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

/**
 * The Log food sheet (FOOD-1): a bottom sheet on a phone, a side panel on a desktop.
 *
 * - Name, with recent entries under it ("Log again" fills the rest; the time stays now).
 * - The health score 1-5 with its examples, calories from quick sizes or typed, the time with
 *   quick picks, the meal type (guessed from the time until it's chosen), nutrients folded away.
 * - With a fast running and calories above 0, "Ends your 17 h fast at 12:30 pm", on by default.
 *
 * Opened on an entry, it edits it as a whole and offers Delete.
 */
export function LogFoodSheet({
  open,
  onOpenChange,
  meal,
  day,
  today,
  running,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Null: a new entry. */
  meal: MealRow | null;
  /** The day the page shows: a new entry on a past day starts at noon there. */
  day: string;
  today: string;
  running: FastRow | null;
}) {
  const desktop = useIsDesktop();
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side={desktop ? "right" : "bottom"}
        className={cn("gap-0", desktop ? "w-full sm:max-w-md" : "max-h-[92dvh] rounded-t-2xl")}
      >
        {open ? (
          <FoodForm
            key={meal?.id ?? "new"}
            meal={meal}
            day={day}
            today={today}
            running={running}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

/** A `datetime-local` value `minutes` before now. */
function minutesAgo(minutes: number): string {
  return toLocalInput(Date.now() - minutes * 60_000);
}

function wholeOrNull(raw: string): number | null | "bad" {
  if (raw.trim() === "") return null;
  const n = Number(raw);
  return Number.isInteger(n) ? n : "bad";
}

function FoodForm({
  meal,
  day,
  today,
  running,
  onDone,
}: {
  meal: MealRow | null;
  day: string;
  today: string;
  running: FastRow | null;
  onDone: () => void;
}) {
  const writes = useWrites();
  const ids = useId();
  const recent = useFoodRecent(!meal);
  const [name, setName] = useState(meal?.name ?? "");
  const [score, setScore] = useState<number | null>(meal?.score ?? null);
  const [calories, setCalories] = useState(meal ? String(meal.calories) : "");
  const [eatenAt, setEatenAt] = useState(() =>
    toLocalInput(meal?.eatenAt ?? (day === today ? Date.now() : new Date(`${day}T12:00`))),
  );
  const [mealType, setMealType] = useState<FoodMealType | "">(meal?.mealType ?? "");
  const [showNutrients, setShowNutrients] = useState(
    !!meal && (meal.proteinG !== null || meal.carbsG !== null || meal.fatG !== null),
  );
  const [protein, setProtein] = useState(meal?.proteinG?.toString() ?? "");
  const [carbs, setCarbs] = useState(meal?.carbsG?.toString() ?? "");
  const [fat, setFat] = useState(meal?.fatG?.toString() ?? "");
  const [endFast, setEndFast] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  const at = fromLocalInput(eatenAt);
  const cal = wholeOrNull(calories);
  const grams = [wholeOrNull(protein), wholeOrNull(carbs), wholeOrNull(fat)];
  const guessed = at ? guessMealType(new Date(at)) : "snack";
  const endsFast =
    !meal && running && typeof cal === "number" && cal > 0 && at && Date.parse(at) > Date.parse(running.startedAt)
      ? { seconds: Math.floor((Date.parse(at) - Date.parse(running.startedAt)) / 1000), at }
      : null;

  const problem = !name.trim()
    ? "Say what it was."
    : score === null
      ? "Pick a health score."
      : cal === null || cal === "bad" || cal < 0 || cal > CALORIES_MAX
        ? `Calories are a whole number from 0 to ${CALORIES_MAX.toLocaleString()}.`
        : !at
          ? "Say when you ate it."
          : grams.some((g) => g === "bad" || (typeof g === "number" && (g < 0 || g > GRAMS_MAX)))
            ? `Nutrients are whole grams from 0 to ${GRAMS_MAX.toLocaleString()}.`
            : null;

  const suggestions = useMemo(() => {
    const typed = name.trim().toLowerCase();
    return (recent.data ?? [])
      .filter((m) => (typed ? m.name.toLowerCase().includes(typed) && m.name.toLowerCase() !== typed : true))
      .slice(0, typed ? 5 : 6);
  }, [recent.data, name]);

  function logAgain(m: MealRow) {
    setName(m.name);
    setScore(m.score);
    setCalories(String(m.calories));
    setProtein(m.proteinG?.toString() ?? "");
    setCarbs(m.carbsG?.toString() ?? "");
    setFat(m.fatG?.toString() ?? "");
    setShowNutrients(m.proteinG !== null || m.carbsG !== null || m.fatG !== null);
    setMealType("");
  }

  async function run(write: () => Promise<unknown>) {
    setError(null);
    setPending(true);
    try {
      await write();
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  function submit() {
    if (problem || pending || score === null || typeof cal !== "number" || !at) return;
    const req: FoodEntryRequest = {
      name: name.trim(),
      score,
      calories: cal,
      eatenAt: at,
      mealType: mealType || undefined,
      proteinG: grams[0] as number | null,
      carbsG: grams[1] as number | null,
      fatG: grams[2] as number | null,
      endFast: !!endsFast && endFast,
    };
    void run(() => (meal ? writes.editFood(meal.id, req) : writes.logFood(req)));
  }

  const chosen = SCORES.find((s) => s.score === score);

  return (
    <form
      className="flex min-h-0 flex-1 flex-col"
      onSubmit={(e) => {
        e.preventDefault();
        submit();
      }}
    >
      <SheetHeader className="pb-2">
        <SheetTitle>{meal ? `Change ${meal.name}` : "Log food"}</SheetTitle>
        <SheetDescription>
          {meal
            ? "Changes keep the record: the entry gets a newer version, and a day that already paid is re-scored."
            : "What you ate or drank, how healthy it was, and roughly how many calories."}
        </SheetDescription>
      </SheetHeader>

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-4 pb-4">
        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-name`}>Name</Label>
          <Input
            id={`${ids}-name`}
            value={name}
            maxLength={NAME_MAX}
            autoComplete="off"
            placeholder="Salmon salad"
            disabled={pending}
            onChange={(e) => setName(e.target.value)}
          />
          {!meal && suggestions.length > 0 ? (
            <div role="group" aria-label="Log again" className="flex flex-wrap gap-1.5 pt-1">
              {suggestions.map((m) => (
                <button
                  key={m.id}
                  type="button"
                  onClick={() => logAgain(m)}
                  className="inline-flex min-h-9 items-center gap-1.5 rounded-full bg-muted px-3 py-1 text-xs hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring/50 md:min-h-0"
                >
                  <HistoryIcon className="size-3" aria-hidden />
                  <span>
                    <span className="sr-only">Log again: </span>
                    {m.name} · {m.score} · {m.calories.toLocaleString()} cal
                  </span>
                </button>
              ))}
            </div>
          ) : null}
        </div>

        <fieldset className="space-y-1.5">
          <legend className="text-sm font-medium">Health score</legend>
          <div className="grid grid-cols-5 gap-1.5">
            {SCORES.map((s) => (
              <label
                key={s.score}
                className={cn(
                  "relative flex min-h-14 cursor-pointer flex-col items-center justify-center rounded-lg px-1 py-1.5 text-center has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring/50",
                  score === s.score ? "bg-muted ring-2 ring-primary" : "bg-card ring-1 ring-foreground/10 hover:bg-muted",
                )}
              >
                <input
                  type="radio"
                  name={`${ids}-score`}
                  value={s.score}
                  aria-label={scoreLabel(s.score)}
                  checked={score === s.score}
                  onChange={() => setScore(s.score)}
                  disabled={pending}
                  className="absolute inset-0 z-10 cursor-pointer appearance-none rounded-lg opacity-0"
                />
                <span aria-hidden className={cn("mb-1 h-1.5 w-6 rounded-full", SCORE_FILL[s.score])} />
                <span className="text-lg leading-none font-semibold">{s.score}</span>
                <span className="mt-0.5 text-[0.65rem] leading-tight">{s.word}</span>
              </label>
            ))}
          </div>
          <p className="text-xs text-muted-foreground" aria-live="polite">
            {chosen ? `${chosen.word[0].toUpperCase()}${chosen.word.slice(1)}: ${chosen.examples}.` : "1 junk · 3 average · 5 very healthy"}
          </p>
        </fieldset>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-calories`}>Calories</Label>
          <div role="group" aria-label="Quick sizes" className="flex flex-wrap gap-1.5">
            {CALORIE_SIZES.map((size) => (
              <Button
                key={size.label}
                type="button"
                variant={cal === size.calories ? "secondary" : "outline"}
                size="sm"
                aria-pressed={cal === size.calories}
                className="min-h-9 md:min-h-0"
                onClick={() => setCalories(String(size.calories))}
              >
                {size.label} {size.calories.toLocaleString()}
              </Button>
            ))}
          </div>
          <Input
            id={`${ids}-calories`}
            type="number"
            inputMode="numeric"
            min={0}
            max={CALORIES_MAX}
            step={1}
            value={calories}
            disabled={pending}
            placeholder="700"
            onChange={(e) => setCalories(e.target.value)}
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-time`}>Eaten at</Label>
          <Input
            id={`${ids}-time`}
            type="datetime-local"
            value={eatenAt}
            disabled={pending}
            onChange={(e) => setEatenAt(e.target.value)}
          />
          <div role="group" aria-label="Quick times" className="flex flex-wrap gap-1.5">
            {[
              { label: "Now", minutes: 0 },
              { label: "30 min ago", minutes: 30 },
              { label: "1 h ago", minutes: 60 },
              { label: "2 h ago", minutes: 120 },
            ].map((pick) => (
              <Button
                key={pick.label}
                type="button"
                variant="ghost"
                size="sm"
                className="min-h-9 md:min-h-0"
                onClick={() => setEatenAt(minutesAgo(pick.minutes))}
              >
                {pick.label}
              </Button>
            ))}
          </div>
        </div>

        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-type`}>Meal type</Label>
          <select
            id={`${ids}-type`}
            value={mealType}
            disabled={pending}
            onChange={(e) => setMealType(e.target.value as FoodMealType | "")}
            className="h-9 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 dark:bg-input/30"
          >
            {!meal ? <option value="">{`${mealTypeWord(guessed)} (from the time)`}</option> : null}
            {MEAL_TYPES.map((t) => (
              <option key={t} value={t}>
                {mealTypeWord(t)}
              </option>
            ))}
          </select>
        </div>

        <div className="space-y-2">
          <Button
            type="button"
            variant="link"
            size="sm"
            className="h-auto px-0"
            aria-expanded={showNutrients}
            onClick={() => setShowNutrients((v) => !v)}
          >
            {showNutrients ? "Hide nutrients" : "Add protein, carbs and fat"}
          </Button>
          {showNutrients ? (
            <div className="grid grid-cols-3 gap-2">
              {(
                [
                  ["protein", "Protein (g)", protein, setProtein],
                  ["carbs", "Carbs (g)", carbs, setCarbs],
                  ["fat", "Fat (g)", fat, setFat],
                ] as const
              ).map(([key, label, value, set]) => (
                <div key={key} className="space-y-1">
                  <Label htmlFor={`${ids}-${key}`} className="text-xs">
                    {label}
                  </Label>
                  <Input
                    id={`${ids}-${key}`}
                    type="number"
                    inputMode="numeric"
                    min={0}
                    max={GRAMS_MAX}
                    step={1}
                    value={value}
                    disabled={pending}
                    onChange={(e) => set(e.target.value)}
                  />
                </div>
              ))}
            </div>
          ) : null}
          <p className="text-xs text-muted-foreground">Nutrients are kept for you; they don't change the XP.</p>
        </div>

        {endsFast ? (
          <div className="flex items-center gap-3 rounded-lg bg-muted/60 px-3 py-2.5">
            <Switch id={`${ids}-end-fast`} checked={endFast} onCheckedChange={setEndFast} disabled={pending} />
            <Label htmlFor={`${ids}-end-fast`} className="font-normal">
              Ends your {formatDuration(endsFast.seconds)} fast at {formatClock(endsFast.at)}
            </Label>
          </div>
        ) : null}

        {problem ? (
          <p className="text-xs text-muted-foreground" aria-live="polite">
            {problem}
          </p>
        ) : null}
        {error ? (
          <p role="alert" className="text-xs text-destructive">
            {error}
          </p>
        ) : null}
      </div>

      <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t px-4 py-3">
        {meal ? (
          <Button type="button" variant="destructive" disabled={pending} onClick={() => setConfirmDelete(true)}>
            Delete
          </Button>
        ) : (
          <span />
        )}
        <div className="flex gap-2">
          <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" className="min-h-11 md:min-h-0" disabled={!!problem || pending}>
            {pending ? "Saving…" : meal ? "Save" : "Log"}
          </Button>
        </div>
      </div>

      {meal ? (
        <AlertDialog open={confirmDelete} onOpenChange={setConfirmDelete}>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Delete {meal.name}?</AlertDialogTitle>
              <AlertDialogDescription>
                The record keeps what it was. If its day already paid, the day is re-scored without it.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>Keep it</AlertDialogCancel>
              <AlertDialogAction
                variant="destructive"
                onClick={() => void run(() => writes.deleteFood(meal.id))}
              >
                Delete
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      ) : null}
    </form>
  );
}
