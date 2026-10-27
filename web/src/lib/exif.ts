/**
 * When a photo was taken, read from its EXIF (HOME-4, environment plan §8.2): a space photo is
 * dated from it, so photos taken on the phone at a walk-through and added later land on that
 * day. Read before `resizePhoto` re-encodes the photo, which drops the EXIF (the location too).
 *
 * Only a JPEG's `DateTimeOriginal` counts, or its `DateTimeDigitized` when that's missing:
 * `DateTime` changes when a photo is edited. The camera writes its own local time, which is the
 * day it was taken where it was taken. Anything unreadable answers null, never an error, so a
 * photo without a date simply takes the day picked.
 */

/** EXIF sits at the front of a JPEG; this is far more than any camera writes before it. */
const HEAD_BYTES = 256 * 1024;

const SOI = 0xffd8;
const APP1 = 0xffe1;
const SOS = 0xffda;
const EOI = 0xffd9;
const EXIF_IFD_POINTER = 0x8769;
const DATE_TIME_ORIGINAL = 0x9003;
const DATE_TIME_DIGITIZED = 0x9004;
const ASCII = 2;

/** `yyyy-MM-dd` from EXIF's `YYYY:MM:DD HH:MM:SS`, or null when it isn't a real date. */
export function exifDay(text: string): string | null {
  const match = /^(\d{4}):(\d{2}):(\d{2})/.exec(text.trim());
  if (!match) return null;
  const [, y, m, d] = match;
  const year = Number(y);
  const month = Number(m);
  const day = Number(d);
  if (year < 1900 || month < 1 || month > 12 || day < 1) return null;
  if (day > new Date(Date.UTC(year, month, 0)).getUTCDate()) return null;
  return `${y}-${m}-${d}`;
}

/** The day a JPEG's EXIF says it was taken, from its bytes; null without one. */
export function dateTakenFromBytes(bytes: ArrayBuffer): string | null {
  try {
    const view = new DataView(bytes);
    if (view.byteLength < 4 || view.getUint16(0) !== SOI) return null;
    let offset = 2;
    while (offset + 4 <= view.byteLength) {
      const marker = view.getUint16(offset);
      if ((marker & 0xff00) !== 0xff00 || marker === SOS || marker === EOI) return null;
      const size = view.getUint16(offset + 2);
      if (size < 2) return null;
      if (marker === APP1 && isExifHeader(view, offset + 4)) {
        return readTiff(view, offset + 10, Math.min(view.byteLength, offset + 2 + size));
      }
      offset += 2 + size;
    }
    return null;
  } catch {
    return null;
  }
}

/** The day a photo was taken, from its EXIF; null for a PNG, WebP or a JPEG without one. */
export async function readDateTaken(file: Blob): Promise<string | null> {
  try {
    return dateTakenFromBytes(await file.slice(0, HEAD_BYTES).arrayBuffer());
  } catch {
    return null;
  }
}

/** "Exif\0\0" at `offset`. */
function isExifHeader(view: DataView, offset: number): boolean {
  if (offset + 6 > view.byteLength) return false;
  return (
    view.getUint32(offset) === 0x45786966 && // "Exif"
    view.getUint16(offset + 4) === 0
  );
}

function readTiff(view: DataView, tiff: number, end: number): string | null {
  if (tiff + 8 > end) return null;
  const order = view.getUint16(tiff);
  if (order !== 0x4949 && order !== 0x4d4d) return null;
  const little = order === 0x4949;
  const u16 = (at: number) => view.getUint16(at, little);
  const u32 = (at: number) => view.getUint32(at, little);
  if (u16(tiff + 2) !== 42) return null;

  /** The entries of the IFD at `ifd` (from the TIFF header), as tag → its 12-byte entry's offset. */
  const entries = (ifd: number): Map<number, number> => {
    const found = new Map<number, number>();
    const start = tiff + ifd;
    if (ifd < 8 || start + 2 > end) return found;
    const count = u16(start);
    for (let i = 0; i < count; i++) {
      const entry = start + 2 + i * 12;
      if (entry + 12 > end) break;
      found.set(u16(entry), entry);
    }
    return found;
  };

  /** An ASCII entry's text, from its value's offset (it's longer than 4 bytes). */
  const ascii = (entry: number | undefined): string | null => {
    if (entry === undefined || u16(entry + 2) !== ASCII) return null;
    const count = u32(entry + 4);
    if (count < 10 || count > 64) return null;
    const at = tiff + u32(entry + 8);
    if (at + count > end) return null;
    let text = "";
    for (let i = 0; i < count; i++) {
      const c = view.getUint8(at + i);
      if (c === 0) break;
      text += String.fromCharCode(c);
    }
    return text;
  };

  const ifd0 = entries(u32(tiff + 4));
  const pointer = ifd0.get(EXIF_IFD_POINTER);
  if (pointer === undefined) return null;
  const exif = entries(u32(pointer + 8));
  for (const tag of [DATE_TIME_ORIGINAL, DATE_TIME_DIGITIZED]) {
    const text = ascii(exif.get(tag));
    const day = text ? exifDay(text) : null;
    if (day) return day;
  }
  return null;
}
