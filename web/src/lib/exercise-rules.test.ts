import { describe, expect, it } from "vitest";
import {
  DETECTED_REASON,
  UNKNOWN_SOURCE_REASON,
  WALK_REASON,
  exerciseVerdict,
} from "@/lib/exercise-rules";

// The kernel's table (ActivityRulesTests.Only_workouts_Chris_records_count_and_never_a_walk),
// row for row, so the mock and the API agree.
const TABLE: [source: string | null, activity: string | null, counted: boolean, reason: string | null][] = [
  ["manual", "running", true, null],
  ["workout_heart_rate", "cycling", true, null],
  ["live_oura_heart_rate", "strength_training", true, null],
  ["live_third_party_heart_rate", "rowing", true, null],
  [" MANUAL ", "Running", true, null],
  ["manual", "walking", false, WALK_REASON],
  ["workout_heart_rate", "Walking", false, WALK_REASON],
  ["autodetected", "walking", false, WALK_REASON],
  ["autodetected", "running", false, DETECTED_REASON],
  ["confirmed", "cycling", false, DETECTED_REASON],
  ["garmin_import", "running", false, UNKNOWN_SOURCE_REASON],
  ["unknown", "padel_v2", false, UNKNOWN_SOURCE_REASON],
  [null, "yoga", false, UNKNOWN_SOURCE_REASON],
  ["manual", null, true, null],
];

describe("exerciseVerdict", () => {
  it.each(TABLE)("%s %s → counted %s (%s)", (source, activity, counted, reason) => {
    expect(exerciseVerdict(source, activity)).toEqual({ counted, reason });
  });

  it("says the reasons in the kernel's words", () => {
    expect([WALK_REASON, DETECTED_REASON, UNKNOWN_SOURCE_REASON]).toEqual([
      "A walk: it's in your steps",
      "Detected by Oura",
      "Unknown source",
    ]);
  });
});
