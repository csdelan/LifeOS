import { BotIcon, ImagePlusIcon, UserIcon } from "lucide-react";
import { useRef, useState } from "react";
import { PhotoViewer } from "@/components/food/meal-photo";
import { Button } from "@/components/ui/button";
import { jpegName, resizePhoto } from "@/lib/photo";
import { useArtifactBytesUrl, useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { useWriteAction } from "@/lib/write-action";

/**
 * A Person's photo in a circle (GEN-15), fetched with the session's token. Without one, or while
 * it loads, a person or an agent icon stands in. `alt` is empty where the name sits beside it.
 */
export function PersonAvatar({
  photoArtifactId,
  kind,
  alt = "",
  className,
}: {
  photoArtifactId?: string | null;
  /** `ai` for an AI agent: its stand-in is a bot. */
  kind?: string | null;
  alt?: string;
  className?: string;
}) {
  const photo = useArtifactBytesUrl(photoArtifactId);
  const box = cn("size-10 shrink-0 rounded-full", className);
  if (photoArtifactId && photo.data) {
    return <img src={photo.data} alt={alt} className={cn(box, "object-cover")} draggable={false} />;
  }
  const Icon = kind === "ai" ? BotIcon : UserIcon;
  return (
    <span
      role={alt ? "img" : undefined}
      aria-label={alt || undefined}
      aria-hidden={alt ? undefined : true}
      className={cn(
        box,
        "flex items-center justify-center bg-muted text-muted-foreground",
        photoArtifactId && photo.isLoading && "animate-pulse",
      )}
    >
      <Icon className="size-1/2" aria-hidden />
    </span>
  );
}

/**
 * A Person's photo on its own page, with Add photo, or Change and Remove once it has one. A
 * picked photo is shrunk to 1,200 px and saved at once, as a cover is; the record keeps a removed
 * one's bytes. Off while the Person is being edited, so nothing saves under Cancel.
 */
export function PersonPhotoControl({
  person,
  editing,
}: {
  person: { id: string; title: string; photoArtifactId?: string | null; kind?: string | null };
  editing: boolean;
}) {
  const writes = useWrites();
  const action = useWriteAction({ report: "inline", fallback: "That photo didn't save." });
  const [preparing, setPreparing] = useState(false);
  const [viewing, setViewing] = useState(false);
  const input = useRef<HTMLInputElement>(null);
  const photo = person.photoArtifactId ?? null;
  const busy = preparing || action.pending;

  async function pick(file: File | undefined) {
    if (!file) return;
    action.setError(null);
    setPreparing(true);
    let blob: Blob;
    try {
      blob = await resizePhoto(file);
    } catch (e) {
      action.setError(e instanceof Error ? e.message : "Couldn't prepare that photo.");
      return;
    } finally {
      setPreparing(false);
    }
    await action.run(() => writes.setPersonPhoto(person.id, blob, jpegName(file.name)));
  }

  return (
    <div className="mt-3 flex items-center gap-3">
      {photo ? (
        <button
          type="button"
          className="shrink-0 cursor-zoom-in rounded-full focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
          aria-label={`View the photo of ${person.title}`}
          onClick={() => setViewing(true)}
        >
          <PersonAvatar photoArtifactId={photo} kind={person.kind} className="size-16" />
        </button>
      ) : (
        <PersonAvatar kind={person.kind} className="size-16" />
      )}
      <div className="min-w-0 space-y-1">
        {!editing ? (
          <div className="flex flex-wrap gap-1.5">
            <Button variant="outline" size="sm" disabled={busy} onClick={() => input.current?.click()}>
              <ImagePlusIcon aria-hidden /> {photo ? "Change photo" : "Add photo"}
            </Button>
            {photo ? (
              <Button
                variant="ghost"
                size="sm"
                disabled={busy}
                onClick={() => void action.run(() => writes.removePersonPhoto(person.id))}
              >
                Remove photo
              </Button>
            ) : null}
          </div>
        ) : null}
        <p className="text-xs text-muted-foreground" aria-live="polite">
          {preparing ? "Preparing the photo…" : action.pending ? "Saving…" : photo || editing ? null : "Optional."}
        </p>
        {action.error ? (
          <p role="alert" className="text-xs text-destructive">
            {action.error}
          </p>
        ) : null}
      </div>
      <input
        ref={input}
        type="file"
        accept="image/*"
        hidden
        aria-label={`Photo of ${person.title}`}
        onChange={(e) => {
          void pick(e.target.files?.[0]);
          e.target.value = "";
        }}
      />
      <PhotoViewer artifactId={photo} name={person.title} open={viewing && !!photo} onOpenChange={setViewing} />
    </div>
  );
}
