import { Link, useNavigate } from "@tanstack/react-router";
import { EyeIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { TypeBadge } from "@/components/primitives/type-badge";
import { useSubjects } from "@/lib/queries";
import { ListSkeleton } from "@/components/primitives/skeletons";

export function VisionPage() {
  const { data, isLoading } = useSubjects("Value");
  const { data: goals } = useSubjects("Goal");
  const navigate = useNavigate();
  const longTerm = (goals ?? []).filter((g) => !g.archived);

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-3xl px-8 py-10">
        <p className="type-scale-section text-primary">Vision</p>
        <h1 className="type-scale-display mt-1">Who I'm becoming</h1>
        <p className="mt-2 text-muted-foreground">
          Identity Statements first, then the long-horizon Goals that serve them.
          Not a mood board — alignment.
        </p>
        {isLoading ? <ListSkeleton /> : null}
        <div className="mt-8 space-y-8">
          {(data ?? []).map((v) => (
            <article
              key={v.id}
              className="cursor-pointer rounded-xl bg-card p-5 ring-1 ring-foreground/10 transition-shadow hover:shadow-(--shadow-lift)"
              onClick={() => void navigate({ to: "/map", search: { selected: v.id } })}
            >
              <TypeBadge type="Value" />
              <h2 className="mt-2 font-heading text-2xl">{v.title}</h2>
              <p className="mt-2 text-sm text-muted-foreground">
                {v.areaName ?? "Whole life"}
              </p>
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
                  className="text-left text-sm hover:text-primary"
                  onClick={() => void navigate({ to: "/map", search: { selected: g.id } })}
                >
                  {g.title}
                  {g.targetDate ? (
                    <span className="ml-2 text-xs text-muted-foreground">{g.targetDate}</span>
                  ) : null}
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
