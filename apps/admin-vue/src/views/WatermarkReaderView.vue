<template>
  <div>
    <div class="head">
      <h2 class="head__title">水印解析</h2>
      <el-button type="primary" @click="chooseFile">选择截图</el-button>
      <input
        ref="fileInput"
        class="file-input"
        type="file"
        accept="image/png,image/jpeg,image/webp"
        @change="onFileChange"
      />
    </div>

    <section
      class="panel upload-panel"
      :class="{ 'upload-panel--active': dragging }"
      @dragenter.prevent="dragging = true"
      @dragover.prevent
      @dragleave.prevent="dragging = false"
      @drop.prevent="onDrop"
    >
      <template v-if="!state.fileName">
        <el-icon class="upload-panel__icon"><View /></el-icon>
        <p class="upload-panel__title">拖入或选择后台截图</p>
        <p class="upload-panel__hint">图片只在当前浏览器本地处理，不会上传服务器</p>
      </template>
      <div v-else class="workspace">
        <div class="preview">
          <div class="preview__meta">
            <span>{{ state.fileName }}</span>
            <span>{{ state.imageWidth }} × {{ state.imageHeight }}</span>
          </div>
          <div class="preview__canvas">
            <canvas ref="processedCanvas" :style="{ width: `${state.zoom}%` }" />
          </div>
        </div>

        <aside class="side">
          <section class="side__section">
            <div class="side__title">增强参数</div>
            <label class="field">
              <span>旋转角度 <b>{{ state.angle }}°</b></span>
              <el-slider v-model="state.angle" :min="-45" :max="45" :step="1" @change="render" />
            </label>
            <label class="field">
              <span>灰度下限 <b>{{ Math.round(state.low * 100) }}%</b></span>
              <el-slider v-model="state.low" :min="0.7" :max="0.98" :step="0.005" @change="render" />
            </label>
            <label class="field">
              <span>灰度上限 <b>{{ Math.round(state.high * 100) }}%</b></span>
              <el-slider v-model="state.high" :min="0.8" :max="1" :step="0.005" @change="render" />
            </label>
            <label class="field">
              <span>缩放 <b>{{ state.zoom }}%</b></span>
              <el-slider v-model="state.zoom" :min="50" :max="250" :step="10" />
            </label>
            <div class="actions">
              <el-button :loading="state.ocrRunning" type="primary" @click="runOcr">
                自动识别
              </el-button>
              <el-button :disabled="state.ocrRunning" @click="resetEffects">重置参数</el-button>
            </div>
            <p v-if="state.ocrStatus" class="progress">{{ state.ocrStatus }}</p>
          </section>

          <section class="side__section">
            <div class="side__title">识别结果</div>
            <dl class="result">
              <dt>登录账号</dt>
              <dd>{{ emptyText(state.account) }}</dd>
              <dt>昵称</dt>
              <dd>{{ emptyText(state.nickName) }}</dd>
              <dt>用户 ID</dt>
              <dd>{{ emptyText(state.userId) }}</dd>
              <dt>会话后缀</dt>
              <dd>{{ emptyText(state.sessionSuffix) }}</dd>
              <dt>截图时间</dt>
              <dd>{{ emptyText(state.captureTime) }}</dd>
              <dt>识别方式</dt>
              <dd>{{ emptyText(state.matchMethod) }}</dd>
            </dl>
            <el-alert
              v-if="state.ocrDone && !hasStructuredResult"
              type="warning"
              :closable="false"
              show-icon
              title="未识别到结构化信息"
              description="调整旋转角度和灰度范围后重新识别，或在下方 OCR 原文中人工查找。"
            />
          </section>

          <section v-if="state.ocrText" class="side__section">
            <div class="side__title">OCR 原文</div>
            <pre class="ocr-text">{{ state.ocrText }}</pre>
          </section>
        </aside>
      </div>
    </section>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref, useTemplateRef } from 'vue';
import { ElMessage } from 'element-plus';
import { View } from '@element-plus/icons-vue';
import request from '@/api/request';
import { emptyText, formatDateTime } from '@/utils/format';
import { decodeFingerprint } from '@/utils/watermark';

interface WatermarkOcrState {
  fileName: string;
  imageWidth: number;
  imageHeight: number;
  angle: number;
  low: number;
  high: number;
  zoom: number;
  ocrRunning: boolean;
  ocrDone: boolean;
  ocrStatus: string;
  ocrText: string;
  account: string;
  nickName: string;
  userId: string;
  sessionSuffix: string;
  captureTime: string;
  matchMethod: string;
}

