import { describe, expect, it } from "vitest";
import { dateTakenFromBytes, exifDay, readDateTaken } from "@/lib/exif";

/** ASCII text, NUL-terminated, as EXIF stores it. */
function asciiz(text: string): number[] {
  return [...text].map((c) => c.charCodeAt(0)).concat(0);
}

/**
 * A JPEG's head as a camera writes it: SOI, a JFIF APP0 (unless left out), an APP1 with EXIF
 * whose IFD0 points to an Exif IFD holding the dates given, then SOS. Byte order as asked.
 */
function jpeg(
  opts: { little?: boolean; original?: string; digitized?: string; app0?: boolean; exif?: boolean } = {},
): Uint8Array<ArrayBuffer> {
  const little = opts.little ?? true;
  const dates = [
    ...(opts.original !== undefined ? [[0x9003, opts.original] as const] : []),
    ...(opts.digitized !== undefined ? [[0x9004, opts.digitized] as const] : []),
  ];
  // TIFF: header (8), IFD0 with the Exif pointer (18), the Exif IFD, then the strings.
  const exifIfd = 8 + 18;
  const exifIfdSize = 2 + 12 * dates.length + 4;
  const strings = dates.map(([, text]) => asciiz(text));
  const size = exifIfd + exifIfdSize + strings.reduce((n, s) => n + s.length, 0);
  const tiff = new DataView(new ArrayBuffer(size));
  tiff.setUint16(0, little ? 0x4949 : 0x4d4d);
  tiff.setUint16(2, 42, little);
  tiff.setUint32(4, 8, little);
  tiff.setUint16(8, 1, little);
  tiff.setUint16(10, 0x8769, little);
  tiff.setUint16(12, 4, little);
  tiff.setUint32(14, 1, little);
  tiff.setUint32(18, exifIfd, little);
  tiff.setUint32(22, 0, little);
  tiff.setUint16(exifIfd, dates.length, little);
  let at = exifIfd + exifIfdSize;
  dates.forEach(([tag], i) => {
    const entry = exifIfd + 2 + i * 12;
    tiff.setUint16(entry, tag, little);
    tiff.setUint16(entry + 2, 2, little);
    tiff.setUint32(entry + 4, strings[i].length, little);
    tiff.setUint32(entry + 8, at, little);
    strings[i].forEach((b, j) => tiff.setUint8(at + j, b));
    at += strings[i].length;
  });

  const bytes: number[] = [0xff, 0xd8];
  if (opts.app0 ?? true) {
    bytes.push(0xff, 0xe0, 0x00, 0x10, ...asciiz("JFIF"), 1, 1, 0, 0, 1, 0, 1, 0, 0);
  }
  if (opts.exif ?? true) {
    const app1 = 2 + 6 + size;
    bytes.push(0xff, 0xe1, app1 >> 8, app1 & 0xff, ...asciiz("Exif"), 0, ...new Uint8Array(tiff.buffer));
  }
  bytes.push(0xff, 0xda, 0x00, 0x08, 1, 1, 0, 0, 0x3f, 0, 0x12, 0x34, 0xff, 0xd9);
  return new Uint8Array(bytes);
}

const buffer = (bytes: Uint8Array<ArrayBuffer>) => bytes.slice().buffer;

describe("the date a photo was taken (HOME-4)", () => {
  it("reads DateTimeOriginal in either byte order, past a JFIF header", () => {
    expect(dateTakenFromBytes(buffer(jpeg({ original: "2026:10:27 16:05:09" })))).toBe("2026-10-27");
    expect(dateTakenFromBytes(buffer(jpeg({ little: false, original: "2026:09:28 08:30:00" })))).toBe("2026-09-28");
    expect(dateTakenFromBytes(buffer(jpeg({ app0: false, original: "2025:12:31 23:59:59" })))).toBe("2025-12-31");
  });

  it("falls back to DateTimeDigitized, and skips a date that isn't one", () => {
    expect(dateTakenFromBytes(buffer(jpeg({ digitized: "2026:10:01 12:00:00" })))).toBe("2026-10-01");
    expect(
      dateTakenFromBytes(buffer(jpeg({ original: "0000:00:00 00:00:00", digitized: "2026:10:02 09:00:00" }))),
    ).toBe("2026-10-02");
    expect(dateTakenFromBytes(buffer(jpeg({ original: "    :  :     :  :  " })))).toBeNull();
  });

  it("answers null for a photo without a date, never an error", () => {
    expect(dateTakenFromBytes(buffer(jpeg({ exif: false })))).toBeNull();
    expect(dateTakenFromBytes(buffer(jpeg({})))).toBeNull();
    const png = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13]);
    expect(dateTakenFromBytes(buffer(png))).toBeNull();
    expect(dateTakenFromBytes(new ArrayBuffer(0))).toBeNull();
    // Cut short anywhere, it still answers.
    const whole = jpeg({ original: "2026:10:27 16:05:09" });
    for (let n = 0; n < whole.length; n += 7) {
      const day = dateTakenFromBytes(buffer(whole.slice(0, n)));
      expect(day === null || day === "2026-10-27").toBe(true);
    }
  });

  it("reads a picked file's head", async () => {
    const file = new File([jpeg({ original: "2026:10:26 18:00:00" })], "IMG_0042.jpg", { type: "image/jpeg" });
    expect(await readDateTaken(file)).toBe("2026-10-26");
    expect(await readDateTaken(new Blob(["not a photo"]))).toBeNull();
  });

  it("knows a real date", () => {
    expect(exifDay("2024:02:29 10:00:00")).toBe("2024-02-29");
    expect(exifDay("2025:02:29 10:00:00")).toBeNull();
    expect(exifDay("2026:13:01 10:00:00")).toBeNull();
    expect(exifDay("1899:12:31 10:00:00")).toBeNull();
  });
});
