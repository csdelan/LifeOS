import { Link, useNavigate } from "@tanstack/react-router";
import { EyeIcon } from "lucide-react";
import { CoverCredit } from "@/components/cover/cover-credit";
import { CoverImage } from "@/components/cover/cover-image";
import { CoverThumb } from "@/components/cover/cover-thumb";
import { EmptyState } from "@/components/primitives/empty-state";
import { TypeBadge } from "@/components/primitives/type-badge";
import { useSubjects } from "@/lib/queries";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { cn } from "@/lib/utils";

/**
 * The width a Vision card's square asks for. A landscape photo fills the square by its
 * height, so it shows wider than the square: about 220 px across in a 144 px square.
 */
const VISION_SQUARE_WIDTH = 300;

export function VisionPage() {
  const { data, isLoading, isError } = useSubjects("Value");
  const { data: goals } = useSubjects("Goal");
  const navigate = useNavigate();
  const longTerm = (goals ?? []).filter((g) => !g.archived);

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-3xl px-4 py-10 md:px-8">
        <p className="type-scale-section text-primary">Vision</p>
        <h1 className="type-scale-display mt-1">Who I'm becoming</h1>
        <p className="mt-2 text-muted-foreground">
          Identity Statements first, then the long-horizon Goals that serve them.
        </p>
        {isLoading ? <ListSkeleton /> : null}
        {isError ? (
          <EmptyState
            tone="error"
            icon={EyeIcon}
            title="Vision failed to load"
            description="This is not an empty vision — the reader never returned."
          />
        ) : null}
        <div className="mt-8 space-y-8">
          {(data ?? []).map((v) => (
            // The photo is a square along the card's left edge (Chris: "more square, smaller
            // images along the left edge of the card"), 128 px on a phone and 144 px from md,
            // filling the card's height when the text runs taller. The title's button
            // stretches over the whole card, so the card opens the statement while the
            // credit's links, laid above it, still work.
            <article
              key={v.id}
              className={cn(
                "relative flex overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10 transition-shadow hover:shadow-(--shadow-lift) has-[button:focus-visible]:ring-2 has-[button:focus-visible]:ring-ring/50",
                v.cover && "min-h-32 md:min-h-36",
              )}
            >
              {v.cover ? (
                <CoverImage
                  url={v.cover.url}
                  color={v.cover.color}
                  positionY={v.cover.positionY}
                  width={VISION_SQUARE_WIDTH}
                  className="w-32 shrink-0 self-stretch md:w-36"
                />
              ) : null}
              <div className={cn("min-w-0 flex-1 p-5", v.cover && "p-4 md:p-5")}>
                <TypeBadge type="Value" />
                <h2 className="mt-2 font-heading text-2xl">
                  <button
                    type="button"
                    className="cursor-pointer text-left outline-none after:absolute after:inset-0 after:content-['']"
                    onClick={() => void navigate({ to: "/map", search: { selected: v.id } })}
                  >
                    {v.title}
                  </button>
                </h2>
                <p className="mt-2 text-sm text-muted-foreground">
                  {v.areaName ?? "Whole life"}
                </p>
                {v.cover ? <CoverCredit cover={v.cover} variant="line" className="mt-2" /> : null}
              </div>
            </article>
          ))}
        </div>
        <section className="mt-12">
          <h2 className="type-scale-section text-muted-foreground">Long-term goals</h2>
          <ul className="mt-3 space-y-2">
            {longTerm.map((g) => (
              <li key={g.id}>
                <button
                  type="button"
                  className="flex min-h-11 items-center gap-3 text-left text-sm hover:text-primary focus-visible:ring-2 focus-visible:ring-ring/50"
                  onClick={() => void navigate({ to: "/map", search: { selected: g.id } })}
                >
                  {g.cover ? <CoverThumb cover={g.cover} size={40} /> : null}
                  <span>
                    {g.title}
                    {g.targetDate ? (
                      <span className="ml-2 text-xs text-muted-foreground">{g.targetDate}</span>
                    ) : null}
                  </span>
                </button>
              </li>
            ))}
          </ul>
        </section>
        <p className="mt-10 text-xs text-muted-foreground">
          Full composition per GEN-16 ships in a later pass.{" "}
          <Link to="/map" className="underline">
            Open the Map
          </Link>
        </p>
        {data?.length === 0 ? (
          <EmptyState icon={EyeIcon} title="No identity statements yet" />
        ) : null}
      </div>
    </div>
  );
}
