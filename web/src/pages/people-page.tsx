import { useNavigate } from "@tanstack/react-router";
import { UsersIcon } from "lucide-react";
import { ConnectedAgents } from "@/components/people/connected-agents";
import { PersonAvatar } from "@/components/people/person-photo";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { usePeople } from "@/lib/queries";
import { Badge } from "@/components/ui/badge";
import { ReadFailed } from "@/components/primitives/read-state";

export function PeoplePage() {
  const people = usePeople();
  const { data, isLoading } = people;
  const navigate = useNavigate();

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-2xl px-4 py-10 md:px-8">
        <p className="type-scale-section text-primary">People / Agents</p>
        <h1 className="type-scale-display mt-1">Who's in the record</h1>
        <p className="mt-2 text-muted-foreground">
          Humans and AI agents share one directory (GEN-15). Associations, not alignment edges.
          Open someone to add their photo.
        </p>
        {isLoading ? <ListSkeleton /> : null}
        <ReadFailed
          read={people}
          what="People"
          notEmpty="an empty directory"
          size="page"
          icon={UsersIcon}
        />
        <ul className="mt-8 space-y-2">
          {(data ?? []).map((p) => (
            <li key={p.id}>
              <button
                type="button"
                className="flex min-h-11 w-full cursor-pointer items-center justify-between rounded-xl bg-card px-4 py-3 text-left ring-1 ring-foreground/10 focus-visible:ring-2 focus-visible:ring-ring/50"
                onClick={() => void navigate({ to: "/map", search: { selected: p.id } })}
              >
                <div className="flex min-w-0 items-center gap-3">
                  <PersonAvatar photoArtifactId={p.photoArtifactId} kind={p.personKind} />
                  <div className="min-w-0">
                    <p className="font-medium">{p.title}</p>
                    <p className="text-xs text-muted-foreground">{p.role ?? "—"}</p>
                  </div>
                </div>
                <Badge variant="secondary">{p.personKind === "ai" ? "AI" : "Human"}</Badge>
              </button>
            </li>
          ))}
        </ul>
        {data?.length === 0 ? (
          <EmptyState icon={UsersIcon} title="No people yet" />
        ) : null}
        <ConnectedAgents />
      </div>
    </div>
  );
}
