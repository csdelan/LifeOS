/** The Measurements feature's cached reads (`lib/query-keys.ts` holds every feature's). */
export const measurementKeys = {
  // Every Measurements read sits under ["measurements"], so a metric or a reading write refreshes them all.
  measurements: ["measurements"] as const,
  metrics: ["measurements", "metrics"] as const,
  metric: (metric: string, days: number | null) => ["measurements", "metric", metric, days ?? "all"] as const,
  labVisits: ["measurements", "visits"] as const,
  // Outside ["measurements"]: the starter list is the kernel's, so no write changes it.
  metricCatalog: ["metric-catalog"] as const,
};
