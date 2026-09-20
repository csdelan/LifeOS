import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import {
  BookOpenIcon,
  HistoryIcon,
  LayoutListIcon,
  Link2Icon,
  TagIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Separator } from "@/components/ui/separator";
import { DateChip } from "@/components/primitives/date-chip";
import { EmptyState } from "@/components/primitives/empty-state";
import { StatusPill } from "@/components/primitives/status-pill";
import { Tag } from "@/components/primitives/tag";
import { TypeBadge } from "@/components/primitives/type-badge";
import { ListSkeleton } from "@/components/primitives/skeletons";
import {
  useHistory,
  useJournal,
  useRelations,
  useSubject,
  useSubjectTags,
  useSubjects,
  useTagUniverse,
  useWrites,
} from "@/lib/queries";
import { STATUS_BY_TYPE, defaultStatus, pickApplicableAttrs } from "@/lib/production-ui-types";
import type { Relation, SubjectType } from "@/lib/production-ui-types";
import { formatTimestamp } from "@/lib/dates";

export function SubjectDetail({
  subjectId,
  onDirtyChange,
  onClose,
}: {
  subjectId: string;
  onDirtyChange?: (dirty: boolean) => void;
  onClose?: () => void;
}) {
  const { data, isLoading, isError } = useSubject(subjectId);
  const [editing, setEditing] = useState(false);
  const writes = useWrites();

  const [statement, setStatement] = useState("");
  const [scope, setScope] = useState("");
  const [due, setDue] = useState("");
  const [targetDate, setTargetDate] = useState("");
  const [status, setStatus] = useState("");

  useEffect(() => {
    if (!data) return;
    const attrs = parseAttrBag(data.attributes);
    setStatement(data.statement ?? attrs.statement ?? "");
    setScope(
      data.type === "Goal"
        ? attrs.desired_end_state ?? data.scope ?? ""
        : data.scope ?? attrs.description ?? "",
    );
    setDue(data.due ?? attrs.due ?? "");
    setTargetDate(data.targetDate ?? attrs.target_date ?? "");
    setStatus(data.status || defaultStatus(data.type));
    setEditing(false);
  }, [data]);

  const dirty = useMemo(() => {
    if (!data || !editing) return false;
    const attrs = parseAttrBag(data.attributes);
    const originalScope =
      data.type === "Goal"
        ? attrs.desired_end_state ?? data.scope ?? ""
        : data.scope ?? attrs.description ?? "";
    return (
      statement !== (data.statement ?? attrs.statement ?? "") ||
      scope !== originalScope ||
      due !== (data.due ?? attrs.due ?? "") ||
      targetDate !== (data.targetDate ?? attrs.target_date ?? "") ||
      status !== (data.status || defaultStatus(data.type))
    );
  }, [data, editing, statement, scope, due, targetDate, status]);

  useEffect(() => {
    onDirtyChange?.(dirty);
  }, [dirty, onDirtyChange]);

  if (isLoading) {
    return (
      <div className="p-5">
        <ListSkeleton rows={6} />
      </div>
    );
  }
  if (isError) {
    return (
      <EmptyState
        tone="error"
        title="Couldn't load this subject"
        description="The reader failed. Retry from the Map."
      />
    );
  }
  if (!data) {
    return (
      <EmptyState
        title="Select something"
        description="Detail opens here — not a separate page."
      />
    );
  }

  const subject = data;
  const statuses = STATUS_BY_TYPE[subject.type];

  async function save() {
    if (subject.type === "Goal" && status === "Active" && !targetDate) {
      toast.error("A Goal needs a target date before it can be Active.");
      return;
    }
    const raw: Record<string, string> = {};
    if (subject.type === "Value") {
      raw.statement = statement;
    } else if (subject.type === "Goal") {
      raw.desired_end_state = scope;
      raw.target_date = targetDate;
    } else if (subject.type === "Project") {
      raw.description = scope;
      raw.target_date = targetDate;
    } else if (subject.type === "Task") {
      raw.description = scope;
      raw.due = due;
    } else if (subject.type === "Constraint") {
      raw.scope = scope;
    } else {
      raw.description = scope;
    }
    const attrs = pickApplicableAttrs(subject.type, raw);
    if (Object.keys(attrs).length > 0) {
      await writes.setAttributes(subject.id, attrs);
    }
    const nextStatus = status || defaultStatus(subject.type);
    if (nextStatus && nextStatus !== (subject.status || defaultStatus(subject.type))) {
      await writes.setStatus(subject.id, nextStatus);
    }
    setEditing(false);
  }

  function cancel() {
    const attrs = parseAttrBag(subject.attributes);
    setStatement(subject.statement ?? attrs.statement ?? "");
    setScope(
      subject.type === "Goal"
        ? attrs.desired_end_state ?? subject.scope ?? ""
        : subject.scope ?? attrs.description ?? "",
    );
    setDue(subject.due ?? attrs.due ?? "");
    setTargetDate(subject.targetDate ?? attrs.target_date ?? "");
    setStatus(subject.status || defaultStatus(subject.type));
    setEditing(false);
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="shrink-0 border-b px-5 py-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <TypeBadge type={subject.type} />
            {/* TODO: title rename is not in Application — bsk set only patches attributes. */}
            <h2 className="mt-1 font-heading text-2xl leading-tight">{subject.title}</h2>
            <div className="mt-2 flex flex-wrap items-center gap-1.5">
              <StatusPill status={subject.status || defaultStatus(subject.type) || null} />
              <DateChip
                date={subject.type === "Goal" || subject.type === "Project" ? subject.targetDate : subject.due}
                kind={subject.type === "Goal" || subject.type === "Project" ? "target" : "due"}
              />
              {subject.areaName ? (
                <span className="text-[0.75rem] text-muted-foreground">{subject.areaName}</span>
              ) : null}
              {subject.archived ? (
                <span className="text-[0.75rem] text-muted-foreground">Archived</span>
              ) : null}
            </div>
          </div>
          <div className="flex shrink-0 gap-1.5">
            {editing ? (
              <>
                <Button variant="outline" size="sm" onClick={cancel}>
                  Cancel
                </Button>
                <Button size="sm" onClick={() => void save()} disabled={!dirty}>
                  Save
                </Button>
              </>
            ) : (
              <Button variant="outline" size="sm" onClick={() => setEditing(true)}>
                Edit
              </Button>
            )}
            {onClose ? (
              <Button variant="ghost" size="sm" onClick={onClose}>
                Close
              </Button>
            ) : null}
          </div>
        </div>
      </header>

      <Tabs defaultValue="overview" className="flex min-h-0 flex-1 flex-col gap-0">
        <div className="shrink-0 border-b px-5">
          <TabsList variant="line" className="w-full justify-start">
            <TabsTrigger value="overview">
              <LayoutListIcon /> Overview
            </TabsTrigger>
            <TabsTrigger value="relationships">
              <Link2Icon /> Relationships
            </TabsTrigger>
            <TabsTrigger value="tags">
              <TagIcon /> Tags
            </TabsTrigger>
            <TabsTrigger value="journal">
              <BookOpenIcon /> Journal
            </TabsTrigger>
            <TabsTrigger value="history">
              <HistoryIcon /> History
            </TabsTrigger>
          </TabsList>
        </div>
        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">
          <TabsContent value="overview">
            <OverviewTab
              type={subject.type}
              editing={editing}
              statement={statement}
              scope={scope}
              due={due}
              targetDate={targetDate}
              status={status}
              urn={subject.urn}
              setStatement={setStatement}
              setScope={setScope}
              setDue={setDue}
              setTargetDate={setTargetDate}
              setStatus={setStatus}
              statuses={statuses}
              archived={subject.archived}
              onArchive={() => void writes.archive(subject.id)}
              onRestore={() => void writes.restore(subject.id)}
            />
          </TabsContent>
          <TabsContent value="relationships">
            <RelationshipsTab subjectId={subject.id} />
          </TabsContent>
          <TabsContent value="tags">
            <TagsTab subjectId={subject.id} />
          </TabsContent>
          <TabsContent value="journal">
            <JournalTab subjectId={subject.id} />
          </TabsContent>
          <TabsContent value="history">
            <HistoryTab subjectId={subject.id} />
          </TabsContent>
        </div>
      </Tabs>
    </div>
  );
}

