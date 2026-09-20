import {
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";
import { useMemo } from "react";
import { lifeOsReads, type MapLens } from "@/lib/mock/api";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { NewSubjectRequest, SubjectType } from "@/lib/production-ui-types";

export const qk = {
  subjects: ["subjects"] as const,
  subject: (id: string) => ["subject", id] as const,
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
};

export function useSubjects(type?: SubjectType, includeArchived = false) {
  return useQuery({
    queryKey: [...qk.subjects, type ?? "all", includeArchived],
    queryFn: () => lifeOsReads.listSubjects({ type, includeArchived }),
  });
}

export function useSubject(id: string | undefined) {
  return useQuery({
    queryKey: qk.subject(id ?? ""),
    queryFn: () => lifeOsReads.getSubject(id!),
    enabled: !!id,
  });
}

export function useListItem(id: string | undefined) {
  return useQuery({
    queryKey: ["list-item", id],
    queryFn: () => lifeOsReads.getListItem(id!),
    enabled: !!id,
  });
}

export function useRelations(id: string | undefined) {
  return useQuery({
    queryKey: qk.relations(id ?? ""),
    queryFn: () => lifeOsReads.relations(id!),
    enabled: !!id,
  });
}

export function useSubjectTags(id: string | undefined) {
  return useQuery({
    queryKey: qk.tags(id ?? ""),
    queryFn: () => lifeOsReads.tags(id!),
    enabled: !!id,
  });
}

export function useTagUniverse() {
  return useQuery({
    queryKey: qk.tagUniverse,
    queryFn: () => lifeOsReads.tagUniverse(),
  });
}

export function useJournal(id: string | undefined) {
  return useQuery({
    queryKey: qk.journal(id ?? ""),
    queryFn: () => lifeOsReads.journal(id!),
    enabled: !!id,
  });
}

export function useHistory(id: string | undefined) {
  return useQuery({
    queryKey: qk.history(id ?? ""),
    queryFn: () => lifeOsReads.history(id!),
    enabled: !!id,
  });
}

export function useInbox() {
  return useQuery({
    queryKey: qk.inbox,
    queryFn: () => lifeOsReads.inbox(),
  });
}

export function useAreas() {
  return useQuery({
    queryKey: qk.areas,
    queryFn: () => lifeOsReads.areas(),
  });
}

export function usePeople() {
  return useQuery({
    queryKey: qk.people,
    queryFn: () => lifeOsReads.people(),
  });
}

export function useDashboard() {
  return useQuery({
    queryKey: qk.dashboard,
    queryFn: () => lifeOsReads.dashboard(),
  });
}

export function useAlignmentForest(includeArchived: boolean, lens: MapLens) {
  return useQuery({
    queryKey: qk.forest(includeArchived, lens),
    queryFn: () => lifeOsReads.alignmentForest({ includeArchived, lens }),
  });
}

export function useWrites() {
  const qc = useQueryClient();
  return useMemo(
    () =>
      createMockWriteClient(() => {
        void qc.invalidateQueries();
      }),
    [qc],
  );
}

export function useCreateSubject() {
  const writes = useWrites();
  return useMutation({
    mutationFn: (req: NewSubjectRequest) => writes.newSubject(req),
  });
}
