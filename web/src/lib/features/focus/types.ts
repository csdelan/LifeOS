/**
 * The Primary focus (TODAY-1, D6, focus plan G1): the Active Goal Chris chooses in a daily or
 * weekly review, kept until he changes it. Until he chooses one, or once the one he chose is no
 * longer Active, Focus suggests the Active Goal with the nearest target date and says it's a
 * suggestion. Focus reads it as `primaryFocus` (`GET /api/focus`); choosing it is
 * `POST /api/focus/primary`.
 *
 * A type that needs no narrowing is the generated one (`ApiSchema`).
 */
import type { ApiSchema } from "@/lib/production-ui-types";

/** The focus as last chosen, and whether choosing wrote anything (choosing the focus held doesn't). */
export type FocusChoiceRead = ApiSchema<"FocusChoiceRow">;
