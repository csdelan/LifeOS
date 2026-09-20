import { api, unwrap } from "@/lib/live/http";

export type Me = { email: string };

export const meQueryKey = ["me"] as const;

export function fetchMe(): Promise<Me> {
  return unwrap(api().GET("/api/me"));
}
