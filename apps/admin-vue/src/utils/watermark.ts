const GRID_SIZE = 12;
const CELL_SIZE = 12;
const MAGIC = 0xad;
const DATA_BITS = 128;
const PAYLOAD_BITS = 144;
const CRC_BITS = 16;
const TIME_EPOCH_MS = Date.UTC(2024, 0, 1);
const MINUTE_MS = 60_000;
const MAX_TIME_MINUTES = 0xffffff;
const MASK_SALT = 'simpleshop-admin-watermark-v2-mask';

export const WATERMARK_TILE_SIZE = GRID_SIZE * CELL_SIZE;

export interface WatermarkSource {
  userId: string;
  sessionId?: string;
  capturedAt?: Date;
}

export interface WatermarkPayload {
  userId: string;
  sessionSuffix: string;
  capturedAtMs: number;
}

export interface DecodedWatermark extends WatermarkPayload {
  confidence: number;
  scale: number;
  offsetX: number;
  offsetY: number;
}

export function drawFingerprintTile(source: WatermarkSource): string {
  const canvas = document.createElement('canvas');
  canvas.width = WATERMARK_TILE_SIZE;
  canvas.height = WATERMARK_TILE_SIZE;
  const ctx = canvas.getContext('2d');
  if (!ctx) return '';

  const payload = encodeWatermarkPayload(source);
  for (let index = 0; index < payload.length; index++) {
    const encodedBit = payload[index] ^ maskBit(index);
    const row = Math.floor(index / GRID_SIZE);
    const col = index % GRID_SIZE;
    const positive = encodedBit
      ? 'rgba(255, 24, 24, 0.006)'
      : 'rgba(24, 24, 255, 0.006)';
    const negative = encodedBit
      ? 'rgba(24, 24, 255, 0.006)'
      : 'rgba(255, 24, 24, 0.006)';
    ctx.fillStyle = positive;
    ctx.fillRect(col * CELL_SIZE, row * CELL_SIZE, CELL_SIZE / 2, CELL_SIZE);
    ctx.fillStyle = negative;
    ctx.fillRect(
      col * CELL_SIZE + CELL_SIZE / 2,
      row * CELL_SIZE,
      CELL_SIZE / 2,
      CELL_SIZE,
    );
  }
  return canvas.toDataURL('image/png');
}

export function decodeFingerprint(image: ImageData): DecodedWatermark | null {
  let best: DecodedWatermark | null = null;
  const scales = [1, 2, 0.8, 0.9, 1.1, 1.2, 1.25, 1.5, 0.75];

  for (const scale of scales) {
    const tile = WATERMARK_TILE_SIZE * scale;
    const cell = CELL_SIZE * scale;
    if (tile < 36 || tile > 576) continue;

    const offsets = [0, -1, 1, -2, 2];
    for (const offsetX of offsets) {
      for (const offsetY of offsets) {
        const features = aggregateCells(image, tile, cell, offsetX, offsetY);
        if (!features) continue;

        const decoded = decodeFeatures(features);
        if (!decoded) continue;
        const candidate = toDecodedWatermark(decoded, scale, offsetX, offsetY);
        if (!best || candidate.confidence > best.confidence) best = candidate;
      }
    }
  }

  if (best && best.confidence >= 0.18) return best;
  return decodeWithPhaseSearch(image);
}

export function encodeWatermarkPayload(source: WatermarkSource): Uint8Array {
  const dataBits = new Uint8Array(DATA_BITS);
  let cursor = 0;
  writeBits(dataBits, cursor, 8, BigInt(MAGIC));
  cursor += 8;
  writeBits(dataBits, cursor, 64, toUint64(source.userId));
  cursor += 64;
  writeBits(dataBits, cursor, 24, BigInt(toTimeMinutes(source.capturedAt)));
  cursor += 24;
  writeBits(dataBits, cursor, 32, BigInt(toSessionCode(source.sessionId)));

  const crc = crc16(dataBits);
  const payload = new Uint8Array(PAYLOAD_BITS);
  payload.set(dataBits);
  writeBits(payload, DATA_BITS, CRC_BITS, BigInt(crc));
  return payload;
}

export function decodeWatermarkPayload(bits: Uint8Array): WatermarkPayload | null {
  if (bits.length !== PAYLOAD_BITS) return null;

  const dataBits = bits.slice(0, DATA_BITS);
  if (readBits(dataBits, 0, 8) !== BigInt(MAGIC)) return null;
  if (readBits(bits, DATA_BITS, CRC_BITS) !== BigInt(crc16(dataBits))) return null;

  const userId = readBits(dataBits, 8, 64).toString();
  const minutes = Number(readBits(dataBits, 72, 24));
  const sessionCode = Number(readBits(dataBits, 96, 32));
  return {
    userId,
    sessionSuffix: sessionCode.toString(16).padStart(8, '0'),
    capturedAtMs: TIME_EPOCH_MS + minutes * MINUTE_MS,
  };
}

