import { useId, useMemo, useState } from "react";
import { ChevronDownIcon } from "lucide-react";
import { HalfPointPicker } from "@/components/primitives/half-point-picker";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Switch } from "@/components/ui/switch";
import {
  HOME_PARTS,
  NOTE_MAX,
  PART_LABEL,
  ratedAgo,
  ratingHelp,
  ratingLabel,
  ratingWord,
  walkThroughOrder,
} from "@/lib/environment";
import { useIsDesktop } from "@/lib/media-query";
import type { HomePart, HomeSpaceRow, SpaceRatingRequest } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

/** One space's picks in the sheet: none of them sent until touched. */
interface Draft {
  parts: Record<HomePart, number | null>;
  same: boolean;
  note: string;
}

const EMPTY: Draft = { parts: { tidiness: null, cleanliness: null, aesthetics: null }, same: false, note: "" };

function touched(draft: Draft): boolean {
  return draft.same || HOME_PARTS.some((p) => draft.parts[p] != null) || draft.note.trim() !== "";
}

function request(space: HomeSpaceRow, draft: Draft): SpaceRatingRequest {
  const note = draft.note.trim() || undefined;
  if (draft.same) return { space: space.id, same: true, note };
  const req: SpaceRatingRequest = { space: space.id, note };
  for (const p of HOME_PARTS) if (draft.parts[p] != null) req[p] = draft.parts[p]!;
  return req;
}

/** What a space's header says it will save: "New: tidiness 4, cleanliness 3.5". */
function picksText(draft: Draft): string {
  if (draft.same) return "Same as last time";
  const picked = HOME_PARTS.filter((p) => draft.parts[p] != null).map((p) => `${p} ${draft.parts[p]}`);
  if (picked.length > 0) return `New: ${picked.join(", ")}`;
  return draft.note.trim() ? "A note, but no rating yet" : "";
}

/**
 * The walk-through (HOME-2, environment plan §4.8): a bottom sheet on a phone, a side panel on
 * a desktop. Every space, the longest since rated first, then the one that costs the score
 * most: three of the Food Log's half-point pickers with HOME-2's examples, Same as last time,
 * and an optional note. One Save writes every space touched in one POST; a space left alone
 * isn't sent. Opened on one space (`only`, a card's Rate), it rates that space alone, with
 * Tidiness and Cleanliness first and Aesthetics, which changes slowly, a tap away.
 */
