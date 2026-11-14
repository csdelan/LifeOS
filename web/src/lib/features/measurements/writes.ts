/**
 * The Measurements feature's writes: what the write seam offers, how the live app sends each to
 * the API, and what each does besides itself (`lib/write-effects.ts`).
 */
import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";
import { qk } from "@/lib/query-keys";
import type { Effect } from "@/lib/write-effects";
import type {
  LabVisitRequest,
  LabVisitResult,
  MetricReadingBatchRequest,
  MetricReadingEditRequest,
  MetricReadingRequest,
  MetricReadingResult,
  MetricReadingsResult,
  MetricRequest,
  MetricRow,
} from "@/lib/features/measurements/types";

export interface MeasurementWrites {
  /**
   * Add a metric (MEASURE-1, `POST /api/measurements/metrics`): a starter-list name (Weight)
   * fills in the rest. Archive one with `archive(urn)`.
   */
  addMetric(req: MetricRequest & { name: string }): Promise<MetricRow>;
  /** Rename a metric or change its unit, decimals, target, direction or normal range (`POST …/metrics/{id}`). */
  setMetric(metric: string, req: MetricRequest): Promise<MetricRow>;
  /**
   * Log a reading (`POST /api/measurements/readings`), now or at any past time. Refused, with
   * nothing written, for a future time, too many decimal places, or an archived metric.
   */
  logReading(req: MetricReadingRequest): Promise<MetricReadingResult>;
  /**
   * Log readings of several metrics at one time with one note (`POST …/readings/batch`), all or
   * nothing: blood pressure's systolic and diastolic. Refused as `logReading` is, and for a
   * metric given twice.
   */
  logReadings(req: MetricReadingBatchRequest): Promise<MetricReadingsResult>;
  /** Change a reading's value, time or note (`POST …/readings/{id}`). */
  editReading(reading: string, req: MetricReadingEditRequest): Promise<MetricReadingResult>;
  /** Delete a reading; the record keeps its versions (`POST …/readings/{id}/delete`). */
  deleteReading(reading: string): Promise<MetricReadingResult>;
  /**
   * Log a lab visit with all its results (MEASURE-4, `POST /api/measurements/visits`), all or
   * nothing: a test not tracked yet is made from the starter list by its name, or under Other
   * tests with the unit given. Refused for a future time, a test given twice, an archived test,
   * too many decimal places or a range upside down.
   */
  logLabVisit(req: LabVisitRequest): Promise<LabVisitResult>;
  /** Save a visit as given (`POST …/visits/{id}`): its results become these; one left out is deleted. */
  saveLabVisit(visit: string, req: LabVisitRequest): Promise<LabVisitResult>;
  /** Delete a visit and its results; the record keeps their versions (`POST …/visits/{id}/delete`). */
  deleteLabVisit(visit: string): Promise<LabVisitResult>;
  /** Add a visit's report, a PDF or a photo up to 10 MB, in place of any before it (`POST …/visits/{id}/report`). */
  attachLabReport(visit: string, report: Blob, filename?: string): Promise<LabVisitResult>;
  /** Take a visit's report off; its bytes stay in the record (`POST …/visits/{id}/report/delete`). */
  removeLabReport(visit: string): Promise<LabVisitResult>;
}

export const liveMeasurementWrites: MeasurementWrites = {
  async addMetric(req) {
    return fits<MetricRow>()(await unwrap(api().POST("/api/measurements/metrics", { body: req })));
  },

  async setMetric(metric, req) {
    return fits<MetricRow>()(await unwrap(
      api().POST("/api/measurements/metrics/{id}", { params: { path: { id: metric } }, body: req }),
    ));
  },

  async logReading(req) {
    return fits<MetricReadingResult>()(await unwrap(api().POST("/api/measurements/readings", { body: req })));
  },

  async logReadings(req) {
    return fits<MetricReadingsResult>()(await unwrap(api().POST("/api/measurements/readings/batch", { body: req })));
  },

  async editReading(reading, req) {
    return fits<MetricReadingResult>()(await unwrap(
      api().POST("/api/measurements/readings/{id}", { params: { path: { id: reading } }, body: req }),
    ));
  },

  async deleteReading(reading) {
    return fits<MetricReadingResult>()(await unwrap(
      api().POST("/api/measurements/readings/{id}/delete", { params: { path: { id: reading } } }),
    ));
  },

  async logLabVisit(req) {
    return fits<LabVisitResult>()(await unwrap(api().POST("/api/measurements/visits", { body: req })));
  },

  async saveLabVisit(visit, req) {
    return fits<LabVisitResult>()(await unwrap(
      api().POST("/api/measurements/visits/{id}", { params: { path: { id: visit } }, body: req }),
    ));
  },

  async deleteLabVisit(visit) {
    return fits<LabVisitResult>()(await unwrap(
      api().POST("/api/measurements/visits/{id}/delete", { params: { path: { id: visit } } }),
    ));
  },

  async attachLabReport(visit, report, filename) {
    const form = new FormData();
    form.append("report", report, filename ?? (report instanceof File ? report.name : "report.pdf"));
    return fits<LabVisitResult>()(await unwrap(
      api().POST("/api/measurements/visits/{id}/report", {
        params: { path: { id: visit } },
        // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
        body: form as unknown as { report?: string },
        bodySerializer: (body) => body as unknown as FormData,
      }),
    ));
  },

  async removeLabReport(visit) {
    return fits<LabVisitResult>()(await unwrap(
      api().POST("/api/measurements/visits/{id}/report/delete", { params: { path: { id: visit } } }),
    ));
  },
};

/** Readings and visits are information only (D20): a write moves no level, so only Measurements refreshes. */
const measurementMoves = () => [qk.measurements];

export const measurementEffects: { [K in keyof MeasurementWrites]: Effect<K> } = {
  addMetric: { says: (row) => `Added ${row.name}`, refreshes: measurementMoves },
  setMetric: { says: (row) => `Saved ${row.name}`, refreshes: measurementMoves },
  // A reading write says nothing here: the sheet announces it with the metric's decimals.
  logReading: { refreshes: measurementMoves },
  logReadings: { refreshes: measurementMoves },
  editReading: { refreshes: measurementMoves },
  deleteReading: { refreshes: measurementMoves },
  // A visit write makes tests and moves their ranges, so all of Measurements refreshes too. It says
  // nothing here either: the sheet announces it once its report is in.
  logLabVisit: { refreshes: measurementMoves },
  saveLabVisit: { refreshes: measurementMoves },
  deleteLabVisit: { refreshes: measurementMoves },
  attachLabReport: { refreshes: measurementMoves },
  removeLabReport: { refreshes: measurementMoves },
};
