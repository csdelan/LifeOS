import { useNavigate } from "@tanstack/react-router";
import { UsersIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { usePeople } from "@/lib/queries";
import { Badge } from "@/components/ui/badge";

export function PeoplePage() {
  const { data, isLoading, isError } = usePeople();
  const navigate = useNavigate();

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-2xl px-4 py-10 md:px-8">
        <p className="type-scale-section text-primary">People / Agents</p>
        <h1 className="type-scale-display mt-1">Who's in the record</h1>
        <p className="mt-2 text-muted-foreground">
          Humans and AI agents share one directory (GEN-15). Associations, not alignment edges.
        </p>
        {isLoading ? <ListSkeleton /> : null}
        {isError ? (
          <EmptyState
            tone="error"
            icon={UsersIcon}
            title="People failed to load"
            description="This is not an empty directory — the reader never returned."
          />
        ) : null}
        <ul className="mt-8 space-y-2">
          {(data ?? []).map((p) => (
            <li key={p.id}>
              <button
                type="button"
                className="flex min-h-11 w-full cursor-pointer items-center justify-between rounded-xl bg-card px-4 py-3 text-left ring-1 ring-foreground/10 focus-visible:ring-2 focus-visible:ring-ring/50"
                onClick={() => void navigate({ to: "/map", search: { selected: p.id } })}
              >
                <div>
                  <p className="font-medium">{p.title}</p>
                  <p className="text-xs text-muted-foreground">{p.role ?? "—"}</p>
                </div>
                <Badge variant="secondary">{p.personKind === "ai" ? "AI" : "Human"}</Badge>
              </button>
            </li>
          ))}
        </ul>
        {data?.length === 0 ? (
          <EmptyState icon={UsersIcon} title="No people yet" />
        ) : null}
      </div>
    </div>
  );
}
