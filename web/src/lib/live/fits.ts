import type { Unchecked } from "@/lib/live/http";

/**
 * Checks the API's responses against the app's types at build time (code review CR38).
 *
 * The live seams read the API's data as the hand-written types in `production-ui-types.ts`. An
 * `as` cast let a field the API dropped, renamed or retyped read as undefined at run time, and
 * the build pass (CR5a: the occurrences read never returned `note`). `fits` instead has tsc check
 * the response's generated type (`src/api/schema.d.ts`) against the app's.
 */

/**
 * T as the generated types can say it. Each string union is widened to string, since they carry an
 * enum's values as plain strings. Each field is optional, since the generator marks a class's
 * properties optional whatever they are in C# (the API writes every field, nulls included). And
 * a field the app lets be undefined may be null, as the API says a C# null.
 */
type Widened<T> = T extends string
  ? string
  : T extends readonly (infer U)[]
    ? Widened<U>[]
    : T extends object
      ? { [K in keyof T]?: undefined extends T[K] ? Widened<T[K]> | null : Widened<T[K]> }
      : T;

/**
 * The fields the app's type T reads that the API's generated type G doesn't have at all, through
 * arrays and nested objects, as dotted paths. An optional field the API stopped sending passes
 * the assignability check, and reads as undefined (code review CR5a), so it is looked for here.
 */
type Missing<G, T, AppSide extends string, P extends string = ""> = T extends readonly (infer TU)[]
  ? G extends readonly (infer GU)[]
    ? Missing<GU, TU, AppSide, P>
    : never
  : T extends object
    ? G extends object
      ? {
          [K in Exclude<keyof T & string, AppSide>]-?: K extends keyof G
            ? Missing<NonNullable<G[K]>, NonNullable<T[K]>, AppSide, `${P}${K}.`>
            : `${P}${K}`;
        }[Exclude<keyof T & string, AppSide>]
      : never
    : never;

/**
 * The API's response as the type the app reads, checked by tsc rather than cast (code review
 * CR38): the response's generated type must fit the app's as `Widened` says it, and carry every
 * field the app's type names except the app-side ones listed, which the app fills in itself.
 * `fits<InboxItem[], "bytesUrl">()(await unwrap(...))`. A field the API drops, renames or retypes then fails
 * the build here, where an `as` cast let it read as undefined.
 */
export function fits<T, AppSide extends string = never>() {
  return <G extends Widened<T>>(
    value: Unchecked<G>,
    ..._missing: [G] extends [never]
      ? ["The API's type for this response is never, so there is nothing to check"]
      : unknown extends G
        ? ["The API's type for this response is unknown, so there is nothing to check"]
        : [Missing<G, T, AppSide>] extends [never]
          ? []
          : [`The API's type has no field ${Missing<G, T, AppSide>}`]
  ): T => value as unknown as T;
}

/** The fields of a stored file's record that the app fills in itself (CAP-2 / CAP-4). */
export type ArtifactAppSide = "bytesUrl" | "durationSeconds";
