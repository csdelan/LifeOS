import {
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
} from "@tanstack/react-query";
import { useMemo } from "react";
import { createWriteClient, getReads } from "@/lib/client";
import { mapForestStatus, type DashboardRead, type MapLens } from "@/lib/derive";
import type { LifeOsReads } from "@/lib/mock/api";
import type {
  AdherenceState,
  InboxItem,
  LifeOsWriteClient,
  NewSubjectRequest,
  SubjectDetail,
  SubjectListItem,
  SubjectType,
} from "@/lib/production-ui-types";

const reads: LifeOsReads = getReads();

export const qk = {
  subjects: ["subjects"] as const,
  subject: (id: string) => ["subject", id] as const,
  listItem: (id: string) => ["list-item", id] as const,
  relations: (id: string) => ["relations", id] as const,
  tags: (id: string) => ["tags", id] as const,
  tagUniverse: ["tag-universe"] as const,
  journal: (id: string) => ["journal", id] as const,
  history: (id: string) => ["history", id] as const,
  inbox: ["inbox"] as const,
  areas: ["areas"] as const,
  people: ["people"] as const,
  habits: ["habits"] as const,
  dashboard: ["dashboard"] as const,
  forest: (archived: boolean, lens: MapLens) =>
    ["forest", archived, lens] as const,
  edges: (archived: boolean) => ["edges", archived] as const,
};

function invalidate(qc: QueryClient, keys: readonly (readonly unknown[])[]) {
  return Promise.all(keys.map((queryKey) => qc.invalidateQueries({ queryKey })));
}

function decorateWrites(client: LifeOsWriteClient, qc: QueryClient): LifeOsWriteClient {
  return {
    async newSubject(req) {
      const created = await client.newSubject(req);
      const extra =
        req.type === "Idea" || req.type === "Problem"
          ? [qk.inbox]
          : req.type === "Area"
            ? [qk.areas]
            : req.type === "Person"
              ? [qk.people]
              : req.type === "Habit"
                ? [qk.habits]
                : [];
      await invalidate(qc, [qk.subjects, qk.dashboard, ["forest"], ["edges"], ...extra]);
      return created;
    },

    async promote(source, type, title) {
      const created = await client.promote(source, type, title);
      await invalidate(qc, [qk.inbox, qk.subjects, qk.dashboard, ["forest"], ["edges"]]);
      return created;
    },

    async setAttributes(subject, attrs) {
      await client.setAttributes(subject, attrs);
      await invalidate(qc, [
        qk.subject(subject),
        qk.listItem(subject),
        qk.subjects,
        qk.dashboard,
        ["forest"],
        ["edges"],
      ]);
    },

    async setStatus(subject, status) {
      optimisticStatus(qc, subject, status);
      try {
        await client.setStatus(subject, status);
      } finally {
        await invalidate(qc, [
          qk.subject(subject),
          qk.listItem(subject),
          qk.history(subject),
          qk.subjects,
          qk.dashboard,
          qk.inbox,
          ["forest"],
          ["edges"],
        ]);
      }
    },

    async archive(subject) {
      await client.archive(subject);
      await invalidate(qc, [
        qk.subject(subject),
        qk.listItem(subject),
        qk.subjects,
        qk.dashboard,
        qk.inbox,
        ["forest"],
        ["edges"],
      ]);
    },

    async restore(subject) {
      await client.restore(subject);
      await invalidate(qc, [
        qk.subject(subject),
        qk.listItem(subject),
        qk.subjects,
        qk.dashboard,
        ["forest"],
        ["edges"],
      ]);
    },

    async tag(item, opts) {
      await client.tag(item, opts);
      await invalidate(qc, [qk.tags(item), qk.tagUniverse, qk.subjects]);
    },

    async link(from, relation, to) {
      await client.link(from, relation, to);
      await invalidate(qc, [
        qk.relations(from),
        qk.relations(to),
        ["forest"],
        ["edges"],
        qk.dashboard,
      ]);
    },

    async relate(eventId, subject, as) {
      await client.relate(eventId, subject, as);
      await invalidate(qc, [qk.inbox, qk.journal(subject), qk.relations(subject)]);
    },

    async flag(item) {
      await client.flag(item);
      await invalidate(qc, [qk.inbox, qk.dashboard]);
    },

    async dismiss(item) {
      optimisticInboxRemove(qc, item);
      try {
        await client.dismiss(item);
      } finally {
        await invalidate(qc, [qk.inbox, qk.dashboard]);
      }
    },

    async drop(item) {
      optimisticInboxRemove(qc, item);
      try {
        await client.drop(item);
      } finally {
        await invalidate(qc, [qk.inbox, qk.dashboard, qk.subjects, ["forest"], ["edges"]]);
      }
    },

    async adhere(habit, state, opts) {
      optimisticAdhere(qc, habit, state === "missed" ? "not_followed" : state);
      try {
        await client.adhere(habit, state, opts);
      } finally {
        await invalidate(qc, [qk.habits, qk.dashboard]);
      }
    },

    async involve(subject, person, role, remove) {
      await client.involve(subject, person, role, remove);
      await invalidate(qc, [qk.subject(subject), qk.people]);
    },

    async appendJournal(subject, text) {
      await client.appendJournal(subject, text);
      await invalidate(qc, [qk.journal(subject)]);
    },

    async capture(text) {
      await client.capture(text);
      await invalidate(qc, [qk.inbox, qk.dashboard]);
    },
  };
}

