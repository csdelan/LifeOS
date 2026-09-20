import createClient from "openapi-fetch";
import type { paths } from "@/api/schema";
import { apiBaseUrl } from "@/lib/api-base";

let cached: ReturnType<typeof createClient<paths>> | null = null;

/** OpenAPI-typed fetch client. Generated types live in `web/src/api/schema.d.ts`. */
export function api() {
  cached ??= createClient<paths>({ baseUrl: apiBaseUrl() });
  return cached;
}

export async function unwrap<T>(
  result: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await result;
  if (!response.ok || error) {
    throw new Error(formatApiError(error, response));
  }
  return data as T;
}

function formatApiError(error: unknown, response: Response): string {
  if (error && typeof error === "object" && "error" in error && typeof error.error === "string") {
    return error.error;
  }
  return `API ${response.status} ${response.statusText}`.trim();
}
