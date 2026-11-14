import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { ListTodoIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { EmptyState } from "@/components/primitives/empty-state";
import { ListRow } from "@/components/primitives/list-row";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { ShowMore } from "@/components/primitives/show-more";
import { usePaged } from "@/components/primitives/use-paged";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { useCreateActions } from "@/components/create/create-context";
import { useDirty } from "@/components/shell/unsaved-edits";
import { PeekPanel } from "@/components/shell/panels";
import { TimerChip } from "@/components/timer/task-timer";
import { SeriesList } from "@/components/tasks/series-list";
import { useTaskSettleOnOpen } from "@/components/tasks/use-task-settle-on-open";
import {
  useAlignmentEdges,
  useAreas,
  useCreateSubject,
  useSubjects,
  useSubjectsOfTypes,
  useTagUniverse,
  useTimers,
} from "@/lib/queries";
import { STATUS_BY_TYPE, defaultStatus, type SubjectListItem, type SubjectType } from "@/lib/production-ui-types";
import { todayIso } from "@/lib/dates";
import { NONE } from "@/lib/subject-forms";
import {
  TASK_GROUP_LABEL,
  TASK_GROUPS,
  groupTasks,
  isHiddenTaskStatus,
  isOverdueTask,
  type TaskGroup,
} from "@/lib/task-groups";
import { loadView, saveView } from "@/lib/view-state";
import { errorText } from "@/lib/write-action";
import { ReadFailed } from "@/components/primitives/read-state";

type TasksView = {
  status: string;
  due: string;
  scheduled: string;
  area: string;
  parent: string;
  tag: string;
};

const DEFAULT_VIEW: TasksView = {
  status: "active",
  due: "any",
  scheduled: "any",
  area: "",
  parent: "",
  tag: "",
};

/** What a Task's parent filter offers. */
const PARENT_TYPES: readonly SubjectType[] = ["Goal", "Project"];
const NO_PARENTS: SubjectListItem[] = [];

export function TasksPage() {
  const search = useSearch({ from: "/tasks" });
  const navigate = useNavigate({ from: "/tasks" });
  const selected = search.selected;
  const { data: parentRows } = useSubjectsOfTypes(PARENT_TYPES);
  const { data: edges } = useAlignmentEdges(true);
  const { data: areas } = useAreas();
  const { data: tags } = useTagUniverse();
  const { data: timers } = useTimers();
  const { setDirty, requestNavigation } = useDirty();
  const [filters, setFilters] = useState<TasksView>(() => loadView("tasks", DEFAULT_VIEW));
  const activeView = filters.status === "active";
  const repeatingView = filters.status === "repeating";
  // Active, the view it opens on, reads open Tasks only, and a view of one status reads that
  // status only, not every repetition ever done; All alone reads every Task (code review CR76,
  // CR99). A Task with no status yet is Not started, which only the open read finds.
  const oneStatus = !activeView && !repeatingView && filters.status !== "all" ? filters.status : undefined;
  const tasks = useSubjects("Task", !activeView, false, {
    status: activeView || oneStatus === defaultStatus("Task") ? "open" : oneStatus,
    enabled: !repeatingView,
  });
  const { data, isLoading } = tasks;
  const today = todayIso();
  // A repeating Task's date that passed while the app was closed gets its repetition (TASKS-3).
  useTaskSettleOnOpen();

  // A saved tag filter whose tag was renamed (or is no longer used) falls back to
  // "Any tag" rather than silently hiding every task.
  const tagFilter =
    filters.tag && tags && !tags.some((t) => t.tag === filters.tag) ? "" : filters.tag;

  useEffect(() => {
    saveView("tasks", filters);
  }, [filters]);

  const parents = parentRows ?? NO_PARENTS;

  const parentOf = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const e of edges ?? []) {
      const kids = map.get(e.fromId) ?? [];
      kids.push(e.toId);
      map.set(e.fromId, kids);
    }
    return map;
  }, [edges]);

  const filtered = useMemo(() => {
    let rows = (data ?? []).filter((t) => t.type === "Task");
    if (filters.status === "active") {
      rows = rows.filter((t) => !t.archived && !isHiddenTaskStatus(t.status || defaultStatus("Task")));
    } else if (filters.status && filters.status !== "all") {
      rows = rows.filter((t) => (t.status || defaultStatus("Task")) === filters.status);
    }
    if (filters.area) rows = rows.filter((t) => t.area === filters.area);
    if (tagFilter) {
      rows = rows.filter((t) =>
        (t.tags ?? "")
          .split(",")
          .map((x) => x.trim())
          .includes(tagFilter),
      );
    }
    if (filters.parent) {
      rows = rows.filter((t) => (parentOf.get(t.id) ?? []).includes(filters.parent));
    }
    if (filters.due === "overdue") rows = rows.filter((t) => isOverdueTask(t, today));
    if (filters.due === "today") rows = rows.filter((t) => t.due === today);
    if (filters.due === "upcoming") rows = rows.filter((t) => t.due && t.due > today);
    if (filters.due === "none") rows = rows.filter((t) => !t.due);
    if (filters.scheduled === "today") rows = rows.filter((t) => t.scheduled === today);
    if (filters.scheduled === "upcoming") rows = rows.filter((t) => t.scheduled && t.scheduled > today);
    if (filters.scheduled === "none") rows = rows.filter((t) => !t.scheduled);
    return rows;
  }, [data, filters, tagFilter, parentOf, today]);

  // Grouped and sorted once per change to the list, not on every render (code review CR99).
  const grouped = useMemo(() => groupTasks(filtered, today), [filtered, today]);
  const ordered = useMemo(() => TASK_GROUPS.flatMap((g) => grouped[g]), [grouped]);
  const paged = usePaged(
    ordered,
    [filters.status, filters.due, filters.scheduled, filters.area, filters.parent, tagFilter].join("|"),
    selected,
  );
  // The groups share the page in their order: a group past it waits for Show more.
  const drawnIn = {} as Record<TaskGroup, number>;
  let drawLeft = paged.drawn;
  for (const g of TASK_GROUPS) {
    drawnIn[g] = Math.max(0, Math.min(grouped[g].length, drawLeft));
    drawLeft -= grouped[g].length;
  }
  const timerById = useMemo(
    () => new Map((timers ?? []).map((t) => [t.subjectId, t])),
    [timers],
  );

  function select(id: string) {
    requestNavigation(() => {
      void navigate({ search: { selected: id } });
    });
  }

  return (
    <div className="flex h-full min-h-0">
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="shrink-0 border-b px-4 py-4 md:px-6">
          <p className="type-scale-section text-primary">Tasks</p>
          <h1 className="font-heading text-2xl">Working view</h1>
          <p className="mt-1 text-xs text-muted-foreground">
            Overdue, Today, Upcoming, Unscheduled. Each task once; a repeating task shows its
            current repetition. Title-only Enter creates a standalone Task.
          </p>
          <QuickTaskEntry />
          <div className="mt-3 flex flex-wrap gap-2">
            <FilterSelect
              label="Status"
              value={filters.status}
              onChange={(status) => setFilters((f) => ({ ...f, status }))}
              options={[
                { value: "active", label: "Active" },
                { value: "all", label: "All" },
                { value: "repeating", label: "Repeating" },
                ...(STATUS_BY_TYPE.Task ?? []).map((s) => ({ value: s, label: s })),
              ]}
            />
            <FilterSelect
              label="Due"
              value={filters.due}
              onChange={(due) => setFilters((f) => ({ ...f, due }))}
              options={[
                { value: "any", label: "Any due" },
                { value: "overdue", label: "Overdue" },
                { value: "today", label: "Due today" },
                { value: "upcoming", label: "Upcoming due" },
                { value: "none", label: "No due" },
              ]}
            />
            <FilterSelect
              label="Scheduled"
              value={filters.scheduled}
              onChange={(scheduled) => setFilters((f) => ({ ...f, scheduled }))}
              options={[
                { value: "any", label: "Any scheduled" },
                { value: "today", label: "Do today" },
                { value: "upcoming", label: "Upcoming do" },
                { value: "none", label: "Unscheduled" },
              ]}
            />
            <FilterSelect
              label="Area"
              value={filters.area || NONE}
              onChange={(area) => setFilters((f) => ({ ...f, area: area === NONE ? "" : area }))}
              options={[
                { value: NONE, label: "All areas" },
                ...(areas ?? []).map((a) => ({ value: a.urn, label: a.name })),
              ]}
            />
            <FilterSelect
              label="Goal / Project"
              value={filters.parent || NONE}
              onChange={(parent) =>
                setFilters((f) => ({ ...f, parent: parent === NONE ? "" : parent }))
              }
              options={[
                { value: NONE, label: "Any parent" },
                ...parents.map((p) => ({ value: p.id, label: p.title })),
              ]}
            />
            <FilterSelect
              label="Tag"
              value={tagFilter || NONE}
              onChange={(tag) => setFilters((f) => ({ ...f, tag: tag === NONE ? "" : tag }))}
              options={[
                { value: NONE, label: "Any tag" },
                ...(tags ?? []).map((t) => ({ value: t.tag, label: t.tag })),
              ]}
            />
          </div>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">
          {repeatingView ? (
            <SeriesList area={filters.area} selected={selected} onSelect={select} />
          ) : (
            <>
              {isLoading ? <ListSkeleton rows={8} /> : null}
              <ReadFailed
                read={tasks}
                what="Tasks"
                notEmpty="an empty list"
                size="page"
                icon={ListTodoIcon}
              />
              {!isLoading && filtered.length === 0 ? (
                <EmptyState
                  icon={ListTodoIcon}
                  title="No tasks in this filter"
                  description="Clear filters or add a title above."
                />
              ) : null}
              {TASK_GROUPS.map((g) =>
                drawnIn[g] === 0 ? null : (
                  <section key={g} className="mb-5">
                    <h2 className="type-scale-section mb-1 px-2 text-muted-foreground">
                      {TASK_GROUP_LABEL[g]}
                      <span className="ml-2 tabular-nums">{grouped[g].length}</span>
                    </h2>
                    {grouped[g].slice(0, drawnIn[g]).map((item) => (
                      <ListRow
                        key={item.id}
                        item={item}
                        selected={selected === item.id}
                        onSelect={() => select(item.id)}
                        trailing={<TimerChip timer={timerById.get(item.id)} />}
                      />
                    ))}
                  </section>
                ),
              )}
              <ShowMore {...paged} />
            </>
          )}
        </div>
      </div>
      <PeekPanel
        open={!!selected}
        title="Task detail"
        onClose={() =>
          requestNavigation(() =>
            void navigate({ search: { selected: undefined } }),
          )
        }
      >
        {selected ? (
          <SubjectDetail
            subjectId={selected}
            onDirtyChange={setDirty}
            onOpenSubject={select}
            onClose={() =>
              requestNavigation(() =>
                void navigate({ search: { selected: undefined } }),
              )
            }
          />
        ) : null}
      </PeekPanel>
    </div>
  );
}