const fileInput = useTemplateRef<HTMLInputElement>('fileInput');
const processedCanvas = useTemplateRef<HTMLCanvasElement>('processedCanvas');
const dragging = ref(false);
const state = reactive<WatermarkOcrState>({
  fileName: '',
  imageWidth: 0,
  imageHeight: 0,
  angle: 22,
  low: 0.88,
  high: 0.99,
  zoom: 100,
  ocrRunning: false,
  ocrDone: false,
  ocrStatus: '',
  ocrText: '',
  account: '',
  nickName: '',
  userId: '',
  sessionSuffix: '',
  captureTime: '',
  matchMethod: '',
});

let sourceData: ImageData | null = null;
let worker: any = null;

const hasStructuredResult = computed(
  () => Boolean(state.account || state.userId || state.sessionSuffix || state.captureTime),
);

function chooseFile() {
  fileInput.value?.click();
}

async function onFileChange(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (file) await loadFile(file);
}

async function onDrop(event: DragEvent) {
  dragging.value = false;
  const file = event.dataTransfer?.files?.[0];
  if (file) await loadFile(file);
}

async function loadFile(file: File) {
  if (!file.type.startsWith('image/')) {
    ElMessage.warning('请选择 PNG、JPG 或 WebP 图片');
    return;
  }

  const url = URL.createObjectURL(file);
  try {
    const image = await new Promise<HTMLImageElement>((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error('图片读取失败'));
      img.src = url;
    });

    const scale = Math.min(1, 2600 / image.naturalWidth);
    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
    canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
    const ctx = canvas.getContext('2d', { willReadFrequently: true });
    if (!ctx) throw new Error('当前浏览器不支持 Canvas');
    ctx.drawImage(image, 0, 0, canvas.width, canvas.height);

    sourceData = ctx.getImageData(0, 0, canvas.width, canvas.height);
    state.fileName = file.name;
    state.imageWidth = canvas.width;
    state.imageHeight = canvas.height;
    state.ocrText = '';
    state.account = '';
    state.nickName = '';
    state.userId = '';
    state.sessionSuffix = '';
    state.captureTime = '';
    state.matchMethod = '';
    state.ocrDone = false;
    state.ocrStatus = '';
    await nextTick();
    renderOriginal();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : '图片读取失败');
  } finally {
    URL.revokeObjectURL(url);
  }
}

function renderOriginal() {
  const canvas = processedCanvas.value;
  if (!canvas || !sourceData) return;
  canvas.width = sourceData.width;
  canvas.height = sourceData.height;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  ctx.putImageData(sourceData, 0, 0);
}

function render() {
  const canvas = processedCanvas.value;
  if (!canvas || !sourceData) return;

  const low = Math.min(state.low, state.high) * 255;
  const high = Math.max(state.low, state.high) * 255;
  const binary = new ImageData(
    new Uint8ClampedArray(sourceData.data),
    sourceData.width,
    sourceData.height,
  );

  for (let i = 0; i < binary.data.length; i += 4) {
    const luminance = binary.data[i] * 0.299 + binary.data[i + 1] * 0.587 + binary.data[i + 2] * 0.114;
    const value = luminance >= low && luminance <= high ? 0 : 255;
    binary.data[i] = value;
    binary.data[i + 1] = value;
    binary.data[i + 2] = value;
    binary.data[i + 3] = 255;
  }

  const bandCanvas = document.createElement('canvas');
  bandCanvas.width = sourceData.width;
  bandCanvas.height = sourceData.height;
  const bandCtx = bandCanvas.getContext('2d');
  if (!bandCtx) return;
  bandCtx.putImageData(binary, 0, 0);

  const radians = (state.angle * Math.PI) / 180;
  const cos = Math.abs(Math.cos(radians));
  const sin = Math.abs(Math.sin(radians));
  canvas.width = Math.ceil(sourceData.width * cos + sourceData.height * sin);
  canvas.height = Math.ceil(sourceData.width * sin + sourceData.height * cos);
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.translate(canvas.width / 2, canvas.height / 2);
  ctx.rotate(radians);
  ctx.drawImage(bandCanvas, -bandCanvas.width / 2, -bandCanvas.height / 2);
}

function resetEffects() {
  state.angle = 22;
  state.low = 0.88;
  state.high = 0.99;
  state.zoom = 100;
  state.ocrText = '';
  state.account = '';
  state.nickName = '';
  state.userId = '';
  state.sessionSuffix = '';
  state.captureTime = '';
  state.matchMethod = '';
  state.ocrDone = false;
  state.ocrStatus = '';
  render();
}

