/**
 * Prepares a photo for upload: bakes in the EXIF orientation and shrinks very large pictures.
 *
 * Phone cameras produce 4-12 MB JPEGs, which the API rejects above 5 MB and which slow down a
 * CPU-only server for no gain, because the OCR pipeline resizes the image itself anyway.
 * The long edge is capped at {@link MAX_UPLOAD_EDGE}, which stays above the server's own
 * working sizes so no OCR detail is lost. Pictures that are already small are sent untouched.
 *
 * Every failure falls back to the original file, so this step can never block an upload.
 */

/** Longest edge, in pixels, of the image that is uploaded. */
export const MAX_UPLOAD_EDGE = 2000;

/** Files at or below this size, and within the pixel cap, are uploaded as they are. */
export const KEEP_ORIGINAL_BELOW_BYTES = 1.5 * 1024 * 1024;

const JPEG_QUALITY = 0.9;

export interface PrepareOptions {
  maxEdge?: number;
  quality?: number;
}

/** Scales (width, height) so the long edge is at most `maxEdge`. Never enlarges. */
export function targetSize(
  width: number,
  height: number,
  maxEdge: number = MAX_UPLOAD_EDGE,
): { width: number; height: number } {
  const longEdge = Math.max(width, height);
  const scale = longEdge > maxEdge ? maxEdge / longEdge : 1;

  return {
    width: Math.max(1, Math.round(width * scale)),
    height: Math.max(1, Math.round(height * scale)),
  };
}

/** Returns a JPEG no larger than the cap, or the original file when nothing needs to change. */
export async function prepareImage(file: File, options: PrepareOptions = {}): Promise<File> {
  const maxEdge = options.maxEdge ?? MAX_UPLOAD_EDGE;
  const quality = options.quality ?? JPEG_QUALITY;

  if (typeof createImageBitmap !== 'function' || typeof document === 'undefined') {
    return file;
  }

  let bitmap: ImageBitmap;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    return file;
  }

  try {
    const longEdge = Math.max(bitmap.width, bitmap.height);
    if (longEdge <= maxEdge && file.size <= KEEP_ORIGINAL_BELOW_BYTES) {
      return file;
    }

    const size = targetSize(bitmap.width, bitmap.height, maxEdge);
    const canvas = document.createElement('canvas');
    canvas.width = size.width;
    canvas.height = size.height;

    const context = canvas.getContext('2d');
    if (!context) {
      return file;
    }

    // JPEG has no alpha channel: paint white first so transparent PNGs do not turn black.
    context.fillStyle = '#ffffff';
    context.fillRect(0, 0, size.width, size.height);
    context.drawImage(bitmap, 0, 0, size.width, size.height);

    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', quality));
    if (!blob) {
      return file;
    }

    const shrunk = size.width < bitmap.width || size.height < bitmap.height;
    if (!shrunk && blob.size >= file.size) {
      // Re-encoding a same-size picture made it bigger: keep the original.
      return file;
    }

    return new File([blob], `${file.name.replace(/\.[^.]+$/, '')}.jpg`, { type: 'image/jpeg' });
  } catch {
    return file;
  } finally {
    bitmap.close();
  }
}
