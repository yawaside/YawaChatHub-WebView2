// Генерирует desktop/build/icon.ico — валидный 32×32 32bpp ICO.
// Нужен, потому что ApplicationIcon в YawaChatHub.csproj требует
// наличия файла; без него CSC падает с "Error opening icon file".
import { writeFileSync, mkdirSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SIZE = 32;
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const out = resolve(root, "desktop", "build", "icon.ico");

// BGRA-буфер
const px = new Uint8Array(SIZE * SIZE * 4);

const hex = (c) => [
  parseInt(c.slice(1, 3), 16),
  parseInt(c.slice(3, 5), 16),
  parseInt(c.slice(5, 7), 16),
];
const c1 = hex("#8b7bff");
const c2 = hex("#38e8ff");
const white = [255, 255, 255];

const inRoundedRect = (x, y, x0, y0, x1, y1, r) => {
  if (x < x0 || x > x1 || y < y0 || y > y1) return false;
  const cx = Math.min(Math.max(x, x0 + r), x1 - r);
  const cy = Math.min(Math.max(y, y0 + r), y1 - r);
  const dx = x - cx, dy = y - cy;
  return dx * dx + dy * dy <= r * r;
};

for (let y = 0; y < SIZE; y++) {
  for (let x = 0; x < SIZE; x++) {
    if (!inRoundedRect(x, y, 1, 1, SIZE - 2, SIZE - 2, 7)) continue;
    const t = (x + y) / (2 * (SIZE - 1));
    const r = Math.round(c1[0] + (c2[0] - c1[0]) * t);
    const g = Math.round(c1[1] + (c2[1] - c1[1]) * t);
    const b = Math.round(c1[2] + (c2[2] - c1[2]) * t);
    const inBubble = inRoundedRect(x, y, 9, 10, 23, 21, 4);
    const inTail = x >= 11 && x <= 15 && y >= 22 && y <= 24;
    const i = (y * SIZE + x) * 4;
    if (inBubble || inTail) {
      px[i] = white[2]; px[i + 1] = white[1]; px[i + 2] = white[0]; px[i + 3] = 255;
    } else {
      px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
    }
  }
}

const header = Buffer.alloc(40);
header.writeUInt32LE(40, 0);
header.writeInt32LE(SIZE, 4);
header.writeInt32LE(SIZE, 8);   // XOR height (без AND)
header.writeUInt16LE(1, 12);
header.writeUInt16LE(32, 14);
header.writeUInt32LE(0, 16);    // biCompression = BI_RGB

const xor = Buffer.alloc(SIZE * SIZE * 4);
for (let row = 0; row < SIZE; row++) {
  const srcY = SIZE - 1 - row;
  Buffer.from(px.buffer, srcY * SIZE * 4, SIZE * 4).copy(xor, row * SIZE * 4);
}

const image = Buffer.concat([header, xor]);

const dir = Buffer.alloc(6);
dir.writeUInt16LE(0, 0); dir.writeUInt16LE(1, 2); dir.writeUInt16LE(1, 4);
const entry = Buffer.alloc(16);
entry.writeUInt8(SIZE, 0); entry.writeUInt8(SIZE, 1);
entry.writeUInt16LE(1, 4); entry.writeUInt16LE(32, 6);
entry.writeUInt32LE(image.length, 8);
entry.writeUInt32LE(22, 12);

mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, Buffer.concat([dir, entry, image]));
console.log(`✓ icon.ico (${image.length + 22} байт) -> ${out}`);
