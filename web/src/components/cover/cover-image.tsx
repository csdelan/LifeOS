import { useState, type ReactNode } from "react";
import { coverColor, coverSrc, coverSrcSet, objectPosition } from "@/lib/cover";
import { cn } from "@/lib/utils";

/**
 * A cover photo filling its box (cover-images plan §3.6): asked for at `width` CSS px (and
 * 2× for sharp screens), cropped by the page at `positionY`, over the photo's own colour so
 * the box never flashes white while it loads and stays that colour if it can't. The image is
 * decorative (`alt=""`): the subject's title beside it says what it's about.
 */
export function CoverImage({
  url,
  color,
  positionY,
  width,
  eager = false,
  title,
  className,
  children,
}: {
  url: string;
  color?: string | null;
  positionY: number;
  width: number;
  /** A banner loads at once; cards lower on the page load as they scroll in. */
  eager?: boolean;
  /** A tooltip, for a thumbnail too small to carry its credit. */
  title?: string;
  className?: string;
  /** Laid over the photo: a credit, a menu. */
  children?: ReactNode;
}) {
  // The url that failed, so a new photo gets its own chance.
  const [failed, setFailed] = useState<string | null>(null);
  return (
    <div
      data-testid="cover-image"
      className={cn("relative overflow-hidden", className)}
      style={{ backgroundColor: coverColor(color) }}
      title={title}
    >
      {failed !== url ? (
        <img
          src={coverSrc(url, width)}
          srcSet={coverSrcSet(url, width)}
          alt=""
          loading={eager ? "eager" : "lazy"}
          decoding="async"
          draggable={false}
          onError={() => setFailed(url)}
          className="pointer-events-none absolute inset-0 size-full object-cover select-none"
          style={{ objectPosition: objectPosition(positionY) }}
        />
      ) : null}
      {children}
    </div>
  );
}
