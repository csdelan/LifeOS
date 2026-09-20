import type { CSSProperties } from "react";
import { toast } from "sonner";
import { InboxIcon, SparklesIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";
import { Switch } from "@/components/ui/switch";
import { DateChip } from "@/components/primitives/date-chip";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListRow } from "@/components/primitives/list-row";
import { StatusPill } from "@/components/primitives/status-pill";
import { Tag } from "@/components/primitives/tag";
import { TreeNode } from "@/components/primitives/tree-node";
import { TypeBadge } from "@/components/primitives/type-badge";
import { CardSkeleton, ListSkeleton, TreeSkeleton } from "@/components/primitives/skeletons";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { todayIso, addDays } from "@/lib/dates";
import type { SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { typeLabel } from "@/lib/production-ui-types";
import { typeAccent } from "@/lib/subject-meta";
import { NodeBackdrop } from "@/components/graph/node-backdrop";
import { TypeIcon } from "@/components/primitives/type-icon";
import "@/components/graph/graph.css";

const GALLERY_TYPES: SubjectType[] = [
  "Value",
  "Goal",
  "Project",
  "Task",
  "Problem",
  "Idea",
  "Decision",
  "Commitment",
  "Constraint",
  "Person",
  "Area",
  "Habit",
  "Appointment",
  "Season",
];

const sample: SubjectListItem = {
  id: "gallery-task",
  urn: "urn:bsk:task:gallery-demo",
  type: "Task",
  title: "Polish the command center",
  status: "In progress",
  due: todayIso(),
  scheduled: addDays(todayIso(), 1),
  areaName: "Dev Career",
  archived: false,
  createdAt: new Date().toISOString(),
  tags: "focus,lifeos",
};

export function GalleryPage() {
  return (
    <div className="h-full overflow-y-auto">
      <div className="mx-auto max-w-5xl space-y-10 px-8 py-10">
        <header>
          <p className="type-scale-section text-primary">Design system</p>
          <h1 className="type-scale-display mt-1">Kitchen sink</h1>
          <p className="mt-2 max-w-xl text-muted-foreground">
            Every LifeOS primitive in isolation. Hearth tokens — warm paper, forest teal,
            ember for attention — in light and dark.
          </p>
        </header>

        <Section title="Type scale">
          <p className="type-scale-display">Prioritize today</p>
          <p className="type-scale-title mt-2">Run a half-marathon</p>
          <p className="type-scale-section mt-3 text-muted-foreground">Section label</p>
          <p className="mt-1 text-sm">Body — Geist, 15px, tight tracking.</p>
        </Section>

        <Section title="Button">
          <div className="flex flex-wrap gap-2">
            <Button>Primary</Button>
            <Button variant="secondary">Secondary</Button>
            <Button variant="outline">Outline</Button>
            <Button variant="ghost">Ghost</Button>
            <Button variant="destructive">Destructive</Button>
            <Button variant="link">Link</Button>
            <Button size="sm">Small</Button>
            <Button size="lg">Large</Button>
            <Button disabled>Disabled</Button>
          </div>
        </Section>

        <Section title="Card">
          <div className="grid gap-4 sm:grid-cols-2">
            <Card>
              <CardHeader>
                <CardTitle>Today's focus</CardTitle>
                <CardDescription>Ship LifeOS production UI</CardDescription>
              </CardHeader>
              <CardContent>Calm, attention-first surface — not a dashboard of counts.</CardContent>
            </Card>
            <CardSkeleton />
          </div>
        </Section>

        <Section title="Tag · StatusPill · DateChip · TypeBadge">
          <div className="flex flex-wrap items-center gap-2">
            {GALLERY_TYPES.map((type) => (
              <TypeBadge key={type} type={type} />
            ))}
            <StatusPill status="Active" />
            <StatusPill status="In progress" />
            <StatusPill status="Waiting" />
            <StatusPill status="Completed" />
            <DateChip date={todayIso()} />
            <DateChip date={addDays(todayIso(), -2)} />
            <DateChip date={addDays(todayIso(), 4)} kind="scheduled" />
            <Tag>health</Tag>
            <Tag onRemove={() => toast("Removed")}>focus</Tag>
            <Badge>Badge</Badge>
            <Switch defaultChecked />
          </div>
        </Section>

        <Section title="Graph node shells">
          <p className="mb-3 text-sm text-muted-foreground">
            First-pass per-type cards. Edit shells in{" "}
            <code className="text-xs">graph.css</code>, watermarks in{" "}
            <code className="text-xs">node-backdrop.tsx</code>, icons in{" "}
            <code className="text-xs">type-icon.tsx</code>.
          </p>
          <div className="flex flex-wrap gap-3">
            {GALLERY_TYPES.map((type) => (
              <div
                key={type}
                className="lifeos-node"
                data-type={type}
                style={
                  {
                    "--node-accent": typeAccent(type),
                    width: 220,
                    height: 84,
                  } as CSSProperties
                }
              >
                <div className="lifeos-node__shell">
                  <div className="lifeos-node__art">
                    <NodeBackdrop type={type} />
                  </div>
                  <div className="lifeos-node__body">
                    <span className="flex items-center gap-1.5">
                      <TypeIcon type={type} className="size-3.5" />
                      <TypeBadge type={type} withGlyph={false} />
                    </span>
                    <p className="truncate text-sm font-medium text-foreground">{typeLabel(type)}</p>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </Section>

        <Section title="ListRow">
          <div className="rounded-xl bg-card p-2 ring-1 ring-foreground/10">
            <ListRow item={sample} selected />
            <ListRow item={{ ...sample, id: "2", title: "Buy race shoes", due: addDays(todayIso(), -2), status: "Not started" }} />
          </div>
        </Section>

        <Section title="TreeNode">
          <div className="rounded-xl bg-card p-2 ring-1 ring-foreground/10">
            <TreeNode
              subject={{ ...sample, type: "Value", title: "Craftsman of systems", status: null, due: null }}
              depth={0}
              expanded
              expandable
              selected
            >
              <TreeNode
                subject={{ ...sample, type: "Goal", title: "Ship LifeOS production UI", status: "Active" }}
                depth={1}
                expanded
                expandable
                extraParents={[{ id: "gallery-parent", title: "a Project somewhere" }]}
              >
                <TreeNode
                  subject={{ ...sample, type: "Task", title: "Polish the command center" }}
                  depth={2}
                  onCreateChild={() => toast("Inline create")}
                />
              </TreeNode>
            </TreeNode>
          </div>
        </Section>

        <Section title="EmptyState">
          <div className="grid gap-4 sm:grid-cols-2">
            <EmptyState
              icon={SparklesIcon}
              tone="clear"
              title="Inbox is genuinely clear"
              description="Nothing needs a decision right now."
            />
            <EmptyState
              icon={InboxIcon}
              tone="error"
              title="Inbox failed to load"
              description="The reader never returned. This is not Inbox Zero."
              action={<Button size="sm">Retry</Button>}
            />
          </div>
        </Section>

        <Section title="Skeleton loaders">
          <ListSkeleton rows={3} />
          <Separator className="my-4" />
          <TreeSkeleton />
        </Section>

        <Section title="Toast">
          <div className="flex gap-2">
            <Button onClick={() => toast.success("Created Long run Saturday")}>Success</Button>
            <Button variant="outline" onClick={() => toast.error("A Goal needs a target date")}>
              Error
            </Button>
            <Button variant="secondary" onClick={() => toast("Captured to Inbox")}>
              Neutral
            </Button>
          </div>
        </Section>

        <Section title="SubjectDetail">
          <p className="mb-3 text-sm text-muted-foreground">
            Live mock subject — read-only first, explicit Edit / Save / Cancel. Tags and
            Relationships are visually separate.
          </p>
          <div className="h-[32rem] overflow-hidden rounded-xl bg-card shadow-(--shadow-lift) ring-1 ring-foreground/10">
            <SubjectDetail subjectId="goal-lifeos" />
          </div>
        </Section>

        <Section title="Input">
          <Input placeholder="Quick capture…" className="max-w-sm" />
        </Section>
      </div>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h2 className="type-scale-section mb-3 text-muted-foreground">{title}</h2>
      {children}
    </section>
  );
}
