/** The Measurements feature's reads, as TanStack Query hooks. */
import { keepPreviousData, useQueries, useQuery } from "@tanstack/react-query";
import { getReads } from "@/lib/client";
import { measurementKeys } from "@/lib/features/measurements/keys";

/** The starter list, from the kernel (code review CR73): fixed while the app runs. */
export function useMetricCatalog() {
  return useQuery({
    queryKey: measurementKeys.metricCatalog,
    queryFn: () => getReads().metricCatalog(),
    staleTime: Infinity,
  });
}

/** Every metric, archived ones too: the Measurements page finds Weight among them and groups the rest. */
export function useMetrics(enabled = true) {
  return useQuery({
    queryKey: measurementKeys.metrics,
    queryFn: () => getReads().metrics(),
    enabled,
  });
}

/**
 * One metric over the last `days` days (all history when null): the chart, the table and the
 * goal; the last range stays up while the next loads.
 */
export function useMetric(metric: string | null, days: number | null, enabled = true) {
  return useQuery({
    queryKey: measurementKeys.metric(metric ?? "", days),
    queryFn: () => getReads().metric(metric!, days),
    enabled: enabled && metric != null,
    placeholderData: keepPreviousData,
  });
}

/**
 * Several metrics over the same range, read together: blood pressure's two on one card and in
 * one row (MEASURE-3). Each shares `useMetric`'s cache, so a card and its row read once.
 */
export function useMetricsOver(metrics: readonly string[], days: number | null, enabled = true) {
  return useQueries({
    queries: metrics.map((metric) => ({
      queryKey: measurementKeys.metric(metric, days),
      queryFn: () => getReads().metric(metric, days),
      enabled,
      placeholderData: keepPreviousData,
    })),
    combine: (results) => ({
      data: results.every((r) => r.data) ? results.map((r) => r.data!) : undefined,
      isLoading: results.some((r) => r.isLoading),
      isError: results.some((r) => r.isError),
      isPlaceholderData: results.some((r) => r.isPlaceholderData),
    }),
  });
}

/** Every lab visit with its results, newest first (MEASURE-4): the Lab results section and the visit sheet. */
export function useLabVisits(enabled = true) {
  return useQuery({
    queryKey: measurementKeys.labVisits,
    queryFn: () => getReads().labVisits(),
    enabled,
  });
}
