/**
 * The mock's half of People (GEN-15): a Person's photo, as `PersonPhotoService` keeps it. The
 * bytes are an artifact in the main store, and `attributes.photo` holds its id; reads add
 * `photoArtifactId`. Refusals are the kernel's in words, but the .NET tests hold the rules.
 */
import type { PersonPhotoRead } from "@/lib/features/people/types";
import { PHOTO_MAX_BYTES } from "@/lib/photo";
import type { PersonRow, SubjectDetail, SubjectListItem } from "@/lib/production-ui-types";
import { byRef, emit, getArtifact, newId, patchStoredAttrs, putArtifact, storedAttrs } from "@/lib/mock/store";

const PHOTO_TYPES = ["image/jpeg", "image/png", "image/webp"];

function personOf(ref: string): SubjectListItem {
  const item = byRef(ref);
  if (!item) throw new Error(`No subject matches '${ref}'.`);
  if (item.type !== "Person") throw new Error(`'${item.title}' is a ${item.type}; only People and AI agents have a photo.`);
  return item;
}

function read(item: SubjectListItem, photoArtifactId: string | null): PersonPhotoRead {
  return { person: { id: item.id, urn: item.urn, type: item.type, title: item.title }, photoArtifactId };
}

/** The Person's photo: the artifact `attributes.photo` names, when the store has its bytes. */
export function personPhotoOf(id: string): string | null {
  const stored = storedAttrs(id).photo;
  return typeof stored === "string" && getArtifact(stored)?.hasBytes ? stored : null;
}

/** A People row as `/api/people` answers it, with its photo. */
export function withPersonPhoto(row: PersonRow): PersonRow {
  return { ...row, photoArtifactId: personPhotoOf(row.id) };
}

/** A subject's detail with its photo: a Person's, and null for anything else. */
export function detailWithPhoto(detail: SubjectDetail): SubjectDetail {
  return { ...detail, photoArtifactId: detail.type === "Person" ? personPhotoOf(detail.id) : null };
}

/** `POST /api/people/{id}/photo`: the photo in place of any the Person had. */
export function mockSetPersonPhoto(ref: string, photo: Blob, filename = "photo.jpg"): PersonPhotoRead {
  const item = personOf(ref);
  if (photo.size === 0) throw new Error("That photo is empty.");
  if (photo.size > PHOTO_MAX_BYTES) {
    throw new Error(`That photo is ${(photo.size / (1024 * 1024)).toFixed(1)} MB; a photo can be at most 5 MB.`);
  }
  if (!PHOTO_TYPES.includes(photo.type)) throw new Error("That isn't a photo: send a JPEG, PNG or WebP image.");
  const artifact = putArtifact({ eventId: newId(), eventKind: "observation", blob: photo, filename, contentType: photo.type });
  patchStoredAttrs(item.id, { photo: artifact.id });
  emit();
  return read(item, artifact.id);
}

/** `POST /api/people/{id}/photo/delete`: the Person stops showing it; the bytes stay. */
export function mockRemovePersonPhoto(ref: string): PersonPhotoRead {
  const item = personOf(ref);
  patchStoredAttrs(item.id, { photo: undefined });
  emit();
  return read(item, null);
}