export function WalkThroughSheet({
  open,
  onOpenChange,
  spaces,
  today,
  only,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  spaces: HomeSpaceRow[];
  today: string;
  /** A space's id: rate it alone. Null: the whole walk-through. */
  only: string | null;
}) {
  const desktop = useIsDesktop();
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side={desktop ? "right" : "bottom"}
        className={cn("gap-0", desktop ? "w-full sm:max-w-md" : "max-h-[92dvh] rounded-t-2xl")}
      >
        {open ? (
          <WalkThroughForm key={only ?? "all"} spaces={spaces} today={today} only={only} onDone={() => onOpenChange(false)} />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

function WalkThroughForm({
  spaces,
  today,
  only,
  onDone,
}: {
  spaces: HomeSpaceRow[];
  today: string;
  only: string | null;
  onDone: () => void;
}) {
  const ids = useId();
  const writes = useWrites();
  // Sorted once, when the sheet opens, so a space doesn't move while it's being rated.
  const [order] = useState(() =>
    only ? spaces.filter((s) => s.id === only) : walkThroughOrder(spaces, today),
  );
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [expanded, setExpanded] = useState<string | null>(order[0]?.id ?? null);
  const [day, setDay] = useState(today);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const single = only != null;
  const draftOf = (id: string) => drafts[id] ?? EMPTY;
  const change = (id: string, next: Partial<Draft>) => {
    setError(null);
    setDrafts((all) => ({ ...all, [id]: { ...(all[id] ?? EMPTY), ...next } }));
  };

  const ready = useMemo(() => order.filter((s) => touched(drafts[s.id] ?? EMPTY)), [order, drafts]);
  const dayProblem = !day ? "Pick the day you rated." : day > today ? "A rating is for today or earlier." : null;

  async function submit() {
    if (ready.length === 0 || pending || dayProblem) return;
    setPending(true);
    setError(null);
    try {
      await writes.rateSpaces(
        ready.map((s) => request(s, draftOf(s.id))),
        day === today ? undefined : day,
      );
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  const name = order[0]?.name ?? "the space";

  return (
    <form
      className="flex min-h-0 flex-1 flex-col"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <SheetHeader className="pb-2">
        <SheetTitle>{single ? `Rate ${name}` : "Walk-through"}</SheetTitle>
        <SheetDescription>
          {single
            ? "A quick check: rate what you see now. Parts you leave out keep their last rating."
            : "Rate each space as you walk through. Spaces you leave alone aren't saved, and keep fading from their last rating."}
        </SheetDescription>
      </SheetHeader>

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 pb-4">
        <div className="flex items-center gap-3">
          <Label htmlFor={`${ids}-day`} className="shrink-0">
            Day
          </Label>
          <Input
            id={`${ids}-day`}
            type="date"
            max={today}
            value={day}
            disabled={pending}
            aria-describedby={dayProblem ? `${ids}-day-problem` : undefined}
            onChange={(e) => {
              setError(null);
              setDay(e.target.value);
            }}
            className="w-auto"
          />
        </div>
        {dayProblem ? (
          <p id={`${ids}-day-problem`} className="text-xs text-muted-foreground" aria-live="polite">
            {dayProblem}
          </p>
        ) : null}

        {single ? (
          order[0] ? (
            <SpaceFields
              space={order[0]}
              draft={draftOf(order[0].id)}
              today={today}
              name={`${ids}-${order[0].id}`}
              pending={pending}
              quickCheck
              onChange={(next) => change(order[0].id, next)}
            />
          ) : (
            <p className="text-sm text-muted-foreground">That space isn't in the home any more.</p>
          )
        ) : (
          <ul className="space-y-2" aria-label="Spaces">
            {order.map((space) => {
              const draft = draftOf(space.id);
              const isOpen = expanded === space.id;
              const panelId = `${ids}-${space.id}-panel`;
              const picks = picksText(draft);
              return (
                <li key={space.id} className="rounded-xl ring-1 ring-foreground/10" data-testid="walk-through-space">
                  <h3>
                    <button
                      type="button"
                      aria-expanded={isOpen}
                      aria-controls={panelId}
                      onClick={() => setExpanded(isOpen ? null : space.id)}
                      className="flex min-h-11 w-full items-center justify-between gap-2 rounded-xl px-3 py-2 text-left focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
                    >
                      <span className="min-w-0">
                        <span className="block font-medium">{space.name}</span>
                        <span className="block text-xs text-muted-foreground">
                          {picks || ratedAgo(space.lastRatedOn, today)}
                        </span>
                      </span>
                      <ChevronDownIcon
                        aria-hidden
                        className={cn("size-4 shrink-0 transition-transform", isOpen && "rotate-180")}
                      />
                    </button>
                  </h3>
                  {isOpen ? (
                    <div id={panelId} className="px-3 pb-3">
                      <SpaceFields
                        space={space}
                        draft={draft}
                        today={today}
                        name={`${ids}-${space.id}`}
                        pending={pending}
                        onChange={(next) => change(space.id, next)}
                      />
                    </div>
                  ) : null}
                </li>
              );
            })}
          </ul>
        )}

        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
      </div>

      <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t px-4 py-3">
        <p className="text-xs text-muted-foreground" aria-live="polite">
          {single ? "" : ready.length === 0 ? "Nothing rated yet" : `${ready.length} of ${order.length} spaces rated`}
        </p>
        <div className="flex gap-2">
          <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={onDone}>
            Cancel
          </Button>
          <Button type="submit" className="min-h-11 md:min-h-0" disabled={ready.length === 0 || pending || !!dayProblem}>
            {pending ? "Saving…" : "Save"}
          </Button>
        </div>
      </div>
    </form>
  );
}

/** One space's pickers, Same as last time and note. */
function SpaceFields({
  space,
  draft,
  today,
  name,
  pending,
  quickCheck = false,
  onChange,
}: {
  space: HomeSpaceRow;
  draft: Draft;
  today: string;
  name: string;
  pending: boolean;
  /** A card's Rate: Aesthetics folds away until asked for. */
  quickCheck?: boolean;
  onChange: (next: Partial<Draft>) => void;
}) {
  const [showAesthetics, setShowAesthetics] = useState(!quickCheck || draft.parts.aesthetics != null);
  const rated = space.lastRatedOn != null;
  const parts = HOME_PARTS.filter((p) => p !== "aesthetics" || showAesthetics);

  return (
    <div className="space-y-4">
      {parts.map((part) => {
        const last = space.parts[part];
        const value = draft.same ? last.rated : draft.parts[part];
        return (
          <fieldset key={part} className="space-y-1.5" disabled={pending || draft.same}>
            <legend className="text-sm font-medium">
              {PART_LABEL[part]}
              {last.rated != null ? (
                <span className="font-normal text-muted-foreground">
                  {" "}
                  · last {last.rated}, {ratedAgo(last.ratedOn, today).replace(/^rated /, "")}
                </span>
              ) : null}
            </legend>
            <HalfPointPicker
              name={`${name}-${part}`}
              value={value}
              onChange={(v) => onChange({ parts: { ...draft.parts, [part]: v } })}
              labelFor={(v) => ratingLabel(part, v)}
              wordFor={(v) => ratingWord(part, v)}
              dimmed={draft.same}
            />
            <p className="text-xs text-muted-foreground" aria-live="polite">
              {ratingHelp(part, value)}
            </p>
          </fieldset>
        );
      })}

      {!showAesthetics ? (
        <Button type="button" variant="outline" size="sm" disabled={pending} onClick={() => setShowAesthetics(true)}>
          Rate Aesthetics too
        </Button>
      ) : null}

      <div className="flex items-start gap-3 rounded-lg bg-muted/60 px-3 py-2.5">
        <Switch
          id={`${name}-same`}
          checked={draft.same}
          disabled={pending || !rated}
          onCheckedChange={(same) => onChange({ same, parts: EMPTY.parts })}
          aria-describedby={`${name}-same-help`}
          className="mt-0.5"
        />
        <div className="space-y-0.5">
          <Label htmlFor={`${name}-same`} className="font-normal">
            Same as last time
          </Label>
          <p id={`${name}-same-help`} className="text-xs text-muted-foreground">
            {rated
              ? "Repeats each part's last rating, so its fade starts over."
              : "Not rated yet, so there's nothing to repeat."}
          </p>
        </div>
      </div>

      <div className="space-y-1.5">
        <Label htmlFor={`${name}-note`}>
          Note <span className="font-normal text-muted-foreground">(optional)</span>
        </Label>
        <Input
          id={`${name}-note`}
          value={draft.note}
          maxLength={NOTE_MAX}
          autoComplete="off"
          placeholder="Cleared the desk"
          disabled={pending}
          onChange={(e) => onChange({ note: e.target.value })}
        />
      </div>

      {touched(draft) ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={pending}
          onClick={() => onChange({ ...EMPTY })}
        >
          Clear {space.name}
        </Button>
      ) : null}
    </div>
  );
}
