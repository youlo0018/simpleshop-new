<template>
  <view v-if="open" class="overlay" @click.self="emit('close')">
    <view class="sheet safe-bottom">
      <view class="sheet__handle" />
      <view class="sheet__head">
        <text class="sheet__title">切换商城</text>
        <button class="sheet__close" aria-label="关闭" @click="emit('close')">
          <AppIcon type="closeempty" :size="20" color="#8a8a8f" />
        </button>
      </view>
      <button
        v-for="item in platform.platforms"
        :key="item.platformCode"
        class="platform-row"
        @click="choose(item)"
      >
        <image v-if="item.logo" class="platform-row__logo" :src="item.logo" mode="aspectFill" />
        <view v-else class="platform-row__logo platform-row__logo--text">
          {{ item.mallName.slice(0, 1) }}
        </view>
        <view class="platform-row__body">
          <text class="platform-row__name">{{ item.mallName }}</text>
          <text class="platform-row__code">{{ item.platformCode }}</text>
        </view>
        <view v-if="item.platformCode === platform.current?.platformCode" class="platform-row__current">
          <AppIcon type="checkmarkempty" :size="16" color="#0071e3" />
          <text>当前</text>
        </view>
      </button>
    </view>
  </view>
</template>

<script setup lang="ts">
import type { PlatformInfo } from '@/core/types';
import AppIcon from './AppIcon.vue';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

defineProps<{ open: boolean }>();
const emit = defineEmits<{ close: [] }>();
const platform = usePlatformStore();
const session = useSessionStore();

async function choose(target: PlatformInfo) {
  if (target.platformCode === platform.current?.platformCode) {
    emit('close');
    return;
  }
  const confirmed = await new Promise<boolean>((resolve) => {
    uni.showModal({
      title: '切换商城',
      content: '切换后将回到首页，并清空当前商城的本地缓存。',
      success: (result) => resolve(result.confirm),
      fail: () => resolve(false),
    });
  });
  if (!confirmed) return;
  await platform.switchPlatform(target.platformCode);
  session.restore();
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.overlay {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: flex-end;
  background: rgba(0, 0, 0, 0.4);
}

.sheet {
  width: 100%;
  max-height: 72vh;
  overflow: auto;
  padding: 16rpx 24rpx 24rpx;
  border-radius: 24rpx 24rpx 0 0;
  background: #fff;
}

.sheet__handle {
  width: 72rpx;
  height: 8rpx;
  margin: 0 auto 20rpx;
  border-radius: 999rpx;
  background: #d9d9de;
}

.sheet__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8rpx 0 20rpx;
}

.sheet__title {
  font-size: 34rpx;
  font-weight: 600;
}

.sheet__close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 64rpx;
  height: 64rpx;
  padding: 0;
  border: 0;
  background: transparent;
}

.platform-row {
  display: flex;
  align-items: center;
  width: 100%;
  gap: 20rpx;
  min-height: 112rpx;
  padding: 16rpx;
  border: 0;
  border-top: 1rpx solid $line;
  background: transparent;
  text-align: left;
}

.platform-row__logo {
  width: 72rpx;
  height: 72rpx;
  flex: 0 0 72rpx;
  border-radius: 16rpx;
}

.platform-row__logo--text {
  display: flex;
  align-items: center;
  justify-content: center;
  background: $bg;
  color: $brand;
  font-size: 30rpx;
  font-weight: 600;
}

.platform-row__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 6rpx;
}

.platform-row__name {
  font-size: 30rpx;
  font-weight: 500;
}

.platform-row__code {
  color: $muted;
  font-size: 22rpx;
}

.platform-row__current {
  display: flex;
  align-items: center;
  gap: 4rpx;
  color: $brand;
  font-size: 24rpx;
}
</style>
