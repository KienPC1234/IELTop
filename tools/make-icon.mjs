#!/usr/bin/env node
/**
 * IELTop logo and icon generator. One file, no dependencies, Node built-ins only.
 *
 * Draws the IELTop mark (an indigo rounded tile with an open book and a check)
 * and writes:
 *   IELTop/Assets/Images/app.ico        multi size Windows icon (PNG packed)
 *   IELTop/Assets/Images/logo-256.png   square logo for docs and the store
 *   IELTop/Assets/Images/logo-64.png    small logo for README and badges
 *
 * Run:
 *   node tools/make-icon.mjs
 *
 * The shapes are drawn at 4x and downsampled, so edges stay smooth. Colours
 * match Styles/SimpleTheme.xaml: accent #4F46E5 on white.
 */

import { deflateSync } from "node:zlib";
import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const OUT_DIR = join(ROOT, "IELTop", "Assets", "Images");

const ACCENT = [0x4f, 0x46, 0xe5];
const ACCENT_DARK = [0x37, 0x30, 0xa3];
const WHITE = [0xff, 0xff, 0xff];
const GOLD = [0xfb, 0xbf, 0x24];

const SS = 4; // supersample factor
const ICO_SIZES = [16, 24, 32, 48, 64, 128, 256];

// ---------- canvas ----------

function makeCanvas(size) {
  return { size, px: new Uint8ClampedArray(size * size * 4) };
}

function blend(canvas, x, y, rgb, alpha) {
  if (alpha <= 0) return;
  const i = (y * canvas.size + x) * 4;
  const p = canvas.px;
  const a = Math.min(1, alpha);
  p[i] = p[i] * (1 - a) + rgb[0] * a;
  p[i + 1] = p[i + 1] * (1 - a) + rgb[1] * a;
  p[i + 2] = p[i + 2] * (1 - a) + rgb[2] * a;
  p[i + 3] = Math.max(p[i + 3], Math.round(a * 255));
}

/** Rounded rectangle, optionally with a vertical gradient to the dark accent. */
function fillRoundRect(canvas, x, y, w, h, radius, rgb, rgb2) {
  const x0 = Math.floor(x), y0 = Math.floor(y);
  const x1 = Math.ceil(x + w), y1 = Math.ceil(y + h);
  for (let py = y0; py < y1; py++) {
    for (let px = x0; px < x1; px++) {
      if (px < 0 || py < 0 || px >= canvas.size || py >= canvas.size) continue;
      if (!insideRoundRect(px + 0.5, py + 0.5, x, y, w, h, radius)) continue;
      let col = rgb;
      if (rgb2) {
        const t = Math.min(1, Math.max(0, (py - y) / h));
        col = [
          Math.round(rgb[0] + (rgb2[0] - rgb[0]) * t),
          Math.round(rgb[1] + (rgb2[1] - rgb[1]) * t),
          Math.round(rgb[2] + (rgb2[2] - rgb[2]) * t),
        ];
      }
      blend(canvas, px, py, col, 1);
    }
  }
}

function insideRoundRect(px, py, x, y, w, h, r) {
  if (px < x || py < y || px > x + w || py > y + h) return false;
  if (r <= 0) return true;
  const cx = Math.min(Math.max(px, x + r), x + w - r);
  const cy = Math.min(Math.max(py, y + r), y + h - r);
  const dx = px - cx, dy = py - cy;
  return dx * dx + dy * dy <= r * r;
}

/** Fill a convex polygon given as [x,y] points, with 1px edge softening. */
function fillPolygon(canvas, points, rgb) {
  const xs = points.map((p) => p[0]);
  const ys = points.map((p) => p[1]);
  const minX = Math.floor(Math.min(...xs)) - 1;
  const maxX = Math.ceil(Math.max(...xs)) + 1;
  const minY = Math.floor(Math.min(...ys)) - 1;
  const maxY = Math.ceil(Math.max(...ys)) + 1;
  for (let py = minY; py <= maxY; py++) {
    for (let px = minX; px <= maxX; px++) {
      if (px < 0 || py < 0 || px >= canvas.size || py >= canvas.size) continue;
      const cx = px + 0.5, cy = py + 0.5;
      if (!pointInPolygon(cx, cy, points)) continue;
      // Softness: fade the last pixel toward the edge.
      let edge = Infinity;
      for (let i = 0; i < points.length; i++) {
        const [x1, y1] = points[i];
        const [x2, y2] = points[(i + 1) % points.length];
        edge = Math.min(edge, distToSegment(cx, cy, x1, y1, x2, y2));
      }
      blend(canvas, px, py, rgb, Math.min(1, Math.max(0, edge + 0.5)));
    }
  }
}