async function ensureWorker() {
  if (worker) return worker;

  state.ocrStatus = '正在加载 OCR 引擎…';
  const tesseract = await import('tesseract.js');
  worker = await tesseract.createWorker('eng', tesseract.OEM.LSTM_ONLY, {
    workerPath: `${window.location.origin}/tesseract/worker.min.js`,
    corePath: `${window.location.origin}/tesseract-core`,
    langPath: `${window.location.origin}/tessdata`,
    gzip: true,
    cacheMethod: 'readOnly',
    logger: (message: { status?: string; progress?: number }) => {
      if (message.status) {
        const percent = Math.round(Number(message.progress || 0) * 100);
        state.ocrStatus = percent > 0 ? `${message.status} ${percent}%` : message.status;
      }
    },
  });
  await worker.setParameters({
    tessedit_pageseg_mode: tesseract.PSM.SPARSE_TEXT,
    preserve_interword_spaces: '1',
  });
  return worker;
}

async function runOcr() {
  if (!processedCanvas.value || !sourceData) {
    ElMessage.warning('请先选择截图');
    return;
  }

  state.ocrRunning = true;
  state.ocrDone = false;
  state.ocrStatus = '准备识别…';
  try {
    const matched = await runFingerprintMatch();
    if (!matched) {
      render();
      await nextTick();
      const ocr = await ensureWorker();
      const result = await ocr.recognize(processedCanvas.value);
      const text = String(result?.data?.text || '');
      state.ocrText = text.trim();
      parseWatermark(text);
      await resolveAccount();
    }
    state.ocrDone = true;
    state.ocrStatus = hasStructuredResult.value ? '识别完成' : '识别完成，未找到结构化字段';
  } catch (error) {
    state.ocrStatus = '';
    ElMessage.error(error instanceof Error ? error.message : '水印识别失败');
  } finally {
    state.ocrRunning = false;
  }
}

async function runFingerprintMatch() {
  if (!sourceData) return false;
  state.ocrStatus = '正在解析数字水印，兼容截图裁剪与缩放…';
  await nextTick();
  await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));
  const decoded = decodeFingerprint(sourceData);
  if (!decoded) return false;

  state.userId = decoded.userId;
  state.sessionSuffix = decoded.sessionSuffix;
  state.captureTime = formatDateTime(new Date(decoded.capturedAtMs));
  state.matchMethod = `数字水印解码 ${(decoded.confidence * 100).toFixed(1)}%`;
  await resolveAccountById();
  state.ocrStatus = '数字水印解析成功';
  return true;
}

async function resolveAccountById() {
  if (!state.userId) return;
  try {
    const found = await findAccountById(state.userId);
    if (!found) return;
    state.account = String(found.userName || '');
    state.nickName = String(found.nickName || '');
  } catch {
    // 用户目录查询失败时仍保留数字水印中的用户 Id。
  }
}

async function findAccountById(userId: string) {
  for (let page = 1; page <= 50; page++) {
    const rows = await request('/gateway/users/List', {
      method: 'GET',
      params: { page, pageSize: 100 },
      silent: true,
    });
    const list = Array.isArray(rows) ? rows : [];
    const found = list.find((row: any) => String(row.id || '') === userId);
    if (found) return found;
    if (list.length < 100) break;
  }
  return null;
}

function parseWatermark(text: string) {
  if (!state.userId) {
    state.userId = mostFrequent(
      [...text.matchAll(/\bU\s*[:：]?\s*(\d{8,})/gi)].map((match) => match[1]),
    );
  }
  if (!state.sessionSuffix) {
    state.sessionSuffix = mostFrequent(
      [...text.matchAll(/\bS\s*[:：]?\s*([a-f0-9]{6,12})/gi)].map((match) => match[1]),
    );
  }
  if (!state.captureTime) {
    state.captureTime = mostFrequent(
      [...text.matchAll(/(20\d{2}[-/.年]\d{1,2}[-/.月]\d{1,2}日?\s+\d{1,2}:\d{2})/g)].map((match) =>
        match[1].replaceAll('年', '-').replaceAll('月', '-').replaceAll('日', ''),
      ),
    );
  }
  if (!state.account) {
    state.account = preferAccountCandidate(
      [...text.matchAll(/\b([a-z][a-z0-9_.-]{2,40})\b/gi)].map((match) => match[1]),
    );
  }
}

