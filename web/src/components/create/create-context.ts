import { createContext, useContext } from "react";
import type { CreatedSubject, SubjectType } from "@/lib/production-ui-types";

export type CreateOpts = {
  type?: SubjectType;
  parent?: { id: string; title: string; type: SubjectType };
  title?: string;
  /** Told once the subject exists (Browse selects it, BROWSE-2). */
  onCreated?: (created: CreatedSubject) => void;
};

export const CreateActions = createContext<{
  openNew: (opts?: CreateOpts) => void;
  openCapture: () => void;
  openVoice: () => void;
}>({
  openNew: () => {},
  openCapture: () => {},
  openVoice: () => {},
});

export function useCreateActions() {
  return useContext(CreateActions);
}
