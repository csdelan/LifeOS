import { CompassIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { useAreas } from "@/lib/queries";

export function AreasPage() {
  const { data, isLoading, isError } = useAreas();

  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-2xl px-4 py-10 md:px-8">
        <p className="type-scale-section text-primary">Areas</p>
        <h1 className="type-scale-display mt-1">Domains of a life</h1>
        <p className="mt-2 text-muted-foreground">
          Areas classify work. They are not alignment edges, and they are not archived.
        </p>
        {isLoading ? <ListSkeleton /> : null}
        {isError ? (
          <EmptyState
            tone="error"
            icon={CompassIcon}
            title="Areas failed to load"
            description="This is not an empty list — the reader never returned."
          />
        ) : null}
        <ul className="mt-8 space-y-3">
          {(data ?? []).map((a) => (
            <li key={a.id} className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
              <h2 className="font-heading text-xl">{a.name}</h2>
              <p className="mt-1 text-sm text-muted-foreground">{a.description}</p>
            </li>
          ))}
        </ul>
        {data?.length === 0 ? (
          <EmptyState icon={CompassIcon} title="No areas yet" />
        ) : null}
      </div>
    </div>
  );
}
