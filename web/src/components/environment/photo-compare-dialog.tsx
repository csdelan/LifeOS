import { useId, useMemo, useState } from "react";
import { toast } from "sonner";
import { MealPhoto } from "@/components/food/meal-photo";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { CompareSide } from "@/components/environment/then-and-now";
import { formatDay } from "@/lib/dates";
import {
  HOME_PARTS,
  PART_LABEL,
  photoCaption,
  SHOT_NAME_MAX,
  SHOTS_MAX,
  shotTitle,
  shownScore,
} from "@/lib/environment";
import type { HomeSpaceRow, SpacePhotoRow } from "@/lib/production-ui-types";
import { useSpacePhotos, useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

/** The native select's look, as the Food Log's meal type. */
export const SELECT_CLASS =
  "h-9 w-full rounded-lg border border-input bg-transparent px-2.5 text-sm focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 disabled:opacity-50 dark:bg-input/30";

type Side = "left" | "right";

/** A photo's short name in the filmstrip and its buttons: "Oct 5 · 37.5 · baseline". */
function photoName(photo: SpacePhotoRow): string {
  const score = photo.score != null ? ` · ${shownScore(photo.score)}` : "";
  return `${formatDay(photo.day)}${score}${photo.baseline ? " · baseline" : ""}`;
}

/**
 * The compare dialog (HOME-4, environment plan §8.3): a shot's two photos side by side (stacked
 * on a phone), Then on the left and Now on the right to start, each with its day and the
 * space's score and parts that day. Tapping a side picks it; the filmstrip below, oldest
 * first, puts any of the shot's photos there. The picked side's photo can be made the
 * baseline, moved to another day or shot, made its own shot (a photo from another spot), or
 * removed.
 */
export function PhotoCompareDialog({
  open,
  onOpenChange,
  space,
  shotId,
  start,
  today,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  space: HomeSpaceRow;
  shotId: string;
  start: CompareSide;
  today: string;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[94dvh] gap-3 overflow-y-auto sm:max-w-[min(94vw,1100px)]">
        {open ? (
          <CompareBody key={`${shotId}-${start}`} space={space} shotId={shotId} start={start} today={today} />
        ) : null}
      </DialogContent>
    </Dialog>
  );
}

function CompareBody({
  space,
  shotId,
  start,
  today,
}: {
  space: HomeSpaceRow;
  shotId: string;
  start: CompareSide;
  today: string;
}) {
  const ids = useId();
  const writes = useWrites();
  const read = useSpacePhotos(space.id, shotId);
  const shots = read.data?.shots ?? space.shots;
  const shot = shots.find((s) => s.id === shotId);
  // Oldest first, as the filmstrip runs.
  const photos = useMemo(() => [...(read.data?.photos ?? [])].reverse(), [read.data]);
  const [picked, setPicked] = useState<Record<Side, string | null>>({ left: null, right: null });
  const [active, setActive] = useState<Side>(start === "then" ? "left" : "right");
  const [changing, setChanging] = useState(false);
  const [owning, setOwning] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // A side shows the photo picked for it while it's still the shot's, else Then or Now.
  const then = photos.find((p) => p.baseline) ?? shot?.baseline ?? null;
  const now = photos.at(-1) ?? shot?.newest ?? null;
  const find = (id: string | null) => (id ? photos.find((p) => p.id === id) : undefined);
  const shown: Record<Side, SpacePhotoRow | null> = {
    left: find(picked.left) ?? then,
    right: find(picked.right) ?? now,
  };
  const focus = shown[active];
  const title = shotTitle(space.name, shot?.name ?? space.name);
  const full = shots.length >= SHOTS_MAX;

  const label = (photo: SpacePhotoRow) =>
    photo.id === then?.id
      ? photoCaption("Then", photo)
      : photo.id === now?.id
        ? photoCaption("Now", photo)
        : photoName(photo);

  /** Only one of Change day, Make this its own shot and Remove photo is open at a time. */
  function closeForms() {
    setChanging(false);
    setOwning(false);
    setConfirmRemove(false);
    setError(null);
  }

  function pickSide(side: Side) {
    setActive(side);
    closeForms();
  }

  /**
   * A photo from another spot gets its own shot: a new shot, then the photo moved to it, where
   * it's the first photo and so the baseline. Two writes, announced as one.
   */
  async function ownShot(photo: SpacePhotoRow, name: string) {
    const before = new Set(shots.map((s) => s.id));
    const added = await writes.addShot(space.id, name, { quiet: true });
    const fresh = added.shots.find((s) => !before.has(s.id));
    if (!fresh) throw new Error("The new shot didn't come back, so the photo wasn't moved.");
    try {
      await writes.changeSpacePhoto(photo.id, { shot: fresh.id }, { quiet: true });
    } catch (e) {
      const why = e instanceof Error ? e.message : "it didn't save.";
      throw new Error(
        `Added the shot ${fresh.name}, but the photo didn't move: ${why} Move it with Change day or shot.`,
      );
    }
    toast.success(`Moved to its own shot, ${shotTitle(space.name, fresh.name)}`);
  }

  async function run(write: () => Promise<unknown>, after?: () => void) {
    setBusy(true);
    setError(null);
    try {
      await write();
      after?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <DialogHeader className="pr-8">
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>Then and Now side by side. Pick a side, then a photo below to put there.</DialogDescription>
      </DialogHeader>

      {shown.left && shown.right ? (
        <div className="grid gap-3 sm:grid-cols-2">
          {(["left", "right"] as const).map((side) => {
            const photo = shown[side]!;
            const caption = label(photo);
            return (
              <figure
                key={side}
                className={cn(
                  "min-w-0 space-y-1.5 rounded-xl p-1.5",
                  active === side ? "ring-2 ring-primary" : "ring-1 ring-foreground/10",
                )}
                data-testid={`compare-${side}`}
              >
                <button
                  type="button"
                  aria-pressed={active === side}
                  aria-label={`${side === "left" ? "Left" : "Right"} side: ${caption}`}
                  onClick={() => pickSide(side)}
                  className="block w-full overflow-hidden rounded-lg focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
                >
                  <MealPhoto
                    artifactId={photo.artifactId}
                    alt=""
                    className="aspect-[4/3] max-h-[45dvh] w-full bg-muted object-contain"
                  />
                </button>
                <figcaption className="space-y-0.5 px-1">
                  <span className="block text-sm font-medium tabular-nums">{caption}</span>
                  <span className="block text-xs text-muted-foreground tabular-nums">
                    {HOME_PARTS.map((p) => `${PART_LABEL[p]} ${shownScore(photo.parts[p].now)}`).join(" · ")}
                  </span>
                </figcaption>
              </figure>
            );
          })}
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          {read.isLoading ? "Loading the photos…" : "No photos of this shot left."}
        </p>
      )}

      {photos.length > 0 ? (
        <div className="space-y-1">
          <p id={`${ids}-strip`} className="text-xs text-muted-foreground">
            {`Every photo of ${title}, oldest first: pick one for the ${active} side.`}
          </p>
          <ul aria-labelledby={`${ids}-strip`} className="flex gap-2 overflow-x-auto pb-1" data-testid="filmstrip">
            {photos.map((p) => {
              const on = focus?.id === p.id;
              return (
                <li key={p.id} className="shrink-0">
                  <button
                    type="button"
                    aria-pressed={on}
                    aria-label={photoName(p)}
                    onClick={() => {
                      setPicked((all) => ({ ...all, [active]: p.id }));
                      closeForms();
                    }}
                    className={cn(
                      "block w-20 overflow-hidden rounded-md text-left focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none",
                      on ? "ring-2 ring-primary" : "ring-1 ring-foreground/10",
                    )}
                  >
                    <MealPhoto artifactId={p.artifactId} alt="" className="aspect-[4/3] w-full" />
                    <span aria-hidden className="block truncate px-1 py-0.5 text-[0.65rem] tabular-nums">
                      {formatDay(p.day)}
                      {p.baseline ? " · base" : ""}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        </div>
      ) : null}

      {focus ? (
        <div className="space-y-2 border-t pt-3" role="group" aria-label={`The ${active} side's photo`}>
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="min-h-11 md:min-h-0"
              disabled={busy || focus.baseline}
              onClick={() => void run(() => writes.setSpaceBaseline(focus.id))}
            >
              {focus.baseline ? "This is the baseline" : "Make this the baseline"}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              className="min-h-11 md:min-h-0"
              disabled={busy}
              aria-expanded={changing}
              onClick={() => {
                const open = !changing;
                closeForms();
                setChanging(open);
              }}
            >
              {shots.length > 1 ? "Change day or shot" : "Change day"}
            </Button>
            {photos.length > 1 ? (
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="min-h-11 md:min-h-0"
                disabled={busy || full}
                aria-expanded={owning}
                aria-describedby={full ? `${ids}-full` : undefined}
                onClick={() => {
                  const open = !owning;
                  closeForms();
                  setOwning(open);
                }}
              >
                Make this its own shot
              </Button>
            ) : null}
            <Button
              type="button"
              variant="ghost"
              size="sm"
              className="min-h-11 md:min-h-0"
              disabled={busy}
              onClick={() => {
                closeForms();
                setConfirmRemove(true);
              }}
            >
              Remove photo
            </Button>
          </div>

          {full && photos.length > 1 ? (
            <p id={`${ids}-full`} className="text-xs text-muted-foreground">
              {space.name} has {SHOTS_MAX} shots, the most a space can have: remove one in Manage spaces to make
              another.
            </p>
          ) : null}

          {owning ? (
            <OwnShotForm
              key={focus.id}
              from={title}
              busy={busy}
              onCancel={() => setOwning(false)}
              onSave={(name) =>
                void run(
                  () => ownShot(focus, name),
                  () => setOwning(false),
                )
              }
            />
          ) : null}

          {changing ? (
            <ChangeForm
              key={focus.id}
              photo={focus}
              shots={shots.map((s) => ({ id: s.id, name: s.name }))}
              today={today}
              busy={busy}
              onCancel={() => setChanging(false)}
              onSave={(req) =>
                void run(
                  () => writes.changeSpacePhoto(focus.id, req),
                  () => setChanging(false),
                )
              }
            />
          ) : null}

          {confirmRemove ? (
            <div className="flex flex-wrap items-center gap-2 rounded-lg px-3 py-2 ring-1 ring-foreground/15">
              <p className="text-sm">Remove the photo from {formatDay(focus.day)}? Its file stays in the record.</p>
              <Button
                type="button"
                variant="destructive"
                size="sm"
                className="min-h-11 md:min-h-0 dark:bg-destructive/10"
                disabled={busy}
                onClick={() =>
                  void run(
                    () => writes.removeSpacePhoto(focus.id),
                    () => setConfirmRemove(false),
                  )
                }
              >
                Remove
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="min-h-11 md:min-h-0"
                disabled={busy}
                onClick={() => setConfirmRemove(false)}
              >
                Keep it
              </Button>
            </div>
          ) : null}

          {error ? (
            <p role="alert" className="text-sm text-destructive">
              {error}
            </p>
          ) : null}
        </div>
      ) : null}
    </>
  );
}

/** The new shot's name, for a photo from another spot. */
function OwnShotForm({
  from,
  busy,
  onCancel,
  onSave,
}: {
  /** The shot the photo leaves: "Kitchen". */
  from: string;
  busy: boolean;
  onCancel: () => void;
  onSave: (name: string) => void;
}) {
  const ids = useId();
  const [name, setName] = useState("");
  return (
    <form
      className="flex flex-wrap items-end gap-3 rounded-lg bg-muted/60 p-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (name.trim()) onSave(name);
      }}
    >
      <div className="min-w-40 flex-1 space-y-1.5">
        <Label htmlFor={`${ids}-name`}>Name the new shot</Label>
        <Input
          id={`${ids}-name`}
          value={name}
          maxLength={SHOT_NAME_MAX}
          autoComplete="off"
          placeholder="Sink"
          disabled={busy}
          aria-describedby={`${ids}-help`}
          onChange={(e) => setName(e.target.value)}
        />
      </div>
      <div className="flex gap-2">
        <Button type="submit" size="sm" className="min-h-11 md:min-h-0" disabled={busy || !name.trim()}>
          {busy ? "Moving…" : "Make shot"}
        </Button>
        <Button type="button" variant="outline" size="sm" className="min-h-11 md:min-h-0" onClick={onCancel}>
          Cancel
        </Button>
      </div>
      <p id={`${ids}-help`} className="w-full text-xs text-muted-foreground">
        For a photo taken from a different spot. It leaves {from} for the new shot, as that shot's baseline.
      </p>
    </form>
  );
}

/** A photo's new day and shot; only what changed is sent. */
function ChangeForm({
  photo,
  shots,
  today,
  busy,
  onCancel,
  onSave,
}: {
  photo: SpacePhotoRow;
  shots: { id: string; name: string }[];
  today: string;
  busy: boolean;
  onCancel: () => void;
  onSave: (req: { day?: string; shot?: string }) => void;
}) {
  const ids = useId();
  const [day, setDay] = useState(photo.day);
  const [shot, setShot] = useState(photo.shotId);
  const problem = !day ? "Pick the day it was taken." : day > today ? "A photo is for today or earlier." : null;
  const unchanged = day === photo.day && shot === photo.shotId;
  return (
    <form
      className="flex flex-wrap items-end gap-3 rounded-lg bg-muted/60 p-3"
      onSubmit={(e) => {
        e.preventDefault();
        if (problem || unchanged) return;
        onSave({ day: day !== photo.day ? day : undefined, shot: shot !== photo.shotId ? shot : undefined });
      }}
    >
      <div className="space-y-1.5">
        <Label htmlFor={`${ids}-day`}>Day</Label>
        <Input
          id={`${ids}-day`}
          type="date"
          max={today}
          value={day}
          disabled={busy}
          onChange={(e) => setDay(e.target.value)}
          className="w-auto"
        />
      </div>
      {shots.length > 1 ? (
        <div className="min-w-40 space-y-1.5">
          <Label htmlFor={`${ids}-shot`}>Shot</Label>
          <select
            id={`${ids}-shot`}
            value={shot}
            disabled={busy}
            onChange={(e) => setShot(e.target.value)}
            className={SELECT_CLASS}
          >
            {shots.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
        </div>
      ) : null}
      <div className="flex gap-2">
        <Button type="submit" size="sm" className="min-h-11 md:min-h-0" disabled={busy || !!problem || unchanged}>
          {busy ? "Saving…" : "Save"}
        </Button>
        <Button type="button" variant="outline" size="sm" className="min-h-11 md:min-h-0" onClick={onCancel}>
          Cancel
        </Button>
      </div>
      {problem ? <p className="w-full text-xs text-muted-foreground">{problem}</p> : null}
    </form>
  );
}