/** Ray casting, works for any winding. */
function pointInPolygon(px, py, points) {
  let inside = false;
  for (let i = 0, j = points.length - 1; i < points.length; j = i++) {
    const [xi, yi] = points[i];
    const [xj, yj] = points[j];
    if (((yi > py) !== (yj > py)) &&
        (px < ((xj - xi) * (py - yi)) / (yj - yi) + xi)) {
      inside = !inside;
    }
  }
  return inside;
}

/** Stroke a polyline with a round cap and join, given a width in pixels. */
function strokePolyline(canvas, points, width, rgb) {
  for (let i = 0; i < points.length - 1; i++) {
    strokeLine(canvas, points[i][0], points[i][1], points[i + 1][0], points[i + 1][1], width, rgb);
  }
}

function distToSegment(px, py, x0, y0, x1, y1) {
  const vx = x1 - x0, vy = y1 - y0;
  const len2 = vx * vx + vy * vy || 1;
  const t = Math.min(1, Math.max(0, ((px - x0) * vx + (py - y0) * vy) / len2));
  return Math.hypot(px - (x0 + vx * t), py - (y0 + vy * t));
}

/** Thick line segment, used for the check mark. */
function strokeLine(canvas, x0, y0, x1, y1, width, rgb) {
  const half = width / 2;
  const minX = Math.floor(Math.min(x0, x1) - half) - 1;
  const maxX = Math.ceil(Math.max(x0, x1) + half) + 1;
  const minY = Math.floor(Math.min(y0, y1) - half) - 1;
  const maxY = Math.ceil(Math.max(y0, y1) + half) + 1;
  const vx = x1 - x0, vy = y1 - y0;
  const len2 = vx * vx + vy * vy || 1;
  for (let py = minY; py <= maxY; py++) {
    for (let px = minX; px <= maxX; px++) {
      if (px < 0 || py < 0 || px >= canvas.size || py >= canvas.size) continue;
      const t = Math.min(1, Math.max(0, ((px - x0) * vx + (py - y0) * vy) / len2));
      const cx = x0 + vx * t, cy = y0 + vy * t;
      const d = Math.hypot(px - cx, py - cy);
      const a = Math.min(1, Math.max(0, half + 0.5 - d));
      blend(canvas, px, py, rgb, a);
    }
  }
}

/** Downsample with box averaging. Returns an 8 bit RGBA pixel array. */
function downsample(canvas, factor) {
  const out = canvas.size / factor;
  const dst = new Uint8ClampedArray(out * out * 4);
  for (let y = 0; y < out; y++) {
    for (let x = 0; x < out; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let dy = 0; dy < factor; dy++) {
        for (let dx = 0; dx < factor; dx++) {
          const i = ((y * factor + dy) * canvas.size + (x * factor + dx)) * 4;
          const pa = canvas.px[i + 3] / 255;
          r += canvas.px[i] * pa;
          g += canvas.px[i + 1] * pa;
          b += canvas.px[i + 2] * pa;
          a += pa;
        }
      }
      const n = factor * factor;
      const o = (y * out + x) * 4;
      if (a <= 0) {
        dst[o] = dst[o + 1] = dst[o + 2] = dst[o + 3] = 0;
      } else {
        dst[o] = Math.round(r / a);
        dst[o + 1] = Math.round(g / a);
        dst[o + 2] = Math.round(b / a);
        dst[o + 3] = Math.round((a / n) * 255);
      }
    }
  }
  return dst;
}

// ---------- the mark ----------

