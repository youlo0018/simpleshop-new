<template>
  <view class="header" :class="{ 'header--solid': solid, 'header--edge-actions': edgeActions }">
    <view class="header__side">
      <button v-if="back" class="icon-btn" aria-label="返回" @click="goBack">
        <AppIcon type="left" :size="22" color="#1a1a1a" />
      </button>
      <slot name="left" />
    </view>
    <view class="header__center">
      <text v-if="title" class="header__title">{{ title }}</text>
      <slot />
    </view>
    <view class="header__side header__side--right">
      <slot name="right" />
    </view>
  </view>
</template>

<script setup lang="ts">
import AppIcon from './AppIcon.vue';

withDefaults(defineProps<{
  title?: string;
  back?: boolean;
  solid?: boolean;
  edgeActions?: boolean;
}>(), {
  title: '',
  back: false,
  solid: true,
  edgeActions: false,
});

function goBack() {
  const pages = getCurrentPages();
  if (pages.length > 1) uni.navigateBack();
  else uni.reLaunch({ url: '/pages/index/index' });
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.header {
  position: sticky;
  top: 0;
  z-index: 30;
  display: grid;
  width: calc(100% + var(--page-gutter, 24rpx) + var(--page-gutter, 24rpx));
  grid-template-columns: 180rpx 1fr 180rpx;
  align-items: center;
  min-height: calc(88rpx + env(safe-area-inset-top));
  margin-right: var(--page-gutter-negative, -24rpx);
  margin-left: var(--page-gutter-negative, -24rpx);
  padding: env(safe-area-inset-top) 16rpx 0;
  background: rgba(255, 255, 255, 0.92);
  backdrop-filter: blur(20px);
}

.header--edge-actions {
  grid-template-columns: 88rpx minmax(0, 1fr) 88rpx;
}

.header--edge-actions .header__side {
  justify-content: center;
}

.header--edge-actions .header__side--right {
  justify-content: center;
}

.header--solid {
  border-bottom: 1rpx solid $line;
}

.header__center {
  display: flex;
  align-items: center;
  justify-content: center;
  min-width: 0;
}

.header__title {
  overflow: hidden;
  font-size: 34rpx;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.header__side {
  display: flex;
  align-items: center;
  min-width: 0;
}

.header__side--right {
  justify-content: flex-end;
}

.icon-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 88rpx;
  height: 88rpx;
  padding: 0;
  border: 0;
  border-radius: 999rpx;
  background: transparent;
  color: $text;
}
</style>
