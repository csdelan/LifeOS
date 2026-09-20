import { ClipboardCheckIcon } from "lucide-react";
import { EmptyState } from "@/components/primitives/empty-state";
import { Button } from "@/components/ui/button";
import { Link } from "@tanstack/react-router";

export function ReviewsPage() {
  return (
    <div className="flex h-full items-center justify-center p-8">
      <EmptyState
        icon={ClipboardCheckIcon}
        title="Reviews come next"
        description="Daily, weekly, and monthly reviews are a later pass. The seam is here so the shell already has a destination."
        action={
          <Button asChild variant="outline">
            <Link to="/">Back to Focus</Link>
          </Button>
        }
      />
    </div>
  );
}
