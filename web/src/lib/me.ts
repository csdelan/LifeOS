import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";

export type Me = { email: string };

export const meQueryKey = ["me"] as const;

export async function fetchMe(): Promise<Me> {
  return fits<Me>()(await unwrap(api().GET("/api/me")));
}
