import { Button } from "@/components/ui/button";
import { LIST_PAGE, type Paged } from "@/components/primitives/use-paged";

/** "200 of 450 shown" and the button that draws the next page; nothing once every row is drawn. */
export function ShowMore({ drawn, total, more }: Paged) {
  const left = total - drawn;
  if (left <= 0) return null;
  return (
    <div className="flex items-center gap-3 px-3 pt-3 text-xs text-muted-foreground">
      <span>
        {drawn} of {total} shown
      </span>
      <Button variant="outline" size="sm" onClick={more}>
        Show {Math.min(left, LIST_PAGE)} more
      </Button>
    </div>
  );
}
