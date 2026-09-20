import createClient from "openapi-fetch";
import type { paths } from "@/api/schema";
import { apiBaseUrl } from "@/lib/api-base";
import { getAccessToken } from "@/lib/auth-token";

let cached: ReturnType<typeof createClient<paths>> | null = null;

export class ApiRequestError extends Error {
  readonly status: number;

  constructor(message: string, status: number) {
    super(message);
    this.name = "ApiRequestError";
    this.status = status;
  }
}

/** OpenAPI-typed fetch client. Generated types live in `web/src/api/schema.d.ts`. */
export function api() {
  if (!cached) {
    cached = createClient<paths>({ baseUrl: apiBaseUrl() });
    cached.use({
      async onRequest({ request }) {
        const token = await getAccessToken();
        if (!token) return request;
        const headers = new Headers(request.headers);
        headers.set("Authorization", `Bearer ${token}`);
        return new Request(request, { headers });
      },
    });
  }
  return cached;
}

export function resetApiClient() {
  cached = null;
}

export async function unwrap<T>(
  result: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await result;
  if (!response.ok || error) {
    throw new ApiRequestError(formatApiError(error, response), response.status);
  }
  return data as T;
}

function formatApiError(error: unknown, response: Response): string {
  if (error && typeof error === "object" && "error" in error && typeof error.error === "string") {
    return error.error;
  }
  return `API ${response.status} ${response.statusText}`.trim();
}
