/**
 * Single factory for the LifeOS data seam.
 *
 * `VITE_API_BASE_URL` unset → in-memory mock (default, unchanged).
 * Set (e.g. http://localhost:5280) → live LifeOs.Api.
 */

import { isLiveApi } from "@/lib/api-base";
import { liveReads } from "@/lib/live/reads";
import { createLiveWriteClient } from "@/lib/live/write-client";
import { mockReads, type LifeOsReads } from "@/lib/mock/api";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient } from "@/lib/production-ui-types";

export { isLiveApi };

export function getReads(): LifeOsReads {
  return isLiveApi() ? liveReads : mockReads;
}

export function createWriteClient(): LifeOsWriteClient {
  return isLiveApi() ? createLiveWriteClient() : createMockWriteClient();
}
