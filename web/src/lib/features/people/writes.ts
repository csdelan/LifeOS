/**
 * The People feature's writes: what the write seam offers, how the live app sends each to the
 * API, and what each does besides itself (`lib/write-effects.ts`).
 */
import { fits } from "@/lib/live/fits";
import { api, unwrap } from "@/lib/live/http";
import { qk } from "@/lib/query-keys";
import type { Effect } from "@/lib/write-effects";
import type { PersonPhotoRead } from "@/lib/features/people/types";

export interface PeopleWrites {
  /**
   * Make a photo a Person's (GEN-15, `POST /api/people/{id}/photo`), in place of any it had: a
   * JPEG, PNG or WebP of at most 5 MB. The page sends it shrunk to 1,200 px (`resizePhoto`).
   */
  setPersonPhoto(person: string, photo: Blob, filename?: string): Promise<PersonPhotoRead>;
  /** Take a Person's photo off; its bytes stay in the record (`POST /api/people/{id}/photo/delete`). */
  removePersonPhoto(person: string): Promise<PersonPhotoRead>;
}

export const livePeopleWrites: PeopleWrites = {
  async setPersonPhoto(person, photo, filename) {
    const form = new FormData();
    form.append("photo", photo, filename ?? "photo.jpg");
    return fits<PersonPhotoRead>()(await unwrap(
      api().POST("/api/people/{id}/photo", {
        params: { path: { id: person } },
        // The browser sets the multipart boundary; openapi-fetch leaves FormData as it is.
        body: form as unknown as { photo?: string },
        bodySerializer: (body) => body as unknown as FormData,
      }),
    ));
  },

  async removePersonPhoto(person) {
    return fits<PersonPhotoRead>()(await unwrap(
      api().POST("/api/people/{id}/photo/delete", { params: { path: { id: person } } }),
    ));
  },
};

/**
 * A photo shows on the People page (and the agents acting as the Person, which read it there) and
 * on the Person's own page, whose history keeps the attribute change. It earns nothing.
 */
const photoMoves = (person: string) => [qk.people, qk.subject(person), qk.listItem(person), qk.history(person)];

export const peopleEffects: { [K in keyof PeopleWrites]: Effect<K> } = {
  setPersonPhoto: { says: () => "Photo saved", refreshes: ([person]) => photoMoves(person) },
  removePersonPhoto: { says: () => "Photo removed", refreshes: ([person]) => photoMoves(person) },
};
