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
import { SubjectDetail } from "@/components/detail/subject-detail";
import { useCreateActions } from "@/components/create/create-context";
import { useDirty } from "@/components/shell/app-shell";
import { PeekPanel } from "@/components/shell/panels";
import { useAlignmentEdges, useAreas, useCreateSubject, useSubjects, useTagUniverse } from "@/lib/queries";
import { STATUS_BY_TYPE, defaultStatus } from "@/lib/production-ui-types";
import { todayIso } from "@/lib/dates";
import { NONE } from "@/lib/subject-forms";
import { TASK_GROUP_LABEL, TASK_GROUPS, groupTasks, isHiddenTaskStatus } from "@/lib/task-groups";
import { loadView, saveView } from "@/lib/view-state";

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

export function TasksPage() {
  const search = useSearch({ from: "/tasks" });
  const navigate = useNavigate({ from: "/tasks" });
  const selected = search.selected;
  const { data, isLoading, isError } = useSubjects("Task", true);
  const { data: allSubjects } = useSubjects(undefined, true);
  const { data: edges } = useAlignmentEdges(true);
  const { data: areas } = useAreas();
  const { data: tags } = useTagUniverse();
  const create = useCreateSubject();
  const { openNew } = useCreateActions();
  const { setDirty, requestNavigation } = useDirty();
  const [filters, setFilters] = useState<TasksView>(() => loadView("tasks", DEFAULT_VIEW));
  const [draft, setDraft] = useState("");
  const [entryError, setEntryError] = useState<string | null>(null);
  const entryRef = useRef<HTMLInputElement>(null);
  const today = todayIso();

  useEffect(() => {
    saveView("tasks", filters);
  }, [filters]);

  const parents = useMemo(
    () =>
      (allSubjects ?? []).filter(
        (s) => (s.type === "Goal" || s.type === "Project") && !s.archived,
      ),
    [allSubjects],
  );

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
    if (filters.tag) {
      rows = rows.filter((t) =>
        (t.tags ?? "")
          .split(",")
          .map((x) => x.trim())
          .includes(filters.tag),
      );
    }
    if (filters.parent) {
      rows = rows.filter((t) => (parentOf.get(t.id) ?? []).includes(filters.parent));
    }
    if (filters.due === "overdue") rows = rows.filter((t) => t.due && t.due < today);
    if (filters.due === "today") rows = rows.filter((t) => t.due === today);
    if (filters.due === "upcoming") rows = rows.filter((t) => t.due && t.due > today);
    if (filters.due === "none") rows = rows.filter((t) => !t.due);
    if (filters.scheduled === "today") rows = rows.filter((t) => t.scheduled === today);
    if (filters.scheduled === "upcoming") rows = rows.filter((t) => t.scheduled && t.scheduled > today);
    if (filters.scheduled === "none") rows = rows.filter((t) => !t.scheduled);
    return rows;
  }, [data, filters, parentOf, today]);

  const grouped = groupTasks(filtered, today);

  function select(id: string) {
    requestNavigation(() => {
      void navigate({ search: { selected: id } });
    });
  }

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
      setEntryError(e instanceof Error ? e.message : "Could not create the task.");
    }
  }

  return (
    <div className="flex h-full min-h-0">
      <div className="flex min-w-0 flex-1 flex-col">
        <header className="shrink-0 border-b px-4 py-4 md:px-6">
          <p className="type-scale-section text-primary">Tasks</p>
          <h1 className="font-heading text-2xl">Working view</h1>
          <p className="mt-1 text-xs text-muted-foreground">
            Overdue, Today, Upcoming, Unscheduled. Each task once. Title-only Enter creates a
            standalone Task.
          </p>
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
          <div className="mt-3 flex flex-wrap gap-2">
            <FilterSelect
              label="Status"
              value={filters.status}
              onChange={(status) => setFilters((f) => ({ ...f, status }))}
              options={[
                { value: "active", label: "Active" },
                { value: "all", label: "All" },
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
              value={filters.tag || NONE}
              onChange={(tag) => setFilters((f) => ({ ...f, tag: tag === NONE ? "" : tag }))}
              options={[
                { value: NONE, label: "Any tag" },
                ...(tags ?? []).map((t) => ({ value: t.tag, label: t.tag })),
              ]}
            />
          </div>
        </header>
        <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4">
          {isLoading ? <ListSkeleton rows={8} /> : null}
          {isError ? (
            <EmptyState
              tone="error"
              icon={ListTodoIcon}
              title="Tasks failed to load"
              description="This is not an empty list — the reader never returned."
            />
          ) : null}
          {!isLoading && filtered.length === 0 ? (
            <EmptyState
              icon={ListTodoIcon}
              title="No tasks in this filter"
              description="Clear filters or add a title above."
            />
          ) : null}
          {TASK_GROUPS.map((g) =>
            grouped[g].length === 0 ? null : (
              <section key={g} className="mb-5">
                <h2 className="type-scale-section mb-1 px-2 text-muted-foreground">
                  {TASK_GROUP_LABEL[g]}
                  <span className="ml-2 tabular-nums">{grouped[g].length}</span>
                </h2>
                {grouped[g].map((item) => (
                  <ListRow
                    key={item.id}
                    item={item}
                    selected={selected === item.id}
                    onSelect={() => select(item.id)}
                  />
                ))}
              </section>
            ),
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
