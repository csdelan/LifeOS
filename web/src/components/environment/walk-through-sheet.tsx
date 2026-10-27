import { useEffect, useId, useMemo, useRef, useState } from "react";
import { CameraIcon, ChevronDownIcon, ImageIcon } from "lucide-react";
import { toast } from "sonner";
import { MealPhoto } from "@/components/food/meal-photo";
import { HalfPointPicker } from "@/components/primitives/half-point-picker";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { Switch } from "@/components/ui/switch";
import { formatDay } from "@/lib/dates";
import {
  addedMessage,
  HOME_PARTS,
  NOTE_MAX,
  PART_LABEL,
  ratedAgo,
  ratingHelp,
  ratingLabel,
  ratingWord,
  shotTitle,
  walkThroughOrder,
} from "@/lib/environment";
import { useIsDesktop } from "@/lib/media-query";
import { preparePhoto, type PreparedPhoto } from "@/lib/photo";
import type { HomePart, HomeSpaceRow, SpaceRatingRequest, SpaceShotRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

/** A photo taken or chosen in the sheet, shrunk and held until Save. */
interface HeldPhoto extends PreparedPhoto {
  url: string;
}

/** One space's picks in the sheet: none of them sent until touched. */
interface Draft {
  parts: Record<HomePart, number | null>;
  same: boolean;
  note: string;
  /** A new photo for each shot, by the shot's id (HOME-4). */
  photos: Record<string, HeldPhoto>;
}

const EMPTY: Draft = {
  parts: { tidiness: null, cleanliness: null, aesthetics: null },
  same: false,
  note: "",
  photos: {},
};

/** Whether a space has a rating to send: a part, Same as last time or a note. */
function touched(draft: Draft): boolean {
  return draft.same || HOME_PARTS.some((p) => draft.parts[p] != null) || draft.note.trim() !== "";
}

function photoCount(draft: Draft): number {
  return Object.keys(draft.photos).length;
}

/** "1 photo", "2 photos". */
function photosText(n: number): string {
  return n === 1 ? "1 photo" : `${n} photos`;
}

function request(space: HomeSpaceRow, draft: Draft): SpaceRatingRequest {
  const note = draft.note.trim() || undefined;
  if (draft.same) return { space: space.id, same: true, note };
  const req: SpaceRatingRequest = { space: space.id, note };
  for (const p of HOME_PARTS) if (draft.parts[p] != null) req[p] = draft.parts[p]!;
  return req;
}

/** What a space's header says it will save: "New: tidiness 4, cleanliness 3.5 · 1 photo". */
function picksText(draft: Draft): string {
  const photos = photoCount(draft) > 0 ? photosText(photoCount(draft)) : "";
  const rating = (() => {
    if (draft.same) return "Same as last time";
    const picked = HOME_PARTS.filter((p) => draft.parts[p] != null).map((p) => `${p} ${draft.parts[p]}`);
    if (picked.length > 0) return `New: ${picked.join(", ")}`;
    return draft.note.trim() ? "A note, but no rating yet" : "";
  })();
  return [rating, photos].filter(Boolean).join(" · ");
}

/**
 * The walk-through (HOME-2, HOME-4, environment plan §4.8, §8.3): a bottom sheet on a phone, a
 * side panel on a desktop. Every space, the longest since rated first, then the one that costs
 * the score most: three of the Food Log's half-point pickers with HOME-2's examples, Same as
 * last time, an optional note, and a photo for each shot, beside its baseline to line it up.
 * One Save writes every space rated in one POST, then adds the photos, each dated from when it
 * was taken (else the sheet's day); a space left alone isn't sent. Opened on one space (`only`,
 * a card's Rate), it rates that space alone, with Tidiness and Cleanliness first and
 * Aesthetics, which changes slowly, a tap away.
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
          <WalkThroughForm
            key={only ?? "all"}
            spaces={spaces}
            today={today}
            only={only}
            camera={!desktop}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

function WalkThroughForm({
  spaces,
  today,
  only,
  camera,
  onDone,
}: {
  spaces: HomeSpaceRow[];
  today: string;
  only: string | null;
  /** Offer Take photo (a phone); a desktop has Choose photo only. */
  camera: boolean;
  onDone: () => void;
}) {
  const ids = useId();
  const writes = useWrites();
  // Sorted once, when the sheet opens, so a space doesn't move while it's being rated.
  const [order] = useState(() => (only ? spaces.filter((s) => s.id === only) : walkThroughOrder(spaces, today)));
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [expanded, setExpanded] = useState<string | null>(order[0]?.id ?? null);
  const [day, setDay] = useState(today);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // Every held photo's object URL, let go when the sheet closes.
  const urls = useRef(new Set<string>());
  useEffect(() => {
    const held = urls.current;
    return () => held.forEach((u) => URL.revokeObjectURL(u));
  }, []);

  const single = only != null;
  const draftOf = (id: string) => drafts[id] ?? EMPTY;
  const change = (id: string, next: Partial<Draft>) => {
    setError(null);
    setDrafts((all) => ({ ...all, [id]: { ...(all[id] ?? EMPTY), ...next } }));
  };
  const holdPhoto = (spaceId: string, shotId: string, photo: PreparedPhoto | null) => {
    setError(null);
    const held = photo ? { ...photo, url: URL.createObjectURL(photo.blob) } : null;
    if (held) urls.current.add(held.url);
    setDrafts((all) => {
      const draft = all[spaceId] ?? EMPTY;
      const photos = { ...draft.photos };
      if (held) photos[shotId] = held;
      else delete photos[shotId];
      return { ...all, [spaceId]: { ...draft, photos } };
    });
  };

  const ready = useMemo(
    () => order.filter((s) => touched(drafts[s.id] ?? EMPTY) || photoCount(drafts[s.id] ?? EMPTY) > 0),
    [order, drafts],
  );
  const rated = ready.filter((s) => touched(draftOf(s.id)));
  const photos = ready.reduce((n, s) => n + photoCount(draftOf(s.id)), 0);
  const dayProblem = !day ? "Pick the day you rated." : day > today ? "A rating is for today or earlier." : null;

  async function submit() {
    if (ready.length === 0 || pending || dayProblem) return;
    setPending(true);
    setError(null);
    try {
      if (rated.length > 0) {
        await writes.rateSpaces(
          rated.map((s) => request(s, draftOf(s.id))),
          day === today ? undefined : day,
        );
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
      setPending(false);
      return;
    }
    // The ratings are in; now the photos, one by one, each on the day it was taken.
    let added = 0;
    let why: string | null = null;
    for (const space of ready) {
      for (const [shot, photo] of Object.entries(draftOf(space.id).photos)) {
        try {
          const on = photo.takenOn && photo.takenOn <= today ? photo.takenOn : day;
          await writes.addSpacePhoto(space.id, photo.blob, {
            day: on,
            shot,
            filename: photo.name,
          });
          added += 1;
        } catch (e) {
          why ??= e instanceof Error ? e.message : "it didn't upload.";
        }
      }
    }
    setPending(false);
    const missed = photos - added;
    if (missed > 0 && rated.length === 0 && added === 0) {
      setError(why ?? "The photos didn't save.");
      return;
    }
    if (added > 0)
      toast.success(added === 1 && ready.length === 1 ? addedMessage(1, ready[0].name) : `Added ${photosText(added)}`);
    if (missed > 0) {
      toast.error(
        `${photosText(missed)} didn't save: ${why} Add ${missed === 1 ? "it" : "them"} from the space's card.`,
      );
    }
    onDone();
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
              camera={camera}
              onChange={(next) => change(order[0].id, next)}
              onPhoto={(shot, photo) => holdPhoto(order[0].id, shot, photo)}
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
                        camera={camera}
                        onChange={(next) => change(space.id, next)}
                        onPhoto={(shot, photo) => holdPhoto(space.id, shot, photo)}
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
          {[
            single ? "" : rated.length === 0 ? "Nothing rated yet" : `${rated.length} of ${order.length} spaces rated`,
            photos > 0 ? `${photosText(photos)} to add` : "",
          ]
            .filter(Boolean)
            .join(" · ")}
        </p>
        <div className="flex gap-2">
          <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={onDone}>
            Cancel
          </Button>
          <Button
            type="submit"
            className="min-h-11 md:min-h-0"
            disabled={ready.length === 0 || pending || !!dayProblem}
          >
            {pending ? "Saving…" : "Save"}
          </Button>
        </div>
      </div>
    </form>
  );
}

/** One space's pickers, Same as last time, note, and a photo for each shot. */
function SpaceFields({
  space,
  draft,
  today,
  name,
  pending,
  quickCheck = false,
  camera,
  onChange,
  onPhoto,
}: {
  space: HomeSpaceRow;
  draft: Draft;
  today: string;
  name: string;
  pending: boolean;
  /** A card's Rate: Aesthetics folds away until asked for. */
  quickCheck?: boolean;
  camera: boolean;
  onChange: (next: Partial<Draft>) => void;
  onPhoto: (shot: string, photo: PreparedPhoto | null) => void;
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

      <div role="group" aria-labelledby={`${name}-photos`} className="space-y-2">
        <p id={`${name}-photos`} className="text-sm font-medium">
          Photos <span className="font-normal text-muted-foreground">(optional)</span>
        </p>
        {space.shots.map((shot) => (
          <ShotPhoto
            key={shot.id}
            space={space.name}
            shot={shot}
            named={space.shots.length > 1}
            held={draft.photos[shot.id] ?? null}
            camera={camera}
            disabled={pending}
            onPhoto={(photo) => onPhoto(shot.id, photo)}
          />
        ))}
      </div>

      {touched(draft) ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={pending}
          // The rating's picks only: photos have their own Leave out.
          onClick={() => onChange({ parts: EMPTY.parts, same: false, note: "" })}
        >
          Clear {space.name}
        </Button>
      ) : null}
    </div>
  );
}

