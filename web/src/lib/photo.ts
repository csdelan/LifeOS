import { readDateTaken } from "@/lib/exif";

/** The long side a photo is sent at (food-log plan §3.8; space photos too, environment plan §8.2). */
export const PHOTO_MAX_EDGE = 1200;

/** The JPEG quality a food photo is sent at: about 150 KB at 1,200 px. */
export const PHOTO_QUALITY = 0.8;

/** The most the API takes (`FoodPhotoService.MaxBytes`). */
export const PHOTO_MAX_BYTES = 5 * 1024 * 1024;

/** `width` × `height` scaled to fit `max` on the long side, in whole pixels; never enlarged. */
export function fitWithin(width: number, height: number, max = PHOTO_MAX_EDGE): { width: number; height: number } {
  const scale = Math.min(1, max / Math.max(width, height, 1));
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

/**
 * A photo as the Food Log sends it: decoded upright (its EXIF orientation applied), scaled to
 * 1,200 px on the long side, and re-encoded as a JPEG at 0.8 on white. Re-encoding also drops
 * the camera's metadata, the location included. Refused in words when the browser can't read
 * the file as an image.
 */
export async function resizePhoto(file: Blob): Promise<Blob> {
  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: "from-image" });
  } catch {
    throw new Error("That file isn't a photo this browser can read. Try a JPEG or PNG.");
  }
  const { width, height } = fitWithin(bitmap.width, bitmap.height);
  const canvas = document.createElement("canvas");
  canvas.width = width;
  canvas.height = height;
  const context = canvas.getContext("2d");
  if (!context) {
    bitmap.close();
    throw new Error("This browser can't prepare photos.");
  }
  context.fillStyle = "#fff";
  context.fillRect(0, 0, width, height);
  context.drawImage(bitmap, 0, 0, width, height);
  bitmap.close();
  const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", PHOTO_QUALITY));
  if (!blob) throw new Error("Couldn't prepare that photo.");
  if (blob.size > PHOTO_MAX_BYTES) throw new Error("That photo is still over 5 MB after shrinking it.");
  return blob;
}

/** The name a photo is sent under: the picked file's, as a JPEG once resized. */
export function jpegName(filename: string): string {
  return `${filename.replace(/\.[^.]*$/, "") || "photo"}.jpg`;
}

/** A picked photo made ready to send: resized, its name, and the day its EXIF says it was taken. */
export interface PreparedPhoto {
  blob: Blob;
  name: string;
  /** `yyyy-MM-dd`, or null when the file doesn't say. */
  takenOn: string | null;
}

/**
 * A space photo as it's sent (HOME-4): its date taken read first, since resizing drops the
 * EXIF, then shrunk and re-encoded as `resizePhoto` does.
 */
export async function preparePhoto(file: File): Promise<PreparedPhoto> {
  const takenOn = await readDateTaken(file);
  return { blob: await resizePhoto(file), name: jpegName(file.name), takenOn };
}
