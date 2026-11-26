import { afterEach, describe, expect, it, vi } from 'vitest';
import { KEEP_ORIGINAL_BELOW_BYTES, MAX_UPLOAD_EDGE, prepareImage, targetSize } from './image-prepare';

describe('targetSize', () => {
  it('keeps small pictures as they are', () => {
    expect(targetSize(800, 600)).toEqual({ width: 800, height: 600 });
  });

  it('never enlarges a picture', () => {
    expect(targetSize(MAX_UPLOAD_EDGE, 10)).toEqual({ width: MAX_UPLOAD_EDGE, height: 10 });
  });

  it('scales a landscape picture down to the long edge', () => {
    expect(targetSize(4000, 3000)).toEqual({ width: 2000, height: 1500 });
  });

  it('scales a portrait picture down to the long edge', () => {
    expect(targetSize(3000, 4000)).toEqual({ width: 1500, height: 2000 });
  });

  it('never produces a zero-sized picture', () => {
    expect(targetSize(100000, 1, 100)).toEqual({ width: 100, height: 1 });
  });
});

describe('prepareImage', () => {
  const original = new File([new Uint8Array(10)], 'card.png', { type: 'image/png' });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('returns the original when the browser cannot decode images', async () => {
    vi.stubGlobal('createImageBitmap', undefined);

    expect(await prepareImage(original)).toBe(original);
  });

  it('returns the original when decoding fails', async () => {
    vi.stubGlobal('createImageBitmap', vi.fn().mockRejectedValue(new Error('bad image')));

    expect(await prepareImage(original)).toBe(original);
  });

  it('keeps a small, light picture untouched', async () => {
    const close = vi.fn();
    vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue({ width: 1200, height: 800, close }));

    expect(await prepareImage(original)).toBe(original);
    expect(close).toHaveBeenCalled();
  });

  it('falls back to the original when no 2D canvas context is available', async () => {
    const close = vi.fn();
    vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue({ width: 4000, height: 3000, close }));
    vi.spyOn(document, 'createElement').mockReturnValue({
      width: 0,
      height: 0,
      getContext: () => null,
    } as unknown as HTMLCanvasElement);

    expect(await prepareImage(original)).toBe(original);
    expect(close).toHaveBeenCalled();
  });

  it('shrinks a large picture into a JPEG', async () => {
    const close = vi.fn();
    const drawImage = vi.fn();
    const canvas = {
      width: 0,
      height: 0,
      getContext: () => ({ fillStyle: '', fillRect: vi.fn(), drawImage }),
      toBlob: (callback: (blob: Blob | null) => void, type: string) =>
        callback(new Blob([new Uint8Array(5)], { type })),
    };
    vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue({ width: 4000, height: 3000, close }));
    vi.spyOn(document, 'createElement').mockReturnValue(canvas as unknown as HTMLCanvasElement);

    const result = await prepareImage(original);

    expect(result).not.toBe(original);
    expect(result.type).toBe('image/jpeg');
    expect(result.name).toBe('card.jpg');
    expect(canvas.width).toBe(2000);
    expect(canvas.height).toBe(1500);
    expect(drawImage).toHaveBeenCalledWith(expect.anything(), 0, 0, 2000, 1500);
    expect(close).toHaveBeenCalled();
  });

  it('re-encodes a heavy but small-pixel picture only when that makes it smaller', async () => {
    const heavy = new File([new Uint8Array(KEEP_ORIGINAL_BELOW_BYTES + 1)], 'scan.png', { type: 'image/png' });
    const canvas = {
      width: 0,
      height: 0,
      getContext: () => ({ fillStyle: '', fillRect: vi.fn(), drawImage: vi.fn() }),
      toBlob: (callback: (blob: Blob | null) => void, type: string) =>
        callback(new Blob([new Uint8Array(KEEP_ORIGINAL_BELOW_BYTES + 2)], { type })),
    };
    vi.stubGlobal('createImageBitmap', vi.fn().mockResolvedValue({ width: 1500, height: 1000, close: vi.fn() }));
    vi.spyOn(document, 'createElement').mockReturnValue(canvas as unknown as HTMLCanvasElement);

    expect(await prepareImage(heavy)).toBe(heavy);
  });
});
