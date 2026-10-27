import { SCORE_FILL } from "@/lib/food";
import { cn } from "@/lib/utils";

/**
 * The picker's buttons in order, each with its place on the ten-column grid: the whole points
 * in the top row, each half in the row under it, centred on the gap between its neighbours
 * (food-log plan §8). Written out so Tailwind sees every class.
 */
const PICKER: readonly { score: number; half: boolean; place: string }[] = [
  { score: 1, half: false, place: "col-start-1 row-start-1" },
  { score: 1.5, half: true, place: "col-start-2 row-start-2" },
  { score: 2, half: false, place: "col-start-3 row-start-1" },
  { score: 2.5, half: true, place: "col-start-4 row-start-2" },
  { score: 3, half: false, place: "col-start-5 row-start-1" },
  { score: 3.5, half: true, place: "col-start-6 row-start-2" },
  { score: 4, half: false, place: "col-start-7 row-start-1" },
  { score: 4.5, half: true, place: "col-start-8 row-start-2" },
  { score: 5, half: false, place: "col-start-9 row-start-1" },
];

/**
 * A 1–5 picker in half points, as the Food Log's health score and the home's ratings use it:
 * radio buttons in a ten-column grid, each whole point spanning two columns in the top row and
 * each half under the gap between its neighbours, so 3.5 sits between 3 and 4. In order, so the
 * arrow keys step 1, 1.5, 2 … 5. Put it in a fieldset whose legend names it; the fieldset's
 * `disabled` reaches the radios.
 */
export function HalfPointPicker({
  name,
  value,
  onChange,
  labelFor,
  wordFor,
  dimmed = false,
  className,
}: {
  /** The radio group's name: unique on the page. */
  name: string;
  value: number | null;
  onChange: (value: number) => void;
  /** Each choice's accessible name: "3.5 · average to good". */
  labelFor: (value: number) => string;
  /** The word under a whole point ("good"), if any. */
  wordFor?: (value: number) => string | undefined;
  /** Shown faded, as while the choice doesn't apply (an alcoholic drink, Same as last time). */
  dimmed?: boolean;
  className?: string;
}) {
  return (
    <div className={cn("grid grid-cols-10 gap-x-1.5 gap-y-1", dimmed && "opacity-50", className)}>
      {PICKER.map(({ score, half, place }) => {
        const word = half ? undefined : wordFor?.(score);
        return (
          <label
            key={score}
            className={cn(
              "relative col-span-2 flex cursor-pointer flex-col items-center justify-center rounded-lg text-center has-[:disabled]:cursor-not-allowed has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-ring/50",
              place,
              half ? "min-h-10 w-3/4 justify-self-center px-0.5 py-1" : "min-h-14 px-1 py-1.5",
              value === score
                ? "bg-muted ring-2 ring-primary"
                : "bg-card ring-1 ring-foreground/10 hover:bg-muted has-[:disabled]:hover:bg-card",
            )}
          >
            <input
              type="radio"
              name={name}
              value={score}
              aria-label={labelFor(score)}
              checked={value === score}
              onChange={() => onChange(score)}
              className="absolute inset-0 z-10 cursor-pointer appearance-none rounded-lg opacity-0 disabled:cursor-not-allowed"
            />
            <span
              aria-hidden
              className={cn("rounded-full", half ? "mb-0.5 h-1 w-4" : "mb-1 h-1.5 w-6", SCORE_FILL[score])}
            />
            <span className={cn("leading-none font-semibold", half ? "text-sm" : "text-lg")}>{score}</span>
            {word ? <span className="mt-0.5 text-[0.65rem] leading-tight">{word}</span> : null}
          </label>
        );
      })}
    </div>
  );
}
