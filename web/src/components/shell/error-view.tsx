import type { ErrorComponentProps } from "@tanstack/react-router";
import { Button } from "@/components/ui/button";
import { isMissingChunk } from "@/lib/new-build";

/**
 * What a failed page shows. Every page has its own (the router's default), so the shell and its
 * navigation stay and the next page opens as usual; the root keeps one for the shell itself
 * (code review CR93).
 */
export function ErrorView({ error, reset }: ErrorComponentProps) {
  if (isMissingChunk(error)) {
    return (
      <Centered>
        <h2 className="font-heading text-2xl">LifeOS was updated</h2>
        <p className="max-w-md text-sm text-muted-foreground">
          This page was opened before the update, and part of it is no longer there. Reload to get
          the new version.
        </p>
        <Button onClick={() => window.location.reload()}>Reload</Button>
      </Centered>
    );
  }
  return (
    <Centered>
      <h2 className="font-heading text-2xl">Something broke</h2>
      <p className="max-w-md text-sm text-muted-foreground">
        {error instanceof Error ? error.message : "Unknown error"}
      </p>
      <div className="flex flex-wrap justify-center gap-2">
        <Button onClick={reset}>Try again</Button>
        <Button variant="outline" onClick={() => window.location.assign("/")}>
          Back to Focus
        </Button>
      </div>
    </Centered>
  );
}

function Centered({ children }: { children: React.ReactNode }) {
  return (
    <div role="alert" className="flex h-full flex-col items-center justify-center gap-3 p-8 text-center">
      {children}
    </div>
  );
}
