import { createContext, useContext } from "react";
import type { CreatedSubject, SubjectType } from "@/lib/production-ui-types";
import type { PendingLink } from "@/lib/decisions";

export type CreateOpts = {
  type?: SubjectType;
  parent?: { id: string; title: string; type: SubjectType };
  title?: string;
  /** A Decision link saved with the new item and shown before Save (GEN-13). */
  link?: PendingLink;
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
