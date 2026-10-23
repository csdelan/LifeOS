import { useId, useState, type FormEvent } from "react";
import { LoaderCircleIcon, SearchIcon } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { coverColor, coverQuery, coverSearchMiss, MAX_QUERY } from "@/lib/cover";
import { useCoverSearch, useWrites } from "@/lib/queries";
import type { CoverPhoto } from "@/lib/production-ui-types";

const FALLBACK_UNSPLASH_LINK = "https://unsplash.com/?utm_source=lifeos&utm_medium=referral";

/**
 * "Choose a cover" (COVER-1, cover-images plan §3.7): search Unsplash, filled in with the
 * subject's title, and tap a photo to make it the cover. The search goes through the server,
 * so the Access Key never reaches the browser. Give it a new key each time it opens, so each
 * opening starts from the title.
 */
export function CoverPicker({
  open,
  onOpenChange,
  subject,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  subject: { id: string; title: string };
}) {
  const initial = coverQuery(subject.title);
  const [draft, setDraft] = useState(initial);
  const [query, setQuery] = useState(initial);
  const [picking, setPicking] = useState<string | null>(null);
  const writes = useWrites();
  const inputId = useId();
  const search = useCoverSearch(query, open);

  const pages = search.data?.pages ?? [];
  const first = pages[0];
  const photos = pages.flatMap((p) => p.photos);
  const miss = coverSearchMiss(first);
  const unsplashLink = first?.unsplashLink ?? FALLBACK_UNSPLASH_LINK;

  function submit(event: FormEvent) {
    event.preventDefault();
    const words = coverQuery(draft);
    if (!words) return;
    setQuery(words);
    if (words === query) void search.refetch();
  }

  async function pick(photo: CoverPhoto) {
    if (picking) return;
    setPicking(photo.id);
    try {
      await writes.setCover({ subject: subject.id, photoId: photo.id });
      onOpenChange(false);
    } catch (err) {
      toast.error(err instanceof Error && err.message ? err.message : "Couldn't set the cover.");
    } finally {
      setPicking(null);
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => (picking ? undefined : onOpenChange(next))}>
      <DialogContent className="flex max-h-[min(90dvh,48rem)] flex-col gap-3 sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>Choose a cover</DialogTitle>
          <DialogDescription>
            Photos from{" "}
            <a href={unsplashLink} target="_blank" rel="noopener noreferrer" className="underline underline-offset-2">
              Unsplash
            </a>
            , for {subject.title}.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={submit} className="flex gap-2" role="search">
          <label htmlFor={inputId} className="sr-only">
            Search Unsplash
          </label>
          <Input
            id={inputId}
            type="search"
            value={draft}
            maxLength={MAX_QUERY}
            onChange={(e) => setDraft(e.target.value)}
            placeholder="mountain sunrise"
            className="min-w-0 flex-1"
          />
          {/* Never disabled while a search loads: a disabled submit button would swallow Enter. */}
          <Button type="submit" disabled={!coverQuery(draft)}>
            <SearchIcon /> Search
          </Button>
        </form>

        <div className="min-h-40 overflow-y-auto" aria-busy={search.isFetching || undefined}>
          {search.isPending && search.fetchStatus !== "idle" ? (
            <p className="flex items-center gap-2 py-8 text-sm text-muted-foreground" role="status">
              <LoaderCircleIcon className="size-4 animate-spin" aria-hidden /> Searching Unsplash…
            </p>
          ) : search.isError && photos.length === 0 ? (
            <p className="py-8 text-sm text-destructive" role="alert">
              {search.error instanceof Error && search.error.message
                ? search.error.message
                : "Unsplash couldn't be searched just now."}
            </p>
          ) : miss ? (
            <p className="py-8 text-sm text-muted-foreground" role="status">
              {miss}
            </p>
          ) : (
            <>
              <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3" aria-label={`Photos for ${first?.query ?? query}`}>
                {photos.map((photo) => (
                  <li key={photo.id}>
                    <PhotoTile
                      photo={photo}
                      busy={picking === photo.id}
                      disabled={!!picking}
                      onPick={() => void pick(photo)}
                    />
                  </li>
                ))}
              </ul>
              {search.hasNextPage ? (
                <div className="mt-3 flex justify-center">
                  <Button
                    variant="outline"
                    onClick={() => void search.fetchNextPage()}
                    disabled={search.isFetchingNextPage}
                  >
                    {search.isFetchingNextPage ? <LoaderCircleIcon className="animate-spin" aria-hidden /> : null}
                    More photos
                  </Button>
                </div>
              ) : null}
              {search.isError && photos.length > 0 ? (
                <p className="mt-2 text-sm text-destructive" role="alert">
                  {search.error instanceof Error ? search.error.message : "The next photos couldn't be loaded."}
                </p>
              ) : null}
            </>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}

function PhotoTile({
  photo,
  busy,
  disabled,
  onPick,
}: {
  photo: CoverPhoto;
  busy: boolean;
  disabled: boolean;
  onPick: () => void;
}) {
  const label = `Use the photo by ${photo.photographer}${photo.alt ? `: ${photo.alt}` : ""}`;
  return (
    <figure className="m-0">
      <button
        type="button"
        onClick={onPick}
        disabled={disabled}
        aria-label={label}
        className="relative block aspect-[3/2] w-full overflow-hidden rounded-lg ring-offset-background transition-opacity outline-none hover:opacity-90 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 disabled:opacity-60"
        style={{ backgroundColor: coverColor(photo.color) }}
      >
        <img
          src={photo.thumbUrl}
          alt=""
          loading="lazy"
          decoding="async"
          draggable={false}
          className="absolute inset-0 size-full object-cover"
        />
        {busy ? (
          <span className="absolute inset-0 grid place-items-center bg-black/40 text-white">
            <LoaderCircleIcon className="size-5 animate-spin" aria-hidden />
            <span className="sr-only">Setting the cover…</span>
          </span>
        ) : null}
      </button>
      <figcaption className="mt-1 truncate text-xs text-muted-foreground">
        {photo.photographerLink ? (
          <a
            href={photo.photographerLink}
            target="_blank"
            rel="noopener noreferrer"
            className="hover:text-foreground hover:underline"
          >
            {photo.photographer}
          </a>
        ) : (
          photo.photographer
        )}
      </figcaption>
    </figure>
  );
}
