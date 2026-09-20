import { useNavigate } from "@tanstack/react-router";
import { UsersIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { usePeople } from "@/lib/queries";
import { Badge } from "@/components/ui/badge";

export function PeoplePage() {
  const { data, isLoading } = usePeople();
  const navigate = useNavigate();

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-2xl px-8 py-10">
        <p className="type-scale-section text-primary">People / Agents</p>
        <h1 className="type-scale-display mt-1">Who's in the record</h1>
        <p className="mt-2 text-muted-foreground">
          Humans and AI agents share one directory (GEN-15). Associations, not alignment edges.
        </p>
        {isLoading ? <ListSkeleton /> : null}
        <ul className="mt-8 space-y-2">
          {(data ?? []).map((p) => (
            <li
              key={p.id}
              className="flex cursor-pointer items-center justify-between rounded-xl bg-card px-4 py-3 ring-1 ring-foreground/10"
              onClick={() => void navigate({ to: "/map", search: { selected: p.id } })}
            >
              <div>
                <p className="font-medium">{p.title}</p>
                <p className="text-xs text-muted-foreground">{p.role ?? "—"}</p>
              </div>
              <Badge variant="secondary">{p.personKind === "ai" ? "AI" : "Human"}</Badge>
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
