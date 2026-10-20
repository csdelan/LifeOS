import { useState } from "react";
import { GraduationCapIcon } from "lucide-react";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import type { HabitRow } from "@/lib/production-ui-types";
import { establishmentProgress } from "@/lib/establish";
import { useSubject, useWrites } from "@/lib/queries";
import { formatAmounts } from "@/lib/rewards";
import { cn } from "@/lib/utils";

/**
 * "Ready to establish" (REWARD-2): the habit met its target, so LifeOS suggests and Chris
 * confirms. Confirming moves it to Established, which pays the establishing bounty and
 * graduates it off the daily list.
 */
export function ReadyToEstablish({ habit, className }: { habit: HabitRow; className?: string }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button
        type="button"
        size="xs"
        variant="outline"
        className={cn("border-[var(--type-reward)]/40 bg-[var(--type-reward)]/12 text-foreground", className)}
        aria-label={`Ready to establish ${habit.name}`}
        onClick={() => setOpen(true)}
      >
        <GraduationCapIcon aria-hidden /> Ready to establish
      </Button>
      <EstablishDialog habit={habit} open={open} onOpenChange={setOpen} />
    </>
  );
}

export function EstablishDialog({
  habit,
  open,
  onOpenChange,
}: {
  habit: HabitRow;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const writes = useWrites();
  const { data: detail } = useSubject(open ? habit.id : undefined);
  const pays = detail?.bounty?.find((l) => l.trigger === "established")?.amounts;
  const paysText = pays && Object.keys(pays).length > 0 ? ` and pays ${formatAmounts(pays)}` : "";
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Establish {habit.name}?</AlertDialogTitle>
          <AlertDialogDescription>
            {habit.establishment ? `${establishmentProgress(habit.establishment)}. ` : null}
            Establishing graduates it: it leaves daily tracking{paysText}. Its history stays, and
            moving it back to Building resumes tracking.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Not yet</AlertDialogCancel>
          <AlertDialogAction onClick={() => void writes.setStatus(habit.id, "Established")}>
            Establish
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
