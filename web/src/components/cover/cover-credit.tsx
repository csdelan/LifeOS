import type { CoverRead } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * "Photo by Annie Spratt on Unsplash", both names linking out with the referral parameters,
 * as Unsplash asks (cover-images plan §3.7).
 * - `overlay` (the default): a dark pill in a photo's corner, for the banner and Today's
 *   focus strip. White on `bg-black/60` meets 4.5:1 over any photo.
 * - `line`: a small muted line under a card's text (the Vision gallery's tiles). It sits
 *   above a stretched card button, so its links still work.
 * A thumbnail is smaller still, and carries `creditText` as its tooltip instead.
 */
export function CoverCredit({
  cover,
  variant = "overlay",
  className,
}: {
  cover: Pick<CoverRead, "photographer" | "photographerLink" | "unsplashLink">;
  variant?: "overlay" | "line";
  className?: string;
}) {
  const link =
    variant === "overlay"
      ? "font-medium underline underline-offset-2"
      : "underline underline-offset-2 hover:text-foreground";
  return (
    <p
      className={cn(
        variant === "overlay"
          ? "absolute bottom-2 left-2 max-w-[calc(100%-1rem)] truncate rounded-md bg-black/60 px-2 py-0.5 text-[0.7rem] text-white"
          : "relative z-10 truncate text-xs text-muted-foreground",
        className,
      )}
    >
      Photo by{" "}
      <a href={cover.photographerLink || cover.unsplashLink} target="_blank" rel="noopener noreferrer" className={link}>
        {cover.photographer}
      </a>{" "}
      on{" "}
      <a href={cover.unsplashLink} target="_blank" rel="noopener noreferrer" className={link}>
        Unsplash
      </a>
    </p>
  );
}
