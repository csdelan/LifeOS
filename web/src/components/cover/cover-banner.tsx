import { useEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from "react";
import { ImageIcon, MoveVerticalIcon, RefreshCwIcon, Trash2Icon } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { CoverImage } from "@/components/cover/cover-image";
import { BANNER_WIDTH, clampPosition, dragPosition, keyPosition, positionText } from "@/lib/cover";
import { useWrites } from "@/lib/queries";
import type { CoverRead } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/** Buttons laid over a photo: white on a dark pill, 4.5:1 over any picture. */
const ON_PHOTO = "border-0 bg-black/60 text-white shadow-none hover:bg-black/75 hover:text-white aria-expanded:bg-black/75";

/**
 * The cover across the top of the detail panel (COVER-2, cover-images plan §3.7): the photo,
 * its credit, and a Cover menu to change, reposition or remove it. Reposition works like
 * Notion's: drag the photo, or use the arrow keys, then Save. Key it by subject, so another
 * subject in the same panel never inherits a repositioning in progress.
 */
export function CoverBanner({
  subjectId,
  cover,
  onChangeCover,
}: {
  subjectId: string;
  cover: CoverRead;
  onChangeCover: () => void;
}) {
  const writes = useWrites();
  const [repositioning, setRepositioning] = useState(false);
  const [draftY, setDraftY] = useState(cover.positionY);
  const [busy, setBusy] = useState(false);
  const slider = useRef<HTMLDivElement>(null);
  const focusSlider = useRef(false);
  const drag = useRef<{ pointerId: number; startY: number; clientY: number } | null>(null);

  // Reposition, chosen from the menu, takes the focus to the photo so the arrow keys move it
  // at once. The menu hands focus back after it closes, so it's taken there too.
  useEffect(() => {
    if (repositioning && focusSlider.current) slider.current?.focus();
  }, [repositioning]);

  function startRepositioning() {
    setDraftY(cover.positionY);
    focusSlider.current = true;
    setRepositioning(true);
  }

  function cancel() {
    drag.current = null;
    setDraftY(cover.positionY);
    setRepositioning(false);
  }

  async function save() {
    setBusy(true);
    try {
      const y = clampPosition(draftY);
      if (y !== cover.positionY) await writes.moveCover(subjectId, y);
      setRepositioning(false);
    } catch (err) {
      toast.error(err instanceof Error && err.message ? err.message : "Couldn't move the cover.");
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    setBusy(true);
    try {
      await writes.clearCover(subjectId);
    } catch (err) {
      toast.error(err instanceof Error && err.message ? err.message : "Couldn't remove the cover.");
    } finally {
      setBusy(false);
    }
  }

  function onPointerDown(event: PointerEvent<HTMLDivElement>) {
    if (!repositioning || busy || event.button !== 0) return;
    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    event.currentTarget.focus();
    drag.current = { pointerId: event.pointerId, startY: draftY, clientY: event.clientY };
  }

  function onPointerMove(event: PointerEvent<HTMLDivElement>) {
    const d = drag.current;
    if (!d || d.pointerId !== event.pointerId) return;
    const box = event.currentTarget.getBoundingClientRect();
    setDraftY(
      dragPosition({
        startY: d.startY,
        dy: event.clientY - d.clientY,
        boxWidth: box.width,
        boxHeight: box.height,
        imageWidth: cover.width || 3,
        imageHeight: cover.height || 2,
      }),
    );
  }

  function onPointerUp(event: PointerEvent<HTMLDivElement>) {
    if (drag.current?.pointerId === event.pointerId) drag.current = null;
  }

  function onKeyDown(event: KeyboardEvent<HTMLDivElement>) {
    if (!repositioning || busy) return;
    const next = keyPosition(draftY, event.key);
    if (next === null) return;
    event.preventDefault();
    setDraftY(next);
  }

  return (
    <div className="relative shrink-0" data-testid="cover-banner">
      <div
        ref={slider}
        {...(repositioning
          ? {
              role: "slider",
              tabIndex: 0,
              "aria-label": "Cover position: drag the photo or use the arrow keys",
              "aria-orientation": "vertical" as const,
              "aria-valuemin": 0,
              "aria-valuemax": 100,
              "aria-valuenow": clampPosition(draftY),
              "aria-valuetext": positionText(draftY),
            }
          : {})}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onKeyDown={onKeyDown}
        className={cn(
          "outline-none",
          repositioning &&
            "cursor-grab touch-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset active:cursor-grabbing",
        )}
      >
        <CoverImage
          url={cover.url}
          color={cover.color}
          positionY={repositioning ? draftY : cover.positionY}
          width={BANNER_WIDTH}
          eager
          className="h-32 md:h-36"
        />
      </div>

      {repositioning ? (
        <span
          aria-hidden
          className="pointer-events-none absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 rounded-full bg-black/60 px-3 py-1 text-xs text-white"
        >
          Drag to reposition
        </span>
      ) : null}

      <p className="absolute bottom-2 left-2 max-w-[calc(100%-1rem)] truncate rounded-md bg-black/60 px-2 py-0.5 text-[0.7rem] text-white">
        Photo by{" "}
        <a
          href={cover.photographerLink || cover.unsplashLink}
          target="_blank"
          rel="noopener noreferrer"
          className="font-medium underline underline-offset-2"
        >
          {cover.photographer}
        </a>{" "}
        on{" "}
        <a
          href={cover.unsplashLink}
          target="_blank"
          rel="noopener noreferrer"
          className="font-medium underline underline-offset-2"
        >
          Unsplash
        </a>
      </p>

      <div className="absolute top-2 right-2 flex gap-1.5">
        {repositioning ? (
          <>
            <Button size="xs" className={ON_PHOTO} onClick={cancel} disabled={busy}>
              Cancel
            </Button>
            <Button
              size="xs"
              className="border-0 bg-white text-black shadow-none hover:bg-white/90"
              onClick={() => void save()}
              disabled={busy}
            >
              Save position
            </Button>
          </>
        ) : (
          <DropdownMenu modal={false}>
            <DropdownMenuTrigger asChild>
              <Button size="xs" className={ON_PHOTO} disabled={busy} aria-label="Cover options">
                <ImageIcon aria-hidden /> Cover
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent
              align="end"
              className="w-44"
              onCloseAutoFocus={(event) => {
                if (!focusSlider.current) return;
                event.preventDefault();
                focusSlider.current = false;
                slider.current?.focus();
              }}
            >
              <DropdownMenuItem onSelect={onChangeCover}>
                <RefreshCwIcon /> Change cover
              </DropdownMenuItem>
              <DropdownMenuItem onSelect={startRepositioning}>
                <MoveVerticalIcon /> Reposition
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem variant="destructive" onSelect={() => void remove()}>
                <Trash2Icon /> Remove cover
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}
      </div>
    </div>
  );
}
