import { Skeleton } from "@/components/ui/skeleton";

export function ListSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="flex flex-col gap-2">
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex items-center gap-3 rounded-lg px-3 py-2">
          <Skeleton className="size-4 rounded-full" />
          <div className="flex-1 space-y-1.5">
            <Skeleton className="h-3.5 w-2/3" />
            <Skeleton className="h-2.5 w-1/3" />
          </div>
          <Skeleton className="h-5 w-16 rounded-full" />
        </div>
      ))}
    </div>
  );
}

export function TreeSkeleton() {
  return (
    <div className="space-y-2 py-2">
      <Skeleton className="h-7 w-48" />
      <Skeleton className="ml-6 h-7 w-64" />
      <Skeleton className="ml-12 h-7 w-56" />
      <Skeleton className="ml-12 h-7 w-40" />
      <Skeleton className="ml-6 h-7 w-52" />
      <Skeleton className="h-7 w-44" />
      <Skeleton className="ml-6 h-7 w-60" />
    </div>
  );
}

export function CardSkeleton() {
  return (
    <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <Skeleton className="mb-3 h-3 w-24" />
      <Skeleton className="h-6 w-3/4" />
      <Skeleton className="mt-3 h-3 w-full" />
      <Skeleton className="mt-2 h-3 w-2/3" />
    </div>
  );
}

export function GraphSkeleton() {
  return (
    <div className="flex h-full min-h-[22rem] items-center justify-center">
      <div className="grid grid-cols-3 gap-6 opacity-70">
        <Skeleton className="h-16 w-44 rounded-xl" />
        <Skeleton className="h-16 w-48 rounded-xl" />
        <Skeleton className="h-16 w-40 rounded-xl" />
        <Skeleton className="col-start-2 h-16 w-52 rounded-xl" />
        <Skeleton className="h-16 w-44 rounded-xl" />
        <Skeleton className="h-16 w-36 rounded-xl" />
      </div>
    </div>
  );
}
