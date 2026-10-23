import { CoverImage } from "@/components/cover/cover-image";
import { creditText } from "@/lib/cover";
import type { CoverRead } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * A small square of a subject's cover before its title (COVER-3, cover-images plan §3.8):
 * 36 px on Today's rows, 40 px on the Vision page's goals. Too small for a credit line, so
 * the credit is its tooltip; the banner one tap away shows it in full.
 */
export function CoverThumb({
  cover,
  size,
  className,
}: {
  cover: CoverRead;
  /** The square's side in CSS px; the photo is asked for at that size (and 2×). */
  size: number;
  className?: string;
}) {
  return (
    <CoverImage
      url={cover.url}
      color={cover.color}
      positionY={cover.positionY}
      width={size}
      title={creditText(cover)}
      className={cn("shrink-0 rounded-md", className)}
      style={{ width: size, height: size }}
    />
  );
}
