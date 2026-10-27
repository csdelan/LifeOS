/**
 * Per-type Lucide icons used in Map outline, TypeBadge, and graph nodes.
 *
 * Customize here: swap any icon in TYPE_ICONS. Browse the set at
 * https://lucide.dev/icons — import names are PascalCase + `Icon`
 * (e.g. `TargetIcon`). Stroke inherits `currentColor` from `typeTone`.
 */
import type { LucideIcon } from "lucide-react";
import {
  CalendarDaysIcon,
  CircleAlertIcon,
  CircleDashedIcon,
  CompassIcon,
  FingerprintIcon,
  FolderKanbanIcon,
  GiftIcon,
  HandshakeIcon,
  LeafIcon,
  LightbulbIcon,
  RepeatIcon,
  ScaleIcon,
  ShieldIcon,
  SquareCheckIcon,
  TargetIcon,
  UserRoundIcon,
} from "lucide-react";
import type { SubjectType } from "@/lib/production-ui-types";
import { typeTone } from "@/lib/subject-meta";
import { cn } from "@/lib/utils";

export const TYPE_ICONS: Record<SubjectType, LucideIcon> = {
  Value: FingerprintIcon,
  Goal: TargetIcon,
  Project: FolderKanbanIcon,
  Task: SquareCheckIcon,
  Problem: CircleAlertIcon,
  Idea: LightbulbIcon,
  Decision: ScaleIcon,
  Commitment: HandshakeIcon,
  Constraint: ShieldIcon,
  Person: UserRoundIcon,
  Area: CompassIcon,
  Habit: RepeatIcon,
  Appointment: CalendarDaysIcon,
  Season: LeafIcon,
  Reward: GiftIcon,
};

export function TypeIcon({
  type,
  className,
  strokeWidth = 1.75,
}: {
  type: SubjectType;
  className?: string;
  strokeWidth?: number;
}) {
  // The kernel can hold a type the web doesn't know yet (a Space, say): a plain glyph, not a crash.
  const Icon = TYPE_ICONS[type] ?? CircleDashedIcon;
  return (
    <Icon
      aria-hidden
      strokeWidth={strokeWidth}
      className={cn("size-3.5 shrink-0", typeTone(type), className)}
    />
  );
}
