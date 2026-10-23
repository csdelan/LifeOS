import type { CoverRead } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * "Photo by Annie Spratt on Unsplash" on a dark pill in a photo's corner (cover-images plan
 * §3.7): white on `bg-black/60` meets 4.5:1 over any photo, and both names link out with
 * the referral parameters, as Unsplash asks. For the banner, the Vision cards and Today's
 * focus strip; a thumbnail is too small and carries `creditText` as its tooltip instead.
 */
export function CoverCredit({
  cover,
  className,
}: {
  cover: Pick<CoverRead, "photographer" | "photographerLink" | "unsplashLink">;
  className?: string;
}) {
  return (
    <p
      className={cn(
        "absolute bottom-2 left-2 max-w-[calc(100%-1rem)] truncate rounded-md bg-black/60 px-2 py-0.5 text-[0.7rem] text-white",
        className,
      )}
    >
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
  );
}