function matchesRef(id: string, urn: string, ref: string) {
  return id === ref || urn === ref;
}

function optimisticStatus(qc: QueryClient, subject: string, status: string) {
  qc.setQueriesData<SubjectListItem[]>(
    { queryKey: qk.subjects },
    (old) =>
      old?.map((s) =>
        matchesRef(s.id, s.urn, subject) ? { ...s, status } : s,
      ),
  );
  qc.setQueryData<SubjectDetail | null>(qk.subject(subject), (old) =>
    old ? { ...old, status } : old,
  );
  qc.setQueryData<SubjectListItem | null>(qk.listItem(subject), (old) =>
    old ? { ...old, status } : old,
  );
  qc.setQueriesData({ queryKey: ["forest"] }, (old) => {
    if (!Array.isArray(old)) return old;
    return mapForestStatus(old, subject, status);
  });
}

function optimisticInboxRemove(qc: QueryClient, item: string) {
  qc.setQueryData<InboxItem[]>(qk.inbox, (old) =>
    old?.filter(
      (i) => i.itemId !== item && i.subjectUrn !== item && i.subjectTitle !== item,
    ),
  );
  qc.setQueryData<DashboardRead>(qk.dashboard, (old) =>
    old ? { ...old, inboxCount: Math.max(0, old.inboxCount - 1) } : old,
  );
}

function optimisticAdhere(
  qc: QueryClient,
  habit: string,
  state: Exclude<AdherenceState, "unrecorded">,
) {
  qc.setQueryData<DashboardRead>(qk.dashboard, (old) => {
    if (!old) return old;
    return {
      ...old,
      todayHabits: old.todayHabits.map((h) =>
        h.habitId === habit || h.habitUrn === habit ? { ...h, state } : h,
      ),
    };
  });
}

export function useSubjects(type?: SubjectType, includeArchived = false) {
  return useQuery({
    queryKey: [...qk.subjects, type ?? "all", includeArchived],
    queryFn: () => reads.listSubjects({ type, includeArchived }),
  });
}

export function useSubject(id: string | undefined) {
  return useQuery({
    queryKey: qk.subject(id ?? ""),
    queryFn: () => reads.getSubject(id!),
    enabled: !!id,
  });
}

export function useListItem(id: string | undefined) {
  return useQuery({
    queryKey: qk.listItem(id ?? ""),
    queryFn: () => reads.getListItem(id!),
    enabled: !!id,
  });
}

export function useRelations(id: string | undefined) {
  return useQuery({
    queryKey: qk.relations(id ?? ""),
    queryFn: () => reads.relations(id!),
    enabled: !!id,
  });
}

export function useSubjectTags(id: string | undefined) {
  return useQuery({
    queryKey: qk.tags(id ?? ""),
    queryFn: () => reads.tags(id!),
    enabled: !!id,
  });
}

export function useTagUniverse() {
  return useQuery({
    queryKey: qk.tagUniverse,
    queryFn: () => reads.tagUniverse(),
  });
}

export function useJournal(id: string | undefined) {
  return useQuery({
    queryKey: qk.journal(id ?? ""),
    queryFn: () => reads.journal(id!),
    enabled: !!id,
  });
}

export function useHistory(id: string | undefined) {
  return useQuery({
    queryKey: qk.history(id ?? ""),
    queryFn: () => reads.history(id!),
    enabled: !!id,
  });
}

export function useInbox() {
  return useQuery({
    queryKey: qk.inbox,
    queryFn: () => reads.inbox(),
  });
}

export function useAreas() {
  return useQuery({
    queryKey: qk.areas,
    queryFn: () => reads.areas(),
  });
}

export function usePeople() {
  return useQuery({
    queryKey: qk.people,
    queryFn: () => reads.people(),
  });
}

export function useDashboard() {
  return useQuery({
    queryKey: qk.dashboard,
    queryFn: () => reads.dashboard(),
  });
}

export function useAlignmentForest(includeArchived: boolean, lens: MapLens) {
  return useQuery({
    queryKey: qk.forest(includeArchived, lens),
    queryFn: () => reads.alignmentForest({ includeArchived, lens }),
  });
}

export function useAlignmentEdges(includeArchived: boolean) {
  return useQuery({
    queryKey: qk.edges(includeArchived),
    queryFn: () => reads.edges({ includeArchived }),
  });
}

export function useWrites() {
  const qc = useQueryClient();
  return useMemo(() => decorateWrites(createWriteClient(), qc), [qc]);
}

export function useCreateSubject() {
  const writes = useWrites();
  return useMutation({
    mutationFn: (req: NewSubjectRequest) => writes.newSubject(req),
  });
}