/**
 * The title-only entry: Enter makes a standalone Task, More… opens the New dialog with the title.
 * It holds what's typed itself, so typing redraws the entry alone, not every Task listed under it
 * (code review CR99).
 */
function QuickTaskEntry() {
  const create = useCreateSubject();
  const { openNew } = useCreateActions();
  const [draft, setDraft] = useState("");
  const [entryError, setEntryError] = useState<string | null>(null);
  const entryRef = useRef<HTMLInputElement>(null);

  async function quickCreate() {
    const title = draft.trim();
    if (!title) return;
    setEntryError(null);
    try {
      await create.mutateAsync({
        type: "Task",
        title,
        attrs: { priority: "Medium" },
      });
      setDraft("");
      entryRef.current?.focus();
    } catch (e) {
      setEntryError(errorText(e, "Could not create the task."));
    }
  }

  return (
    <>
      <form
        className="mt-3 flex gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          void quickCreate();
        }}
      >
        <Input
          ref={entryRef}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="New task — Enter to create"
          aria-label="New task title"
          className="flex-1"
        />
        <Button type="submit" size="sm" disabled={!draft.trim() || create.isPending}>
          Add
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => openNew({ type: "Task", title: draft })}
        >
          More…
        </Button>
      </form>
      {entryError ? <p className="mt-1 text-xs text-destructive">{entryError}</p> : null}
    </>
  );
}

function FilterSelect({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
}) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger size="sm" className="h-7 min-w-32" aria-label={label}>
        <SelectValue placeholder={label} />
      </SelectTrigger>
      <SelectContent>
        {options.map((o) => (
          <SelectItem key={o.value} value={o.value}>
            {o.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