function decodeWithPhaseSearch(image: ImageData) {
  let best: DecodedWatermark | null = null;
  const scales = [
    1.2, 1, 1.25, 1.5, 2, 0.8, 0.9, 1.1, 0.75, 0.67, 0.6, 0.7,
    0.85, 1.33, 1.4, 1.6, 1.67, 1.75, 2.25, 2.5, 0.5,
  ];
  const expectedMagic = Array.from({ length: 8 }, (_, index) => {
    const bit = Number((BigInt(MAGIC) >> BigInt(7 - index)) & 1n);
    return bit ^ maskBit(index);
  });

  // 截图工具可能裁剪边缘或重新缩放，水印原点不再固定为 (0, 0)。
  // 先用 8 位魔术字粗定位相位，再只对高分相位做完整解码。
  for (const scale of scales) {
    const tile = WATERMARK_TILE_SIZE * scale;
    const cell = CELL_SIZE * scale;
    const step = Math.max(2, cell * 0.75);
    const phases: Array<{ x: number; y: number; score: number }> = [];

    for (let originX = 0; originX < tile; originX += step) {
      for (let originY = 0; originY < tile; originY += step) {
        const score = scoreMagicCells(
          image,
          tile,
          cell,
          originX,
          originY,
          expectedMagic,
        );
        if (score !== null) phases.push({ x: originX, y: originY, score });
      }
    }

    phases.sort((a, b) => b.score - a.score);
    for (const phase of phases.slice(0, 6)) {
      const features = aggregateCells(image, tile, cell, phase.x, phase.y);
      if (!features) continue;
      const decoded = decodeFeatures(features);
      if (!decoded) continue;
      const candidate = toDecodedWatermark(decoded, scale, phase.x, phase.y);
      if (!best || candidate.confidence > best.confidence) best = candidate;
    }
  }

  return best && best.confidence >= 0.18 ? best : null;
}

function scoreMagicCells(
  image: ImageData,
  tile: number,
  cell: number,
  originX: number,
  originY: number,
  expectedBits: number[],
) {
  let score = 0;
  for (let index = 0; index < expectedBits.length; index++) {
    const value = sampleCell(image, tile, cell, originX, originY, 0, index, 1);
    if (value === null) return null;
    score += value * (expectedBits[index] ? 1 : -1);
  }
  return score;
}

function decodeFeatures(values: Float64Array) {
  const sorted = Array.from(values).sort((a, b) => a - b);
  let best: { payload: WatermarkPayload; confidence: number } | null = null;

  // 页面内容会让整体色偏不等于 0，甚至让 0/1 数量失衡。
  // 在稳定的分位区间内扫描判定阈值，只接受同时通过魔术字和 16 位 CRC 的结果。
  const firstIndex = Math.floor(PAYLOAD_BITS * 0.05);
  const lastIndex = Math.ceil(PAYLOAD_BITS * 0.95);
  for (let step = 0; step <= 64; step++) {
    const sortedIndex = Math.round(
      firstIndex + ((lastIndex - firstIndex) * step) / 64,
    );
    const threshold = (sorted[sortedIndex] + sorted[sortedIndex + 1]) / 2;
    const payload = new Uint8Array(PAYLOAD_BITS);
    let agreement = 0;
    let signal = 0;

    for (let index = 0; index < PAYLOAD_BITS; index++) {
      const deviation = values[index] - threshold;
      const encodedBit = deviation >= 0 ? 1 : 0;
      payload[index] = encodedBit ^ maskBit(index);
      const polarity = encodedBit ? 1 : -1;
      agreement += deviation * polarity;
      signal += Math.abs(deviation);
    }

    const decoded = decodeWatermarkPayload(payload);
    if (!decoded || signal < 0.0001) continue;
    const candidate = {
      payload: decoded,
      confidence: Math.max(0, agreement / signal),
    };
    if (!best || candidate.confidence > best.confidence) best = candidate;
  }
  return best;
}

function toDecodedWatermark(
  decoded: { payload: WatermarkPayload; confidence: number },
  scale: number,
  offsetX: number,
  offsetY: number,
): DecodedWatermark {
  return {
    ...decoded.payload,
    confidence: decoded.confidence,
    scale,
    offsetX,
    offsetY,
  };
}

function sampleCell(
  image: ImageData,
  tile: number,
  cell: number,
  originX: number,
  originY: number,
  row: number,
  col: number,
  sampleRadius = Math.max(1, Math.min(5, Math.floor(cell / 5))),
) {
  let total = 0;
  let count = 0;

  for (
    let centerY = originY + (row + 0.5) * cell;
    centerY < image.height - sampleRadius;
    centerY += tile
  ) {
    if (centerY < sampleRadius) continue;
    for (
      let centerX = originX + (col + 0.25) * cell;
      centerX < image.width - sampleRadius;
      centerX += tile
    ) {
      if (centerX < sampleRadius) continue;
      for (let dy = -sampleRadius; dy <= sampleRadius; dy++) {
        for (let dx = -sampleRadius; dx <= sampleRadius; dx++) {
          const x = Math.round(centerX + dx);
          const y = Math.round(centerY + dy);
          const rightX = Math.round(centerX + cell / 2 + dx);
          if (x < 0 || x >= image.width || rightX < 0 || rightX >= image.width) {
            continue;
          }
          const leftPixel = (y * image.width + x) * 4;
          const rightPixel = (y * image.width + rightX) * 4;
          total +=
            image.data[leftPixel]
            - image.data[leftPixel + 2]
            - image.data[rightPixel]
            + image.data[rightPixel + 2];
          count += 1;
        }
      }
    }
  }

  return count > 0 ? total / count : null;
}

