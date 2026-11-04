<template>
  <view class="tabbar safe-bottom">
    <button
      v-for="tab in tabs"
      :key="tab.key"
      class="tabbar__item"
      :class="{ 'tabbar__item--active': active === tab.key }"
      @click="open(tab)"
    >
      <image v-if="tab.iconPath" class="tabbar__image" :src="tab.iconPath" mode="aspectFit" />
      <AppIcon
        v-else
        class="tabbar__icon"
        :type="active === tab.key ? tab.activeIcon : tab.iconType"
        :size="21"
        :color="active === tab.key ? tabColor : '#97979d'"
      />
      <text class="tabbar__label">{{ tab.label }}</text>
    </button>
  </view>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useDesignStore } from '@/stores/design';
import { usePlatformStore } from '@/stores/platform';
import AppIcon from './AppIcon.vue';

const props = defineProps<{ active: string }>();
const design = useDesignStore();
const platform = usePlatformStore();

const fallback = [
  { key: 'index', label: '首页', iconType: 'home', activeIcon: 'home-filled', iconPath: '', path: '/pages/index/index' },
  { key: 'category', label: '分类', iconType: 'bars', activeIcon: 'bars', iconPath: '', path: '/pages/category/index' },
  { key: 'cart', label: '购物车', iconType: 'cart', activeIcon: 'cart-filled', iconPath: '', path: '/pages/cart/index' },
  { key: 'profile', label: '我的', iconType: 'person', activeIcon: 'person-filled', iconPath: '', path: '/pages/profile/index' },
];

const tabColor = computed(() => platform.current?.tabColor || '#0071e3');

const tabs = computed(() => {
  const configured = design.tabs;
  if (!Array.isArray(configured) || !configured.length) return fallback;
  return configured
    .filter((tab: any) => tab.visible !== false && tab.enabled !== false)
    .map((tab: any, index: number) => ({
      key: tab.key || tab.type || tab.pagePath?.split('/').filter(Boolean).pop() || `tab-${index}`,
      label: tab.label || tab.text || '入口',
      iconType: iconPairFor(tab).icon,
      activeIcon: iconPairFor(tab).active,
      iconPath: tab.iconPath || '',
      path: normalizePath(tab.path || tab.url || tab.pagePath || fallback[index % fallback.length].path),
    }));
});

function iconPairFor(tab: any) {
  const fromKey = String(tab.key || tab.type || tab.pagePath || '').toLowerCase();
  if (fromKey.includes('cart')) return { icon: 'cart', active: 'cart-filled' };
  if (fromKey.includes('categor')) return { icon: 'bars', active: 'bars' };
  if (fromKey.includes('mall') || fromKey.includes('shop')) return { icon: 'shop', active: 'shop-filled' };
  if (fromKey.includes('profile') || fromKey.includes('mine')) return { icon: 'person', active: 'person-filled' };
  return { icon: 'home', active: 'home-filled' };
}

function normalizePath(path: string) {
  return path.startsWith('/') ? path : `/${path}`;
}

function open(tab: any) {
  if (tab.key === props.active) return;
  uni.reLaunch({ url: tab.path });
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.tabbar {
  position: fixed;
  right: 0;
  bottom: 0;
  left: 0;
  z-index: 50;
  display: flex;
  min-height: 104rpx;
  padding: 8rpx 16rpx 10rpx;
  border-top: 1rpx solid $line;
  background: rgba(255, 255, 255, 0.96);
  box-shadow: 0 -8rpx 24rpx rgba(0, 0, 0, 0.03);
  backdrop-filter: blur(20px);
}

.tabbar__item {
  position: relative;
  display: flex;
  flex: 1;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 3rpx;
  min-width: 88rpx;
  padding: 0;
  border: 0;
  background: transparent;
  color: #9a9aa0;
}

.tabbar__icon {
  width: 46rpx;
  height: 46rpx;
}

.tabbar__image {
  width: 44rpx;
  height: 44rpx;
}

.tabbar__label {
  font-size: 21rpx;
  letter-spacing: 0;
}

.tabbar__item--active {
  color: $tab;
}

.tabbar__item--active::before {
  position: absolute;
  top: -8rpx;
  left: 50%;
  width: 32rpx;
  height: 4rpx;
  border-radius: 0 0 999rpx 999rpx;
  background: $tab;
  content: '';
  transform: translateX(-50%);
}
</style>
