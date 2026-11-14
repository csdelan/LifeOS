/** The Measurements feature's reads: what the read seam asks for, and how the live app asks the API. */
import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";
import type {
  LabVisitsRead,
  MetricDetailRead,
  MetricRow,
  MetricTemplate,
} from "@/lib/features/measurements/types";

export interface MeasurementReads {
  /**
   * The starter list (measurements plan §2.1, `GET /api/measurements/catalog`): what making each
   * metric and lab test by its name fills in, in the list's order (code review CR73).
   */
  metricCatalog(): Promise<MetricTemplate[]>;
  /** Every metric (MEASURE-1, `GET /api/measurements/metrics`), archived ones included, by name. */
  metrics(): Promise<MetricRow[]>;
  /**
   * One metric (its id or name) over the last `days` days, all history when null
   * (`GET /api/measurements/metrics/{id}`): readings newest first, day values with the trend,
   * and the goal.
   */
  metric(metric: string, days: number | null): Promise<MetricDetailRead>;
  /**
   * Every lab visit with its results, newest collected first (MEASURE-4,
   * `GET /api/measurements/visits`). A report's bytes come from `artifactBytesUrl`.
   */
  labVisits(): Promise<LabVisitsRead>;
}

export const liveMeasurementReads: MeasurementReads = {
  async metricCatalog() {
    return fits<MetricTemplate[]>()(await unwrap(api().GET("/api/measurements/catalog")));
  },

  async metrics() {
    return fits<MetricRow[]>()(await unwrap(api().GET("/api/measurements/metrics")));
  },

  async metric(metric, days) {
    const data = await unwrap(
      api().GET("/api/measurements/metrics/{id}", {
        params: { path: { id: metric }, query: { days: days ?? undefined } },
      }),
    );
    return fits<MetricDetailRead>()(data);
  },

  async labVisits() {
    return fits<LabVisitsRead>()(await unwrap(api().GET("/api/measurements/visits")));
  },
};