async function resolveAccount() {
  if (!state.account) return;
  try {
    const rows = await request('/gateway/users/List', {
      method: 'GET',
      params: { page: 1, pageSize: 20, keyword: state.account },
      silent: true,
    });
    const list = Array.isArray(rows) ? rows : [];
    const matched = list.find(
      (row: any) => String(row.userName || '').toLowerCase() === state.account.toLowerCase(),
    );
    if (!matched) return;
    state.account = String(matched.userName || state.account);
    state.nickName = String(matched.nickName || '');
    state.userId = String(matched.id || state.userId);
  } catch {
    // 账号目录查询失败时保留 OCR 结果，不阻断图片处理。
  }
}

function mostFrequent(values: string[]) {
  const counts = new Map<string, number>();
  for (const value of values) {
    const normalized = value.trim();
    if (normalized) counts.set(normalized, (counts.get(normalized) || 0) + 1);
  }
  return [...counts.entries()].sort((a, b) => b[1] - a[1])[0]?.[0] || '';
}

function preferAccountCandidate(values: string[]) {
  const counts = new Map<string, number>();
  for (const raw of values) {
    const value = raw.trim().toLowerCase();
    if (value.length < 3 || ['the', 'and', 'ocr', 'png', 'jpg'].includes(value)) continue;
    counts.set(value, (counts.get(value) || 0) + 1);
  }
  return [...counts.entries()]
    .sort((a, b) => {
      const score = (value: string, count: number) =>
        count * 10
        + (value.includes('admin') ? 100 : 0)
        + (value.startsWith('codex') ? 40 : 0)
        - Math.abs(value.length - 10);
      return score(b[0], b[1]) - score(a[0], a[1]);
    })[0]?.[0] || '';
}

onBeforeUnmount(() => {
  worker?.terminate?.();
  worker = null;
});
</script>

<style scoped>
.head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-4);
  margin-bottom: var(--space-5);
}

.head__title {
  margin: 0;
  font-size: var(--text-display);
  line-height: var(--lh-display);
  font-weight: 600;
}

.file-input {
  display: none;
}

.panel {
  border-radius: var(--radius-card);
  background: var(--bg-card);
  box-shadow: var(--shadow-card);
}

.upload-panel {
  min-height: 420px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-direction: column;
  padding: var(--space-6);
  border: 1px dashed var(--hairline);
  transition: border-color var(--duration-fast) var(--ease), background var(--duration-fast) var(--ease);
}

.upload-panel--active {
  border-color: var(--brand);
  background: rgba(0, 113, 227, 0.03);
}

.upload-panel__icon {
  font-size: 48px;
  color: var(--text-3);
}

.upload-panel__title {
  margin: var(--space-4) 0 0;
  font-size: var(--text-title-2);
  font-weight: 600;
}

.upload-panel__hint {
  margin: var(--space-2) 0 0;
  color: var(--text-2);
}

.workspace {
  width: 100%;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 340px;
  gap: var(--space-4);
  align-items: start;
}

.preview {
  min-width: 0;
}

.preview__meta {
  display: flex;
  justify-content: space-between;
  gap: var(--space-3);
  margin-bottom: var(--space-2);
  font-size: var(--text-foot);
  color: var(--text-2);
}

.preview__canvas {
  max-height: 720px;
  overflow: auto;
  border: 1px solid var(--hairline);
  border-radius: var(--radius-md);
  background: #ffffff;
}

.preview__canvas canvas {
  display: block;
  min-width: 100%;
  height: auto;
}

.side {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}

.side__section {
  padding: var(--space-4);
  border-radius: var(--radius-md);
  background: var(--bg-page);
}

.side__title {
  margin-bottom: var(--space-3);
  font-weight: 600;
}

.field {
  display: block;
  margin-bottom: var(--space-3);
}

.field > span {
  display: flex;
  justify-content: space-between;
  margin-bottom: var(--space-1);
  font-size: var(--text-foot);
  color: var(--text-2);
}

.field b {
  color: var(--text-1);
  font-variant-numeric: tabular-nums;
}

.actions {
  display: flex;
  gap: var(--space-2);
}

.progress {
  margin: var(--space-2) 0 0;
  font-size: var(--text-foot);
  color: var(--text-2);
}

.result {
  display: grid;
  grid-template-columns: 80px minmax(0, 1fr);
  gap: var(--space-2) var(--space-3);
  margin: 0;
}

.result dt {
  color: var(--text-2);
  font-size: var(--text-foot);
}

.result dd {
  margin: 0;
  overflow-wrap: anywhere;
  font-family: var(--font-mono);
  font-size: var(--text-foot);
}

.ocr-text {
  max-height: 220px;
  margin: 0;
  overflow: auto;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  font-family: var(--font-mono);
  font-size: var(--text-note);
  line-height: 1.6;
}

@media (max-width: 1100px) {
  .workspace {
    grid-template-columns: 1fr;
  }
}
</style>
