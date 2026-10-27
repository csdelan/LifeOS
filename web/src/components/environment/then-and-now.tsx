import { ImagePlusIcon } from "lucide-react";
import { MealPhoto } from "@/components/food/meal-photo";
import { firstShot, photoAgo, photoCaption, shotTitle } from "@/lib/environment";
import type { HomeSpaceRow, SpacePhotoRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/** Which side of the compare dialog a tap opens on. */
export type CompareSide = "then" | "now";

/** One photo on the card, its caption under it; tapping it opens the compare dialog. */
function PhotoTile({
  photo,
  label,
  title,
  onClick,
}: {
  photo: SpacePhotoRow;
  label: string;
  title: string;
  onClick: () => void;
}) {
  const caption = photoCaption(label, photo);
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={`Compare ${title}: ${caption}`}
      className="min-w-0 space-y-1 rounded-lg text-left focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
      data-testid={`photo-${label.toLowerCase()}`}
    >
      <MealPhoto
        artifactId={photo.artifactId}
        alt=""
        className="aspect-[4/3] w-full rounded-lg ring-1 ring-foreground/10"
      />
      <span aria-hidden className="block text-xs leading-snug text-muted-foreground tabular-nums">
        {caption}
      </span>
    </button>
  );
}

/** A dashed box that adds photos: where Now goes while a shot has only its baseline. */
function AddTile({
  label,
  ariaLabel,
  wide,
  onClick,
}: {
  label: string;
  ariaLabel: string;
  wide?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={ariaLabel}
      className={cn(
        "flex min-h-20 flex-col items-center justify-center gap-1 rounded-lg border border-dashed border-foreground/25 px-2 text-center text-xs text-muted-foreground hover:bg-muted/60 focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none",
        wide ? "col-span-2" : "aspect-[4/3]",
      )}
    >
      <ImagePlusIcon aria-hidden className="size-4" />
      {label}
    </button>
  );
}

/**
 * A space card's photos (HOME-4, environment plan §8.1): one shot's Then (its baseline) beside
 * its Now (its newest photo), each with its day and the space's score that day. With one photo
 * it shows alone, with room to add another to compare; with none, a box to add the first.
 * A space with more than one shot picks which shows here.
 */
export function ThenAndNow({
  space,
  today,
  shotId,
  onShot,
  onCompare,
  onAdd,
}: {
  space: HomeSpaceRow;
  today: string;
  /** The shot shown; the first with photos when null or gone. */
  shotId: string | null;
  onShot: (shotId: string) => void;
  onCompare: (shotId: string, side: CompareSide) => void;
  onAdd: (shotId: string) => void;
}) {
  const shot = space.shots.find((s) => s.id === shotId) ?? firstShot(space);
  if (!shot) return null;
  const title = shotTitle(space.name, shot.name);
  const then = shot.baseline;
  const now = shot.newest;
  return (
    <div className="space-y-2" data-testid="then-and-now">
      {space.shots.length > 1 ? (
        // One line that scrolls sideways: a card is narrow, and a space can have six shots.
        <div role="group" aria-label="Shot" className="-mx-1 flex gap-1 overflow-x-auto px-1 pb-0.5">
          {space.shots.map((s) => (
            <button
              key={s.id}
              type="button"
              aria-pressed={s.id === shot.id}
              onClick={() => onShot(s.id)}
              className={cn(
                "min-h-9 max-w-36 shrink-0 truncate rounded-full px-2.5 text-xs font-medium focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none md:min-h-7",
                s.id === shot.id
                  ? "bg-primary text-primary-foreground"
                  : "bg-muted text-muted-foreground hover:text-foreground",
              )}
            >
              {s.name}
            </button>
          ))}
        </div>
      ) : null}
      {now && then ? (
        <div className="grid grid-cols-2 gap-2">
          <PhotoTile photo={then} label="Then" title={title} onClick={() => onCompare(shot.id, "then")} />
          {now.id !== then.id ? (
            <PhotoTile photo={now} label="Now" title={title} onClick={() => onCompare(shot.id, "now")} />
          ) : (
            <AddTile
              label="Add a photo to compare"
              ariaLabel={`Add a photo of ${title} to compare`}
              onClick={() => onAdd(shot.id)}
            />
          )}
        </div>
      ) : (
        <div className="grid grid-cols-2">
          <AddTile
            wide
            label={`No photos of ${title} yet`}
            ariaLabel={`Add photos of ${title}: none yet`}
            onClick={() => onAdd(shot.id)}
          />
        </div>
      )}
      {now ? (
        <p className="text-xs text-muted-foreground">
          {photoAgo(now.day, today)} · {shot.photos === 1 ? "1 photo" : `${shot.photos} photos`}
        </p>
      ) : null}
    </div>
  );
}