function aggregateCells(
  image: ImageData,
  tile: number,
  cell: number,
  offsetX: number,
  offsetY: number,
) {
  const values = new Float64Array(PAYLOAD_BITS);
  const counts = new Uint32Array(PAYLOAD_BITS);
  // 每格左右半区做差，抵消控件背景的固定色偏；平铺次数继续进一步降噪。
  const sampleRadius = Math.max(1, Math.min(2, Math.floor(cell / 10)));

  for (let row = 0; row < GRID_SIZE; row++) {
    for (let col = 0; col < GRID_SIZE; col++) {
      const index = row * GRID_SIZE + col;
      const leftX = offsetX + (col + 0.25) * cell;
      const rightX = offsetX + (col + 0.75) * cell;
      const baseY = offsetY + (row + 0.5) * cell;

      for (let centerY = baseY; centerY < image.height; centerY += tile) {
        if (centerY < sampleRadius || centerY >= image.height - sampleRadius) continue;
        for (let centerX = leftX; centerX < image.width; centerX += tile) {
          if (centerX < sampleRadius || centerX >= image.width - sampleRadius) continue;
          for (let dy = -sampleRadius; dy <= sampleRadius; dy++) {
            for (let dx = -sampleRadius; dx <= sampleRadius; dx++) {
              const x = Math.round(centerX + dx);
              const y = Math.round(centerY + dy);
              const rightX = Math.round(centerX + cell / 2 + dx);
              if (rightX < 0 || rightX >= image.width) continue;
              const pixel = (y * image.width + x) * 4;
              const rightPixel = (y * image.width + rightX) * 4;
              values[index] +=
                image.data[pixel]
                - image.data[pixel + 2]
                - image.data[rightPixel]
                + image.data[rightPixel + 2];
              counts[index] += 1;
            }
          }
        }
      }
    }
  }

  for (let index = 0; index < values.length; index++) {
    if (counts[index] === 0) return null;
    values[index] /= counts[index];
  }
  return values;
}

function writeBits(target: Uint8Array, offset: number, length: number, value: bigint) {
  for (let bit = 0; bit < length; bit++) {
    const shift = BigInt(length - bit - 1);
    target[offset + bit] = Number((value >> shift) & 1n);
  }
}

function readBits(source: Uint8Array, offset: number, length: number) {
  let value = 0n;
  for (let bit = 0; bit < length; bit++) {
    value = (value << 1n) | BigInt(source[offset + bit] || 0);
  }
  return value;
}

function maskBit(index: number) {
  return hash32(`${MASK_SALT}|${index}`) & 1;
}

function toUint64(value: string) {
  if (/^\d+$/.test(value)) {
    return BigInt(value) & 0xffffffffffffffffn;
  }

  let first = 0xcbf29ce484222325n;
  let second = 0x84222325cbf29ce4n;
  for (let index = 0; index < value.length; index++) {
    first ^= BigInt(value.charCodeAt(index));
    first = BigInt.asUintN(64, first * 0x100000001b3n);
    second ^= BigInt(value.charCodeAt(value.length - index - 1));
    second = BigInt.asUintN(64, second * 0x100000001b3n);
  }
  return BigInt.asUintN(64, first ^ second);
}

function toTimeMinutes(capturedAt = new Date()) {
  const minutes = Math.floor((capturedAt.getTime() - TIME_EPOCH_MS) / MINUTE_MS);
  return Math.max(0, Math.min(MAX_TIME_MINUTES, minutes));
}

function toSessionCode(sessionId = '') {
  const suffix = sessionId.replace(/[^a-f0-9]/gi, '').slice(-8);
  return suffix ? Number.parseInt(suffix, 16) >>> 0 : hash32(sessionId);
}

function crc16(bits: Uint8Array) {
  const bytes = new Uint8Array(DATA_BITS / 8);
  for (let byte = 0; byte < bytes.length; byte++) {
    let value = 0;
    for (let bit = 0; bit < 8; bit++) {
      value = (value << 1) | bits[byte * 8 + bit];
    }
    bytes[byte] = value;
  }

  let crc = 0xffff;
  for (const byte of bytes) {
    crc ^= byte << 8;
    for (let bit = 0; bit < 8; bit++) {
      crc = crc & 0x8000 ? ((crc << 1) ^ 0x1021) & 0xffff : (crc << 1) & 0xffff;
    }
  }
  return crc;
}

function hash32(value: string) {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index++) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  hash ^= hash >>> 16;
  hash = Math.imul(hash, 0x7feb352d);
  hash ^= hash >>> 15;
  return hash >>> 0;
}
