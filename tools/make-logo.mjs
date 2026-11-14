// Renders docs/img/logo.png from the same geometry as docs/img/logo.svg.
// No dependencies: a tiny software rasteriser with 3x supersampling.
import fs from 'node:fs';
import zlib from 'node:zlib';

const SIZE = 512;
const SS = 3;                 // supersampling factor
const W = SIZE * SS;

const hex = h => [parseInt(h.slice(1, 3), 16), parseInt(h.slice(3, 5), 16), parseInt(h.slice(5, 7), 16)];
const mix = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

const C_HI = hex('#7aa6d4');
const C_MID = hex('#4a76a8');
const C_LO = hex('#25466a');
const DOT_HI = hex('#5b8fc4');
const DOT_LO = hex('#26456a');

// coverage buffers
const r = new Float32Array(W * W);
const g = new Float32Array(W * W);
const b = new Float32Array(W * W);
const a = new Float32Array(W * W);

const S = W / 256;            // svg units -> pixels

function put(x, y, col, alpha) {
  if (x < 0 || y < 0 || x >= W || y >= W || alpha <= 0) return;
  const i = y * W + x;
  const inv = 1 - alpha;
  r[i] = r[i] * inv + col[0] * alpha;
  g[i] = g[i] * inv + col[1] * alpha;
  b[i] = b[i] * inv + col[2] * alpha;
  a[i] = a[i] * inv + alpha * 255;
}

// rounded rectangle coverage test in svg units
function inRoundRect(x, y, x0, y0, x1, y1, rad) {
  if (x < x0 || x > x1 || y < y0 || y > y1) return false;
  const cx = Math.min(Math.max(x, x0 + rad), x1 - rad);
  const cy = Math.min(Math.max(y, y0 + rad), y1 - rad);
  const dx = x - cx, dy = y - cy;
  return dx * dx + dy * dy <= rad * rad + 1e-9;
}

function inTriangle(x, y, p1, p2, p3) {
  const d = (p2[1] - p3[1]) * (p1[0] - p3[0]) + (p3[0] - p2[0]) * (p1[1] - p3[1]);
  const u = ((p2[1] - p3[1]) * (x - p3[0]) + (p3[0] - p2[0]) * (y - p3[1])) / d;
  const v = ((p3[1] - p1[1]) * (x - p3[0]) + (p1[0] - p3[0]) * (y - p3[1])) / d;
  return u >= 0 && v >= 0 && u + v <= 1;
}

const tileGrad = t => (t < 0.45 ? mix(C_HI, C_MID, t / 0.45) : mix(C_MID, C_LO, (t - 0.45) / 0.55));

for (let py = 0; py < W; py++) {
  for (let px = 0; px < W; px++) {
    const x = (px + 0.5) / S, y = (py + 0.5) / S;
    if (!inRoundRect(x, y, 0, 0, 256, 256, 56)) continue;

    // diagonal gradient
    const t = Math.min(1, Math.max(0, (x / 256 + y / 256) / 2));
    put(px, py, tileGrad(t), 1);

    // sheen: white .30 at the top fading out by y=136
    if (y < 136) {
      const sa = 0.30 * (1 - y / 136);
      if (sa > 0) put(px, py, [255, 255, 255], sa);
    }

    // inner hairline
    const onEdge = !inRoundRect(x, y, 1.5, 1.5, 254.5, 254.5, 54.5);
    if (onEdge) put(px, py, [255, 255, 255], 0.24);

    // bubble body + tail. The tail is a plain triangle in the SVG too, so the
    // two renderers cannot drift: (128,172) -> (71,209) -> (71,172).
    const body = inRoundRect(x, y, 32, 40, 224, 172, 34);
    const tail = inTriangle(x, y, [128, 172], [71, 209], [71, 172]);
    if (body || tail) put(px, py, [255, 255, 255], 1);

    // three dots; the gap matters more than the size, because at 16 px anything
    // closer than about 1.5 px bridges into one bar
    const dots = [[82, 106], [128, 106], [174, 106]];
    for (const [cx, cy] of dots) {
      const dx = x - cx, dy = y - cy;
      if (dx * dx + dy * dy <= 11.5 * 11.5) {
        const dt = Math.min(1, Math.max(0, (y - (cy - 11.5)) / 23));
        put(px, py, mix(DOT_HI, DOT_LO, dt), 1);
      }
    }
  }
}

// downsample
const out = Buffer.alloc(SIZE * SIZE * 4);
for (let y = 0; y < SIZE; y++) {
  for (let x = 0; x < SIZE; x++) {
    let rr = 0, gg = 0, bb = 0, aa = 0;
    for (let sy = 0; sy < SS; sy++) {
      for (let sx = 0; sx < SS; sx++) {
        const i = (y * SS + sy) * W + (x * SS + sx);
        rr += r[i]; gg += g[i]; bb += b[i]; aa += a[i];
      }
    }
    const n = SS * SS;
    const o = (y * SIZE + x) * 4;
    const alpha = aa / n;
    // premultiplied back to straight alpha
    out[o] = alpha > 0 ? Math.round(rr / n) : 0;
    out[o + 1] = alpha > 0 ? Math.round(gg / n) : 0;
    out[o + 2] = alpha > 0 ? Math.round(bb / n) : 0;
    out[o + 3] = Math.round(alpha);
  }
}

// PNG encode
function crc32(buf) {
  let c, crc = 0xffffffff;
  for (let n = 0; n < buf.length; n++) {
    c = (crc ^ buf[n]) & 0xff;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crc = (crc >>> 8) ^ c;
  }
  return (crc ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(td) >>> 0);
  return Buffer.concat([len, td, crc]);
}

const raw = Buffer.alloc((SIZE * 4 + 1) * SIZE);
for (let y = 0; y < SIZE; y++) {
  raw[y * (SIZE * 4 + 1)] = 0;
  out.copy(raw, y * (SIZE * 4 + 1) + 1, y * SIZE * 4, (y + 1) * SIZE * 4);
}
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(SIZE, 0); ihdr.writeUInt32BE(SIZE, 4);
ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;

const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk('IHDR', ihdr),
  chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
  chunk('IEND', Buffer.alloc(0))
]);

const target = process.argv[2] || 'docs/img/logo.png';
fs.writeFileSync(target, png);
console.log(target, png.length, 'bytes', SIZE + 'x' + SIZE);
