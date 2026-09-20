import { createContext, useContext } from "react";
import type { SubjectType } from "@/lib/production-ui-types";

export type CreateOpts = {
  type?: SubjectType;
  parent?: { id: string; title: string; type: SubjectType };
  title?: string;
};

export const CreateActions = createContext<{
  openNew: (opts?: CreateOpts) => void;
  openCapture: () => void;
}>({
  openNew: () => {},
  openCapture: () => {},
});

export function useCreateActions() {
  return useContext(CreateActions);
}
