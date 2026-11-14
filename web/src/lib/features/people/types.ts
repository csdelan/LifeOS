/**
 * People (GEN-15): humans and AI agents in one directory. A Person can have a photo, stored as
 * an artifact; `photoArtifactId` on `PersonRow` and `SubjectDetail` names it, and its bytes come
 * from `GET /api/artifacts/{id}/content` (`useArtifactBytesUrl`).
 *
 * A type that needs no narrowing is the generated one (`ApiSchema`).
 */
import type { ApiSchema } from "@/lib/production-ui-types";

/** A Person's photo set or removed: the Person, and the photo's artifact (null once removed). */
export type PersonPhotoRead = ApiSchema<"PersonPhotoRead">;
