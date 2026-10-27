import { useEffect, useId, useMemo, useRef, useState } from "react";
import { CameraIcon, HistoryIcon, ImageIcon } from "lucide-react";
import { toast } from "sonner";
import { MealPhoto, PhotoViewer } from "@/components/food/meal-photo";
import { HalfPointPicker } from "@/components/primitives/half-point-picker";
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
  DIET_RULES,
  DRINK_SCORE,
  formatDuration,
  GRAMS_MAX,
  guessMealType,
  HALF_SCORES,
  MEAL_TYPES,
  NAME_MAX,
  scoreLabel,
  SCORES,
} from "@/lib/diet-score";
import { formatClock, fromLocalInput, mealTypeWord, toLocalInput } from "@/lib/food";
import { useIsDesktop } from "@/lib/media-query";
import { resizePhoto } from "@/lib/photo";
import type { FastRow, FoodEntryRequest, FoodMealType, MealRow } from "@/lib/production-ui-types";
import { useFoodRecent, useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

/**
 * The Log food sheet (FOOD-1): a bottom sheet on a phone, a side panel on a desktop.
 *
 * - Name, with recent entries under it ("Log again" fills the rest, the photo too; the time
 *   stays now).
 * - The health score 1-5 with its examples, and the half points (3.5) as smaller buttons between
 *   them; calories from quick sizes or typed, the time with quick picks, the meal type (guessed
 *   from the time until it's chosen), nutrients folded away.
 * - Alcoholic drink (FOOD-6): the score greys out at 1 and the meal type becomes Drink; turned
 *   off, the score picked before comes back. Log again carries the flag.
 * - With a fast running and calories above 0, "Ends your 17 h fast at 12:30 pm", on by default.
 * - A photo (FOOD-5): Take photo opens the phone's camera, Choose photo its library. It's shrunk
 *   to 1,200 px in the browser first. On a new entry it's sent once the entry is logged; on an
 *   entry already logged it saves as soon as it's picked, and Remove photo takes it off.
 *   Log again and Have another carry the last one's photo, the one already stored, so it isn't
 *   sent again; picking a photo or Remove photo replaces it.
 *
 * Opened on an entry, it edits it as a whole and offers Delete. Opened with a `template` (Have
 * another, on a combined card), it's a new entry filled in from it as Log again fills one; its
 * time is now on today, or the template's own time on a past day.
 */
export function LogFoodSheet({
  open,
  onOpenChange,
  meal,
  template = null,
  day,
  today,
  running,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Null: a new entry. */
  meal: MealRow | null;
  /** A new entry's starting point (Have another). */
  template?: MealRow | null;
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
            key={meal?.id ?? (template ? `another-${template.id}` : "new")}
            meal={meal}
            template={meal ? null : template}
            day={day}
            today={today}
            running={running}
            camera={!desktop}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

function capitalize(text: string): string {
  return `${text[0].toUpperCase()}${text.slice(1)}`;
}

/** A `datetime-local` value `minutes` before now. */
function minutesAgo(minutes: number): string {
  return toLocalInput(Date.now() - minutes * 60_000);
}

/** The name a photo is sent under: the picked file's, as a JPEG once resized. */
function jpegName(filename: string): string {
  return `${filename.replace(/\.[^.]*$/, "") || "photo"}.jpg`;
}

function wholeOrNull(raw: string): number | null | "bad" {
  if (raw.trim() === "") return null;
  const n = Number(raw);
  return Number.isInteger(n) ? n : "bad";
}

function FoodForm({
  meal,
  template,
  day,
  today,
  running,
  camera,
  onDone,
}: {
  meal: MealRow | null;
  template: MealRow | null;
  day: string;
  today: string;
  running: FastRow | null;
  /** Offer Take photo (a phone); a desktop has Choose photo only. */
  camera: boolean;
  onDone: () => void;
}) {
  const writes = useWrites();
  const ids = useId();
  const recent = useFoodRecent(!meal);
  // What the form starts from: the entry being changed, or the one Have another copies.
  const from = meal ?? template;
  const [name, setName] = useState(from?.name ?? "");
  // The score picked; a drink shows and sends 1 instead, and gets this back when it's turned off.
  const [score, setScore] = useState<number | null>(from && !from.alcoholic ? from.score : null);
  const [alcoholic, setAlcoholic] = useState(from?.alcoholic ?? false);
  const [calories, setCalories] = useState(from ? String(from.calories) : "");
  const [eatenAt, setEatenAt] = useState(() =>
    toLocalInput(
      meal?.eatenAt ?? (day === today ? Date.now() : (template?.eatenAt ?? new Date(`${day}T12:00`))),
    ),
  );
  // Have another re-guesses the meal type from the time, as Log again does.
  const [mealType, setMealType] = useState<FoodMealType | "">(meal?.mealType ?? "");
  const [showNutrients, setShowNutrients] = useState(
    !!from && (from.proteinG !== null || from.carbsG !== null || from.fatG !== null),
  );
  const [protein, setProtein] = useState(from?.proteinG?.toString() ?? "");
  const [carbs, setCarbs] = useState(from?.carbsG?.toString() ?? "");
  const [fat, setFat] = useState(from?.fatG?.toString() ?? "");
  const [endFast, setEndFast] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  // The entry's stored photo, kept here: a change saves at once, while `meal` is as it opened.
  const [photoId, setPhotoId] = useState<string | null>(meal?.photoArtifactId ?? null);
  // A new entry's photo, resized and held until the entry is logged.
  const [held, setHeld] = useState<{ blob: Blob; url: string; name: string } | null>(null);
  // A new entry's photo carried over from the entry it copies (Log again, Have another): the
  // photo already stored, so nothing is uploaded. A photo picked here replaces it.
  const [carried, setCarried] = useState<string | null>(meal ? null : (template?.photoArtifactId ?? null));
  // What the photo is busy doing, said under the buttons; null when idle.
  const [photoBusy, setPhotoBusy] = useState<string | null>(null);
  const [viewing, setViewing] = useState(false);
  const cameraInput = useRef<HTMLInputElement>(null);
  const libraryInput = useRef<HTMLInputElement>(null);
  const heldUrl = useRef<string | null>(null);
  useEffect(
    () => () => {
      if (heldUrl.current) URL.revokeObjectURL(heldUrl.current);
    },
    [],
  );

  const at = fromLocalInput(eatenAt);
  const cal = wholeOrNull(calories);
  const grams = [wholeOrNull(protein), wholeOrNull(carbs), wholeOrNull(fat)];
  const shownScore = alcoholic ? DRINK_SCORE : score;
  const guessed = alcoholic ? "drink" : at ? guessMealType(new Date(at)) : "snack";
  const endsFast =
    !meal && running && typeof cal === "number" && cal > 0 && at && Date.parse(at) > Date.parse(running.startedAt)
      ? { seconds: Math.floor((Date.parse(at) - Date.parse(running.startedAt)) / 1000), at }
      : null;

  const problem = !name.trim()
    ? "Say what it was."
    : shownScore === null
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
    setAlcoholic(m.alcoholic);
    if (!m.alcoholic) setScore(m.score);
    setCalories(String(m.calories));
    setProtein(m.proteinG?.toString() ?? "");
    setCarbs(m.carbsG?.toString() ?? "");
    setFat(m.fatG?.toString() ?? "");
    setShowNutrients(m.proteinG !== null || m.carbsG !== null || m.fatG !== null);
    setMealType("");
    // Its photo comes too, unless one was already picked for this entry.
    if (!held) setCarried(m.photoArtifactId);
  }

  /** Turned on, the meal type becomes Drink; turned off, it goes back to what it was. */
  function changeAlcoholic(on: boolean) {
    setAlcoholic(on);
    if (on) setMealType(meal ? "drink" : "");
    else if (meal) setMealType(meal.alcoholic ? mealType : meal.mealType);
  }

  function hold(next: { blob: Blob; name: string } | null) {
    if (heldUrl.current) URL.revokeObjectURL(heldUrl.current);
    heldUrl.current = next ? URL.createObjectURL(next.blob) : null;
    setHeld(next && heldUrl.current ? { ...next, url: heldUrl.current } : null);
  }

  async function pickPhoto(file: File | undefined) {
    if (!file) return;
    setError(null);
    setPhotoBusy(meal ? "Saving the photo…" : "Preparing the photo…");
    try {
      const blob = await resizePhoto(file);
      if (meal) {
        const result = await writes.setFoodPhoto(meal.id, blob, jpegName(file.name));
        setPhotoId(result.meal?.photoArtifactId ?? null);
      } else {
        hold({ blob, name: jpegName(file.name) });
        setCarried(null);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "That photo didn't save.");
    } finally {
      setPhotoBusy(null);
    }
  }

  async function removePhoto() {
    if (!meal) {
      hold(null);
      setCarried(null);
      return;
    }
    setError(null);
    setPhotoBusy("Removing the photo…");
    try {
      await writes.removeFoodPhoto(meal.id);
      setPhotoId(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "The photo is still there: removing it didn't save.");
    } finally {
      setPhotoBusy(null);
    }
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
    if (problem || pending || shownScore === null || typeof cal !== "number" || !at) return;
    const req: FoodEntryRequest = {
      name: name.trim(),
      score: shownScore,
      alcoholic,
      calories: cal,
      eatenAt: at,
      mealType: mealType || undefined,
      proteinG: grams[0] as number | null,
      carbsG: grams[1] as number | null,
      fatG: grams[2] as number | null,
      endFast: !!endsFast && endFast,
      photoArtifactId: !meal && !held && carried ? carried : undefined,
    };
    if (meal) {
      void run(() => writes.editFood(meal.id, req));
      return;
    }
    void run(async () => {
      const logged = await writes.logFood(req);
      if (!held || !logged.meal) return;
      try {
        await writes.setFoodPhoto(logged.meal.id, held.blob, held.name);
      } catch (e) {
        // The entry is in; only its photo isn't. Say so, and how to add it again.
        const why = e instanceof Error ? e.message : "it didn't upload.";
        toast.error(`Logged ${logged.meal.name}, but its photo didn't save: ${why} Open the entry to add it again.`);
      }
    });
  }

  // The photo shown: a stored one by its artifact (the entry's, or the one carried over), or
  // one picked for a new entry by its object URL.
  const storedPhoto = meal ? photoId : held ? null : carried;
  const shownPhoto = storedPhoto ?? held?.url ?? null;
  const photoName = name.trim() || meal?.name || "this entry";

  const chosen = SCORES.find((s) => s.score === shownScore);
  const chosenHalf = HALF_SCORES.find((s) => s.score === shownScore);

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
            : template
              ? `Another ${template.name}, filled in from the last one: change anything that's different.`
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
                    {m.name} · {m.alcoholic ? "drink" : m.score} · {m.calories.toLocaleString()} cal
                  </span>
                </button>
              ))}
            </div>
          ) : null}
        </div>

        <div role="group" aria-labelledby={`${ids}-photo`} className="space-y-1.5">
          <p id={`${ids}-photo`} className="text-sm font-medium">
            Photo
          </p>
          <div className="flex items-start gap-3">
            {shownPhoto ? (
              <button
                type="button"
                onClick={() => setViewing(true)}
                aria-label={`Show the photo of ${photoName} full size`}
                className="size-20 shrink-0 overflow-hidden rounded-lg ring-1 ring-foreground/10 focus-visible:ring-2 focus-visible:ring-ring/50"
              >
                {storedPhoto ? (
                  <MealPhoto artifactId={storedPhoto} alt="" className="size-full" />
                ) : (
                  <img src={shownPhoto} alt="" className="size-full object-cover" />
                )}
              </button>
            ) : null}
            <div className="flex flex-wrap gap-1.5">
              {camera ? (
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="min-h-9 md:min-h-0"
                  disabled={pending || !!photoBusy}
                  onClick={() => cameraInput.current?.click()}
                >
                  <CameraIcon />
                  Take photo
                </Button>
              ) : null}
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="min-h-9 md:min-h-0"
                disabled={pending || !!photoBusy}
                onClick={() => libraryInput.current?.click()}
              >
                <ImageIcon />
                {shownPhoto ? "Choose another" : "Choose photo"}
              </Button>
              {shownPhoto ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="min-h-9 md:min-h-0"
                  disabled={pending || !!photoBusy}
                  onClick={() => void removePhoto()}
                >
                  Remove photo
                </Button>
              ) : null}
            </div>
          </div>
          <p className="text-xs text-muted-foreground" aria-live="polite">
            {photoBusy ??
              (meal
                ? "A photo saves as soon as you pick it."
                : held
                  ? "It's saved with the entry when you log it."
                  : carried
                    ? "The last one's photo comes with it. Choose another or remove it if this one looks different."
                    : "Optional. It's shrunk to 1,200 px before it's sent.")}
          </p>
          {/* Hidden: the buttons above open them. capture asks a phone for its rear camera. */}
          <input
            ref={cameraInput}
            type="file"
            accept="image/*"
            capture="environment"
            hidden
            onChange={(e) => {
              void pickPhoto(e.target.files?.[0]);
              e.target.value = "";
            }}
          />
          <input
            ref={libraryInput}
            type="file"
            accept="image/*"
            hidden
            onChange={(e) => {
              void pickPhoto(e.target.files?.[0]);
              e.target.value = "";
            }}
          />
          <PhotoViewer
            artifactId={storedPhoto}
            src={storedPhoto ? null : held?.url}
            name={photoName}
            open={viewing && !!shownPhoto}
            onOpenChange={setViewing}
          />
        </div>

        <div className="flex items-start gap-3 rounded-lg bg-muted/60 px-3 py-2.5">
          <Switch
            id={`${ids}-alcoholic`}
            checked={alcoholic}
            onCheckedChange={changeAlcoholic}
            disabled={pending}
            aria-describedby={`${ids}-alcoholic-help`}
            className="mt-0.5"
          />
          <div className="space-y-0.5">
            <Label htmlFor={`${ids}-alcoholic`} className="font-normal">
              Alcoholic drink
            </Label>
            <p id={`${ids}-alcoholic-help`} className="text-xs text-muted-foreground">
              A beer, a glass of wine or a shot: one entry is one drink. It always scores 1, and{" "}
              {DIET_RULES.drinksFree === 0
                ? `each one cuts the day's diet XP by ${DIET_RULES.drinkCutPercent}%.`
                : `more than ${DIET_RULES.drinksFree} in a day cut the day's diet XP.`}
            </p>
          </div>
        </div>

        <fieldset className="space-y-1.5" disabled={alcoholic || pending}>
          <legend className="text-sm font-medium">Health score</legend>
          <HalfPointPicker
            name={`${ids}-score`}
            value={shownScore}
            onChange={setScore}
            labelFor={scoreLabel}
            wordFor={(value) => SCORES.find((s) => s.score === value)?.word}
            dimmed={alcoholic}
          />
          <p className="text-xs text-muted-foreground" aria-live="polite">
            {alcoholic
              ? "A drink always scores 1."
              : chosen
                ? `${capitalize(chosen.word)}: ${chosen.examples}.`
                : chosenHalf
                  ? `${capitalize(chosenHalf.word)}: halfway between ${chosenHalf.between[0]} and ${chosenHalf.between[1]}.`
                  : "1 junk · 3 average · 5 very healthy · halves in between"}
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
            {!meal ? (
              <option value="">{`${mealTypeWord(guessed)} (${alcoholic ? "it's alcoholic" : "from the time"})`}</option>
            ) : null}
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
          <Button type="submit" className="min-h-11 md:min-h-0" disabled={!!problem || pending || !!photoBusy}>
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
