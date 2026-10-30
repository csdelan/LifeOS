import { Link, useNavigate } from "@tanstack/react-router";
import { EyeIcon } from "lucide-react";
import { CoverCredit } from "@/components/cover/cover-credit";
import { CoverImage } from "@/components/cover/cover-image";
import { EmptyState } from "@/components/primitives/empty-state";
import { TypeIcon } from "@/components/primitives/type-icon";
import { useSubjects } from "@/lib/queries";
import { ListSkeleton } from "@/components/primitives/skeletons";
import type { SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * The width each gallery tile's photo asks for (and 2× for sharp screens): a statement tile is
 * at most about 350 px wide (three across in the page's 72rem), a goal tile about 260 px.
 */
const STATEMENT_PHOTO_WIDTH = 400;
const GOAL_PHOTO_WIDTH = 300;

/**
 * The Vision page as a gallery (Chris's LifeOS task "Vision page convert to gallery view", after
 * "Sort of like Gallery View in Notion" for the earlier cards): identity statements as tiles
 * with their photo across the top, then the long-term goals as smaller tiles in the same style.
 */
export function VisionPage() {
  const { data, isLoading, isError } = useSubjects("Value");
  const { data: goals } = useSubjects("Goal");
  const navigate = useNavigate();
  const longTerm = (goals ?? []).filter((g) => !g.archived);
  const open = (id: string) => void navigate({ to: "/map", search: { selected: id } });

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-6xl px-4 py-10 md:px-8">
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
        {(data ?? []).length > 0 ? (
          <section className="mt-8" aria-labelledby="vision-statements">
            <h2 id="vision-statements" className="type-scale-section text-muted-foreground">
              Identity statements
            </h2>
            <ul className="mt-3 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {(data ?? []).map((v) => (
                <li key={v.id} className="flex">
                  <GalleryTile
                    item={v}
                    type="Value"
                    photoWidth={STATEMENT_PHOTO_WIDTH}
                    onOpen={() => open(v.id)}
                    title={<span className="font-heading text-xl">{v.title}</span>}
                    detail={v.areaName ?? "Whole life"}
                  />
                </li>
              ))}
            </ul>
          </section>
        ) : null}
        {longTerm.length > 0 ? (
          <section className="mt-12" aria-labelledby="vision-goals">
            <h2 id="vision-goals" className="type-scale-section text-muted-foreground">
              Long-term goals
            </h2>
            <ul className="mt-3 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
              {longTerm.map((g) => (
                <li key={g.id} className="flex">
                  <GalleryTile
                    item={g}
                    type="Goal"
                    photoWidth={GOAL_PHOTO_WIDTH}
                    compact
                    onOpen={() => open(g.id)}
                    title={<span className="text-sm font-medium">{g.title}</span>}
                    detail={g.targetDate ?? null}
                  />
                </li>
              ))}
            </ul>
          </section>
        ) : null}
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

/**
 * One gallery tile: the cover across the top (3:2, cropped at its position), or a quiet panel
 * with the type's icon when there's none, so the grid stays even; then the title, a detail
 * line and the photo's credit. The title's button stretches over the whole tile, so the tile
 * opens the item on the Map while the credit's links, laid above it, still work.
 */
function GalleryTile({
  item,
  type,
  photoWidth,
  compact = false,
  onOpen,
  title,
  detail,
}: {
  item: SubjectListItem;
  type: SubjectType;
  photoWidth: number;
  compact?: boolean;
  onOpen: () => void;
  title: React.ReactNode;
  detail: string | null;
}) {
  return (
    <article
      className="relative flex w-full flex-col overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10 transition-shadow hover:shadow-(--shadow-lift) has-[button:focus-visible]:ring-2 has-[button:focus-visible]:ring-ring/50"
    >
      {item.cover ? (
        <CoverImage
          url={item.cover.url}
          color={item.cover.color}
          positionY={item.cover.positionY}
          width={photoWidth}
          className="aspect-[3/2] w-full shrink-0"
        />
      ) : (
        <div
          data-testid="cover-placeholder"
          className="flex aspect-[3/2] w-full shrink-0 items-center justify-center bg-muted"
          aria-hidden
        >
          <TypeIcon type={type} className={cn("opacity-40", compact ? "size-6" : "size-8")} />
        </div>
      )}
      <div className={cn("flex min-w-0 flex-1 flex-col", compact ? "gap-0.5 p-3" : "gap-1 p-4")}>
        <h3 className="leading-snug">
          <button
            type="button"
            className="cursor-pointer text-left outline-none after:absolute after:inset-0 after:content-['']"
            onClick={onOpen}
          >
            {title}
          </button>
        </h3>
        {detail ? <p className="text-xs text-muted-foreground">{detail}</p> : null}
        {item.cover ? <CoverCredit cover={item.cover} variant="line" className="mt-auto pt-1" /> : null}
      </div>
    </article>
  );
}
