<template>
  <div class="admin-watermark" aria-hidden="true">
    <div
      class="admin-watermark__code"
      :style="{
        backgroundImage: fingerprintImage ? `url(${fingerprintImage})` : undefined,
        backgroundSize: `${WATERMARK_TILE_SIZE}px ${WATERMARK_TILE_SIZE}px`,
      }"
    />
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, shallowRef } from 'vue';
import { getSession } from '@/utils/session';
import { drawFingerprintTile, WATERMARK_TILE_SIZE } from '@/utils/watermark';

const fingerprintImage = shallowRef('');
let timer: number | undefined;

function buildWatermark() {
  const session = getSession();
  if (!session) {
    fingerprintImage.value = '';
    return;
  }

  fingerprintImage.value = drawFingerprintTile({
    userId: session.userId || session.userName,
    sessionId: session.sessionId,
    capturedAt: new Date(),
  });
}

onMounted(() => {
  buildWatermark();
  timer = window.setInterval(buildWatermark, 60_000);
});

onBeforeUnmount(() => {
  if (timer) window.clearInterval(timer);
});
</script>

<style scoped>
.admin-watermark {
  position: fixed;
  inset: 0;
  z-index: 9999;
  pointer-events: none;
  user-select: none;
}

.admin-watermark__code {
  position: absolute;
  inset: 0;
  background-repeat: repeat;
  background-position: 0 0;
}
</style>