/**
 * A shot's photo in the walk-through (HOME-4): its baseline as a thumbnail to line the new one
 * up with, Take photo (a phone) and Choose photo. The photo is shrunk and held until Save.
 */
function ShotPhoto({
  space,
  shot,
  named,
  held,
  camera,
  disabled,
  onPhoto,
}: {
  space: string;
  shot: SpaceShotRow;
  /** Name the shot: the space has more than one. */
  named: boolean;
  held: HeldPhoto | null;
  camera: boolean;
  disabled: boolean;
  onPhoto: (photo: PreparedPhoto | null) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const cameraInput = useRef<HTMLInputElement>(null);
  const libraryInput = useRef<HTMLInputElement>(null);
  const title = shotTitle(space, shot.name);

  async function pick(file: File | undefined) {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      onPhoto(await preparePhoto(file));
    } catch (e) {
      setError(e instanceof Error ? e.message : "Couldn't prepare that photo.");
    } finally {
      setBusy(false);
    }
  }

  const status = busy
    ? "Preparing the photo…"
    : held
      ? held.takenOn
        ? `New photo, taken ${formatDay(held.takenOn)}.`
        : "New photo, dated with the walk-through."
      : shot.baseline
        ? `Line the new photo up with Then, ${formatDay(shot.baseline.day)}.`
        : "No photo yet: the first becomes the baseline.";

  return (
    <div className="flex items-start gap-3 rounded-lg bg-muted/40 p-2" data-testid="shot-photo">
      <div className="flex shrink-0 gap-1.5">
        {shot.baseline ? (
          <figure className="w-16 space-y-0.5">
            <MealPhoto
              artifactId={shot.baseline.artifactId}
              alt={`${title}'s baseline, ${formatDay(shot.baseline.day)}`}
              className="aspect-[4/3] w-16 rounded-md"
            />
            <figcaption className="text-[0.65rem] text-muted-foreground">Then</figcaption>
          </figure>
        ) : null}
        {held ? (
          <figure className="w-16 space-y-0.5">
            <img src={held.url} alt={`New photo of ${title}`} className="aspect-[4/3] w-16 rounded-md object-cover" />
            <figcaption className="text-[0.65rem] text-muted-foreground">New</figcaption>
          </figure>
        ) : null}
      </div>
      <div className="min-w-0 flex-1 space-y-1.5">
        {named ? <p className="truncate text-sm font-medium">{shot.name}</p> : null}
        <p className="text-xs text-muted-foreground" aria-live="polite">
          {status}
        </p>
        <div className="flex flex-wrap gap-1.5">
          {camera ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="min-h-9 md:min-h-0"
              disabled={disabled || busy}
              aria-label={`Take photo of ${title}`}
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
            disabled={disabled || busy}
            aria-label={`${held ? "Choose another photo" : "Choose photo"} of ${title}`}
            onClick={() => libraryInput.current?.click()}
          >
            <ImageIcon />
            {held ? "Choose another" : "Choose photo"}
          </Button>
          {held ? (
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="min-h-9 md:min-h-0"
              disabled={disabled || busy}
              aria-label={`Leave out the new photo of ${title}`}
              onClick={() => onPhoto(null)}
            >
              Leave out
            </Button>
          ) : null}
        </div>
        {error ? (
          <p role="alert" className="text-xs text-destructive">
            {error}
          </p>
        ) : null}
      </div>
      {/* Hidden: the buttons above open them. capture asks a phone for its rear camera. */}
      <input
        ref={cameraInput}
        type="file"
        accept="image/*"
        capture="environment"
        hidden
        onChange={(e) => {
          void pick(e.target.files?.[0]);
          e.target.value = "";
        }}
      />
      <input
        ref={libraryInput}
        type="file"
        accept="image/*"
        hidden
        onChange={(e) => {
          void pick(e.target.files?.[0]);
          e.target.value = "";
        }}
      />
    </div>
  );
}