export function drawLogo(size) {
  const canvas = makeCanvas(size * SS);
  const s = size * SS;
  const u = s / 100; // 100 unit design grid

  // Rounded tile with a soft vertical gradient.
  fillRoundRect(canvas, 5 * u, 5 * u, 90 * u, 90 * u, 21 * u, ACCENT, ACCENT_DARK);

  // Bold open book, centered. Two white pages rise from a low center fold,
  // so the silhouette reads as a book even at 16 pixels.
  const left = 18 * u, right = 82 * u, spine = 50 * u;
  const topOuter = 26 * u, topInner = 36 * u;
  const bottom = 74 * u, foldDepth = 60 * u;
  const fold = 2 * u;

  // Left page: a quadrilateral leaning up to the left.
  fillPolygon(canvas, [
    [left, topOuter],
    [spine - fold, topInner],
    [spine - fold, foldDepth],
    [left, bottom],
  ], WHITE);
  // Right page: mirror.
  fillPolygon(canvas, [
    [right, topOuter],
    [spine + fold, topInner],
    [spine + fold, foldDepth],
    [right, bottom],
  ], WHITE);

  // A soft shadow under the book, so it sits on the tile.
  fillRoundRect(canvas, left + 2 * u, bottom - 1.5 * u, (right - left) - 4 * u, 4 * u, 2 * u, ACCENT_DARK);

  // Gold bookmark ribbon in the fold. This is the one strong accent that
  // makes the mark recognizable even at 16 pixels.
  const ribbonW = 7 * u;
  const ribbonTop = topInner - 2 * u;
  const ribbonBottom = 82 * u;
  const notch = 7 * u;
  fillPolygon(canvas, [
    [spine - ribbonW / 2, ribbonTop],
    [spine + ribbonW / 2, ribbonTop],
    [spine + ribbonW / 2, ribbonBottom],
    [spine, ribbonBottom - notch],
    [spine - ribbonW / 2, ribbonBottom],
  ], GOLD);

  return downsample(canvas, SS);
}

function drawPage(canvas, x, y, w, h, r, dir) {
  // dir -1 leans the left page, +1 the right page, for a gentle open book.
  const x0 = Math.floor(x - 6 * (w / 26) * (canvas.size / 400)) - 2;
  const y0 = Math.floor(y) - 2;
  const x1 = Math.ceil(x + w) + 2;
  const y1 = Math.ceil(y + h) + 2;
  const lean = 4 * (canvas.size / 400); // bottom shifts outward by this
  for (let py = y0; py <= y1; py++) {
    for (let px = x0; px <= x1; px++) {
      if (px < 0 || py < 0 || px >= canvas.size || py >= canvas.size) continue;
      const t = Math.min(1, Math.max(0, (py - y) / h));
      const ox = dir * lean * t;
      if (!insideRoundRect(px + 0.5 - ox, py + 0.5, x, y, w, h, r)) continue;
      blend(canvas, px, py, WHITE, 1);
    }
  }
}

// ---------- PNG encoding ----------

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const body = Buffer.concat([Buffer.from(type, "ascii"), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body), 0);
  return Buffer.concat([len, body, crc]);
}

/** Encode an RGBA pixel array as a PNG buffer. */
export function encodePng(rgba, size) {
  const raw = Buffer.alloc((size * 4 + 1) * size);
  for (let y = 0; y < size; y++) {
    raw[y * (size * 4 + 1)] = 0; // filter type none
    for (let x = 0; x < size * 4; x++) {
      raw[y * (size * 4 + 1) + 1 + x] = rgba[y * size * 4 + x];
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8;  // bit depth
  ihdr[9] = 6;  // colour type RGBA
  ihdr[10] = 0; // compression
  ihdr[11] = 0; // filter
  ihdr[12] = 0; // interlace
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

// ---------- ICO encoding ----------

/** Pack PNG images into a Windows .ico (Vista+ PNG entries). */
export function encodeIco(entries) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); // reserved
  header.writeUInt16LE(1, 2); // type icon
  header.writeUInt16LE(entries.length, 4);

  const dir = Buffer.alloc(16 * entries.length);
  let offset = 6 + dir.length;
  const blobs = [];
  entries.forEach((e, i) => {
    const b = i * 16;
    dir[b] = e.size >= 256 ? 0 : e.size;
    dir[b + 1] = e.size >= 256 ? 0 : e.size;
    dir[b + 2] = 0; // palette
    dir[b + 3] = 0; // reserved
    dir.writeUInt16LE(1, b + 4);  // colour planes
    dir.writeUInt16LE(32, b + 6); // bits per pixel
    dir.writeUInt32LE(e.png.length, b + 8);
    dir.writeUInt32LE(offset, b + 12);
    offset += e.png.length;
    blobs.push(e.png);
  });
  return Buffer.concat([header, dir, ...blobs]);
}

// ---------- run ----------

function main() {
  mkdirSync(OUT_DIR, { recursive: true });

  const entries = ICO_SIZES.map((size) => ({
    size,
    png: encodePng(drawLogo(size), size),
  }));
  writeFileSync(join(OUT_DIR, "app.ico"), encodeIco(entries));

  writeFileSync(join(OUT_DIR, "logo-256.png"), encodePng(drawLogo(256), 256));
  writeFileSync(join(OUT_DIR, "logo-64.png"), encodePng(drawLogo(64), 64));

  console.log("Wrote app.ico, logo-256.png and logo-64.png to Assets/Images");
  console.log(`Icon sizes: ${ICO_SIZES.join(", ")}`);
}

main();
