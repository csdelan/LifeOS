/**
 * The mock's copy of the kernel's starter list (`MetricCatalog` in LifeOs.Domain, measurements
 * plan §2.1, §4.8, §4.9), which it serves as `GET /api/measurements/catalog`. The live app reads
 * the kernel's own, so this copy is the mock's alone (code review CR73), and
 * `tests/rules/catalogs.json` checks it matches. Weight (M1), then the body metrics (M3): blood
 * pressure is two metrics logged together, paired by their keys, and only it comes with a normal
 * range. Then the lab tests (M4) with the common adult ranges a first visit starts from.
 */
import type { LabPanel, MetricBetter, MetricTemplate } from "@/lib/production-ui-types";

function metric(
  key: string,
  name: string,
  unit: string,
  decimals: number,
  better: MetricBetter | null,
  low: number | null = null,
  high: number | null = null,
  panel: LabPanel | null = null,
): MetricTemplate {
  return { key, name, unit, decimals, better, low, high, panel };
}

export const WEIGHT = metric("weight", "Weight", "lb", 1, "lower");
export const BODY_FAT = metric("body_fat", "Body fat", "%", 1, "lower");
export const WAIST = metric("waist", "Waist", "in", 1, "lower");
export const SYSTOLIC = metric("bp_systolic", "Systolic BP", "mmHg", 0, "lower", 90, 119);
export const DIASTOLIC = metric("bp_diastolic", "Diastolic BP", "mmHg", 0, "lower", 60, 79);
export const VO2MAX_LAB = metric("vo2max_lab", "VO2 max (lab)", "mL/kg/min", 1, "higher");

/** The comprehensive metabolic panel's 14 tests and eGFR, as `MetricCatalog.Cmp`. */
export const CMP_TESTS: readonly MetricTemplate[] = [
  metric("glucose", "Glucose", "mg/dL", 0, null, 70, 100, "cmp"),
  metric("bun", "BUN", "mg/dL", 0, null, 6, 20, "cmp"),
  metric("creatinine", "Creatinine", "mg/dL", 2, null, 0.6, 1.3, "cmp"),
  metric("egfr", "eGFR", "mL/min/1.73m2", 0, "higher", 60, null, "cmp"),
  metric("sodium", "Sodium", "mmol/L", 0, null, 135, 145, "cmp"),
  metric("potassium", "Potassium", "mmol/L", 1, null, 3.7, 5.2, "cmp"),
  metric("chloride", "Chloride", "mmol/L", 0, null, 96, 106, "cmp"),
  metric("co2", "CO2", "mmol/L", 0, null, 23, 29, "cmp"),
  metric("calcium", "Calcium", "mg/dL", 1, null, 8.5, 10.2, "cmp"),
  metric("total_protein", "Total protein", "g/dL", 1, null, 6.0, 8.3, "cmp"),
  metric("albumin", "Albumin", "g/dL", 1, null, 3.4, 5.4, "cmp"),
  metric("bilirubin", "Total bilirubin", "mg/dL", 1, null, 0.1, 1.2, "cmp"),
  metric("alp", "Alkaline phosphatase", "U/L", 0, null, 20, 130, "cmp"),
  metric("ast", "AST", "U/L", 0, null, 8, 33, "cmp"),
  metric("alt", "ALT", "U/L", 0, null, 4, 36, "cmp"),
];

/** The lipid panel, as `MetricCatalog.Lipids`. HDL's range depends on sex, so it has none. */
export const LIPID_TESTS: readonly MetricTemplate[] = [
  metric("cholesterol", "Total cholesterol", "mg/dL", 0, "lower", null, 199, "lipids"),
  metric("hdl", "HDL", "mg/dL", 0, "higher", null, null, "lipids"),
  metric("triglycerides", "Triglycerides", "mg/dL", 0, "lower", null, 149, "lipids"),
  metric("ldl", "LDL", "mg/dL", 0, "lower", null, 99, "lipids"),
  metric("non_hdl", "Non-HDL cholesterol", "mg/dL", 0, "lower", null, 129, "lipids"),
];

/** Hemoglobin A1c, as `MetricCatalog.A1c`. */
export const A1C = metric("a1c", "A1c", "%", 1, "lower", null, 5.6, "a1c");

/** The whole starter list, as `MetricCatalog.All`: the body metrics, then the lab tests as reports print them. */
export const METRIC_CATALOG: readonly MetricTemplate[] = [
  WEIGHT,
  BODY_FAT,
  WAIST,
  SYSTOLIC,
  DIASTOLIC,
  VO2MAX_LAB,
  ...CMP_TESTS,
  ...LIPID_TESTS,
  A1C,
];

/** The template a metric named `name` is made from, in any case, as `MetricCatalog.Find`. */
export function findTemplate(name: string): MetricTemplate | undefined {
  const wanted = name.trim().toLowerCase();
  return METRIC_CATALOG.find((t) => t.name.toLowerCase() === wanted);
}
