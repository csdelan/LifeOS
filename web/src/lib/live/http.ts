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

declare const unchecked: unique symbol;

/**
 * An API response whose generated type is `G`, not yet checked against the app's: only `fits`
 * opens it (`lib/live/fits.ts`), so a live call can't read what the API sent as the app's type
 * without tsc checking the two fit (code review CR82). A write whose answer is ignored just
 * awaits it.
 */
export type Unchecked<G> = { readonly [unchecked]: G };

/** The response's data, or an `ApiRequestError` with the API's message and status. */
export async function unwrap<T>(
  result: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<Unchecked<T>> {
  const { data, error, response } = await result;
  if (!response.ok || error) {
    throw new ApiRequestError(formatApiError(error, response), response.status);
  }
  return data as unknown as Unchecked<T>;
}

/** As `unwrap`, but null when the API answers 404: a read of one thing that may not be there. */
export async function unwrapOrNull<T>(
  result: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<Unchecked<T> | null> {
  const { data, error, response } = await result;
  if (response.status === 404) return null;
  if (!response.ok || error || data === undefined) {
    throw new ApiRequestError(formatApiError(error, response), response.status);
  }
  return data as unknown as Unchecked<T>;
}

/** A response read as bytes (`parseAs: "blob"`), which has no JSON to check. */
export async function unwrapBlob(
  result: Promise<{ data?: unknown; error?: unknown; response: Response }>,
): Promise<Blob> {
  const { data, error, response } = await result;
  if (!response.ok || error || !(data instanceof Blob)) {
    throw new ApiRequestError(formatApiError(error, response), response.status);
  }
  return data;
}

function formatApiError(error: unknown, response: Response): string {
  if (error && typeof error === "object" && "error" in error && typeof error.error === "string") {
    return error.error;
  }
  return `API ${response.status} ${response.statusText}`.trim();
}
