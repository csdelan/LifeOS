import { ImageOffIcon } from "lucide-react";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { useArtifactBytesUrl } from "@/lib/queries";
import { cn } from "@/lib/utils";

/**
 * A food entry's photo (FOOD-5), fetched with the session's token and shown through an object
 * URL. A muted block stands in while it loads; a broken-image icon if it can't be read.
 * `alt` is empty when the photo sits inside a control that already names it.
 */
export function MealPhoto({ artifactId, alt, className }: { artifactId: string; alt: string; className?: string }) {
  const photo = useArtifactBytesUrl(artifactId);
  if (photo.data) {
    return <img src={photo.data} alt={alt} className={cn("object-cover", className)} draggable={false} />;
  }
  return (
    <span
      role={alt ? "img" : undefined}
      aria-label={alt ? (photo.isError ? `${alt} (couldn't load)` : alt) : undefined}
      aria-hidden={alt ? undefined : true}
      className={cn("flex items-center justify-center bg-muted text-muted-foreground", !photo.isError && "animate-pulse", className)}
    >
      {photo.isError || photo.data === null ? <ImageOffIcon className="size-5" /> : null}
    </span>
  );
}

/**
 * The photo full size, over the page, as large as the screen allows: a stored one by its
 * artifact, or one picked but not yet saved by its object URL (`src`).
 */
export function PhotoViewer({
  artifactId,
  src,
  name,
  open,
  onOpenChange,
}: {
  artifactId?: string | null;
  src?: string | null;
  name: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const full = "max-h-[80dvh] min-h-40 w-full min-w-40 rounded-lg object-contain";
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="w-auto max-w-[calc(100%-2rem)] gap-2 p-2 sm:max-w-[min(90vw,1200px)]">
        <DialogTitle className="px-2 pt-1 pr-10">{name}</DialogTitle>
        <DialogDescription className="sr-only">The photo of {name}, full size.</DialogDescription>
        {artifactId ? (
          <MealPhoto artifactId={artifactId} alt={`Photo of ${name}`} className={full} />
        ) : src ? (
          <img src={src} alt={`Photo of ${name}`} className={full} />
        ) : null}
      </DialogContent>
    </Dialog>
  );
}