function OverviewTab({
  type,
  editing,
  statement,
  scope,
  due,
  targetDate,
  status,
  urn,
  setStatement,
  setScope,
  setDue,
  setTargetDate,
  setStatus,
  statuses,
  archived,
  onArchive,
  onRestore,
}: {
  type: SubjectType;
  editing: boolean;
  statement: string;
  scope: string;
  due: string;
  targetDate: string;
  status: string;
  urn: string;
  setStatement: (v: string) => void;
  setScope: (v: string) => void;
  setDue: (v: string) => void;
  setTargetDate: (v: string) => void;
  setStatus: (v: string) => void;
  statuses?: string[];
  archived: boolean;
  onArchive: () => void;
  onRestore: () => void;
}) {
  return (
    <div className="space-y-5">
      {type === "Value" ? (
        <Field label="Identity statement">
          {editing ? (
            <Textarea value={statement} onChange={(e) => setStatement(e.target.value)} />
          ) : (
            <p className="font-heading text-lg italic text-foreground/90">
              {statement || "No statement yet."}
            </p>
          )}
        </Field>
      ) : (
        <Field label={type === "Goal" ? "Desired end state" : "Description"}>
          {editing ? (
            <Textarea value={scope} onChange={(e) => setScope(e.target.value)} />
          ) : (
            <p className="text-sm text-foreground/85">{scope || "—"}</p>
          )}
        </Field>
      )}

      {statuses && statuses.length > 0 ? (
        <Field label="Status">
          {editing ? (
            <Select value={status} onValueChange={setStatus}>
              <SelectTrigger className="w-48">
                <SelectValue placeholder="Status" />
              </SelectTrigger>
              <SelectContent>
                {statuses.map((s) => (
                  <SelectItem key={s} value={s}>
                    {s}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          ) : (
            <StatusPill status={status || null} />
          )}
        </Field>
      ) : (
        <p className="text-sm text-muted-foreground">
          {type === "Value"
            ? "Identity Statements have no status — they simply are."
            : "This type has no status workflow."}
        </p>
      )}

      {type === "Goal" || type === "Project" ? (
        <Field label="Target date">
          {editing ? (
            <Input type="date" value={targetDate} onChange={(e) => setTargetDate(e.target.value)} className="w-48" />
          ) : targetDate ? (
            <DateChip date={targetDate} kind="target" />
          ) : (
            <span className="text-sm text-muted-foreground">None</span>
          )}
        </Field>
      ) : type === "Task" ? (
        <Field label="Due date">
          {editing ? (
            <Input type="date" value={due} onChange={(e) => setDue(e.target.value)} className="w-48" />
          ) : due ? (
            <DateChip date={due} kind="due" />
          ) : (
            <span className="text-sm text-muted-foreground">None</span>
          )}
        </Field>
      ) : type !== "Value" && type !== "Person" && type !== "Area" && type !== "Habit" ? (
        <Field label="Due date">
          {editing ? (
            <Input type="date" value={due} onChange={(e) => setDue(e.target.value)} className="w-48" />
          ) : due ? (
            <DateChip date={due} kind="due" />
          ) : (
            <span className="text-sm text-muted-foreground">None</span>
          )}
        </Field>
      ) : null}

      <Field label="URN">
        <code className="text-[0.75rem] text-muted-foreground">{urn}</code>
      </Field>

      <Separator />
      <div>
        <p className="type-scale-section text-muted-foreground">Archive</p>
        <p className="mt-1 text-sm text-muted-foreground">LifeOS archives, never deletes.</p>
        <div className="mt-2">
          {archived ? (
            <Button variant="outline" size="sm" onClick={onRestore}>
              Restore
            </Button>
          ) : (
            <Button variant="outline" size="sm" onClick={onArchive}>
              Archive
            </Button>
          )}
        </div>
      </div>
    </div>
  );
}

function RelationshipsTab({ subjectId }: { subjectId: string }) {
  const { data, isLoading } = useRelations(subjectId);
  const { data: subjects } = useSubjects();
  const writes = useWrites();
  const [rel, setRel] = useState<Relation>("serves");
  const [to, setTo] = useState("");

  if (isLoading) return <ListSkeleton rows={4} />;

  return (
    <div className="space-y-6">
      <section className="rounded-xl bg-muted/40 p-3">
        <h3 className="type-scale-section text-muted-foreground">Parents</h3>
        <p className="mt-1 mb-2 text-xs text-muted-foreground">
          Alignment edges — this is structure, not tagging.
        </p>
        {data?.parents.length ? (
          <ul className="space-y-1">
            {data.parents.map((e) => (
              <li key={`${e.relation}-${e.subjectId}`} className="flex items-center gap-2 text-sm">
                <TypeBadge type={e.type} />
                <span>{e.title}</span>
                <span className="text-[0.6875rem] text-muted-foreground">{e.relation}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-sm text-muted-foreground">No parents — unaligned for now.</p>
        )}
      </section>
      <section className="rounded-xl bg-muted/40 p-3">
        <h3 className="type-scale-section text-muted-foreground">Children</h3>
        {data?.children.length ? (
          <ul className="mt-2 space-y-1">
            {data.children.map((e) => (
              <li key={`${e.relation}-${e.subjectId}`} className="flex items-center gap-2 text-sm">
                <TypeBadge type={e.type} />
                <span>{e.title}</span>
                <span className="text-[0.6875rem] text-muted-foreground">{e.relation}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-2 text-sm text-muted-foreground">No children yet.</p>
        )}
      </section>
      <section>
        <h3 className="type-scale-section text-muted-foreground">Add relationship</h3>
        <div className="mt-2 flex flex-wrap gap-2">
          <Select value={rel} onValueChange={(v) => setRel(v as Relation)}>
            <SelectTrigger className="w-36">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="serves">serves</SelectItem>
              <SelectItem value="results_in">results_in</SelectItem>
              <SelectItem value="supersedes">supersedes</SelectItem>
            </SelectContent>
          </Select>
          <Select value={to} onValueChange={setTo}>
            <SelectTrigger className="min-w-48 flex-1">
              <SelectValue placeholder="Subject…" />
            </SelectTrigger>
            <SelectContent>
              {(subjects ?? [])
                .filter((s) => s.id !== subjectId)
                .slice(0, 40)
                .map((s) => (
                  <SelectItem key={s.id} value={s.id}>
                    {s.title}
                  </SelectItem>
                ))}
            </SelectContent>
          </Select>
          <Button
            size="sm"
            disabled={!to}
            onClick={() => {
              void writes.link(subjectId, rel, to);
              setTo("");
            }}
          >
            Link
          </Button>
        </div>
      </section>
    </div>
  );
}

function TagsTab({ subjectId }: { subjectId: string }) {
  const { data: tags } = useSubjectTags(subjectId);
  const { data: universe } = useTagUniverse();
  const writes = useWrites();
  const [draft, setDraft] = useState("");

  return (
    <div className="space-y-4 rounded-xl border border-dashed border-border p-3">
      <div>
        <h3 className="type-scale-section text-muted-foreground">Tags</h3>
        <p className="mt-1 text-xs text-muted-foreground">
          Classification, not structure. Relationships live on the other tab (GEN-1).
        </p>
      </div>
      <div className="flex flex-wrap gap-1.5">
        {(tags ?? []).length === 0 ? (
          <p className="text-sm text-muted-foreground">No tags yet.</p>
        ) : (
          (tags ?? []).map((t) => (
            <Tag key={t} onRemove={() => void writes.tag(subjectId, { remove: [t] })}>
              {t}
            </Tag>
          ))
        )}
      </div>
      <form
        className="flex gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (!draft.trim()) return;
          void writes.tag(subjectId, { add: [draft.trim()] });
          setDraft("");
        }}
      >
        <Input
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Add a tag"
          list={`tag-universe-${subjectId}`}
        />
        <datalist id={`tag-universe-${subjectId}`}>
          {(universe ?? []).map((t) => (
            <option key={t.tag} value={t.tag} />
          ))}
        </datalist>
        <Button type="submit" size="sm">
          Add
        </Button>
      </form>
    </div>
  );
}

function JournalTab({ subjectId }: { subjectId: string }) {
  const { data } = useJournal(subjectId);
  const writes = useWrites();
  const [text, setText] = useState("");

  return (
    <div className="space-y-4">
      <form
        className="space-y-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (!text.trim()) return;
          void writes.appendJournal(subjectId, text.trim());
          setText("");
        }}
      >
        <Label htmlFor="journal">Append an entry</Label>
        <Textarea
          id="journal"
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="Plain text for this pass — rich journals come later."
        />
        <Button type="submit" size="sm" disabled={!text.trim()}>
          Append
        </Button>
      </form>
      <Separator />
      {(data ?? []).length === 0 ? (
        <EmptyState title="No journal yet" description="Entries are append-only." />
      ) : (
        <ol className="space-y-3">
          {data!.map((e) => (
            <li key={e.eventId} className="rounded-lg bg-muted/50 px-3 py-2">
              <p className="text-[0.6875rem] text-muted-foreground">
                {formatTimestamp(e.occurredAt)}
              </p>
              <p className="mt-1 text-sm">{e.content}</p>
            </li>
          ))}
        </ol>
      )}
    </div>
  );
}

function HistoryTab({ subjectId }: { subjectId: string }) {
  const { data } = useHistory(subjectId);
  if (!data?.length) {
    return (
      <EmptyState
        title="No status history"
        description="Status moves only by event. Changes will appear here."
      />
    );
  }
  return (
    <ol className="relative ml-2 space-y-0 border-l border-border">
      {data.map((e) => (
        <li key={e.id} className="relative py-2 pl-4">
          <span className="absolute top-3.5 -left-1 size-2 rounded-full bg-primary" />
          <StatusPill status={e.status} />
          <p className="mt-1 text-[0.6875rem] text-muted-foreground">
            {formatTimestamp(e.occurredAt)}
          </p>
        </li>
      ))}
    </ol>
  );
}

function parseAttrBag(raw?: string | null): Record<string, string> {
  if (!raw) return {};
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const out: Record<string, string> = {};
    for (const [k, v] of Object.entries(parsed)) {
      if (typeof v === "string") out[k] = v;
    }
    return out;
  } catch {
    return {};
  }
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1.5">
      <p className="type-scale-section text-muted-foreground">{label}</p>
      {children}
    </div>
  );
}
