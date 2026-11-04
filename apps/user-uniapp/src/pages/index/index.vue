<template>
  <view class="home">
    <view class="home__top">
      <button class="platform-trigger" @click="switcherOpen = true">
        <image v-if="platform.current?.logo" class="platform-trigger__logo" :src="platform.current.logo" mode="aspectFill" />
        <view v-else class="platform-trigger__logo platform-trigger__logo--text">
          {{ (platform.current?.mallName || '商').slice(0, 1) }}
        </view>
        <text class="platform-trigger__name">{{ platform.current?.mallName || 'SimpleShop' }}</text>
        <AppIcon class="platform-trigger__arrow" type="arrow-down" :size="13" color="#8a8a8f" />
      </button>
      <button class="home__search" @click="go('/pages/search/index')">
        <AppIcon class="home__search-icon" type="search" :size="18" color="#8a8a8f" />
        <text>{{ searchPlaceholder }}</text>
      </button>
      <view class="home__right-spacer" />
    </view>

    <view v-if="loading" class="home__skeleton">
      <view class="skeleton skeleton--hero" />
      <view class="skeleton skeleton--grid" />
    </view>
    <view v-else class="home__content">
      <DesignComponent
        v-for="(component, index) in design.homeComponents"
        :key="component.id || `${component.type}-${index}`"
        :component="component"
        :products="products"
        :coupons="coupons"
        :activities="activities"
        :notices="notices"
        :shops="shops"
        :categories="categories"
      />
    </view>

    <BottomNav active="index" />
    <PlatformSwitcher :open="switcherOpen" @close="switcherOpen = false" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onLoad, onPullDownRefresh } from '@dcloudio/uni-app';
import DesignComponent from '@/components/DesignComponent.vue';
import BottomNav from '@/components/BottomNav.vue';
import PlatformSwitcher from '@/components/PlatformSwitcher.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import type { Paged, ProductItem } from '@/core/types';
import { usePlatformStore } from '@/stores/platform';
import { useDesignStore } from '@/stores/design';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const design = useDesignStore();
const session = useSessionStore();
const loading = ref(true);
const switcherOpen = ref(false);
const products = ref<ProductItem[]>([]);
const coupons = ref<any[]>([]);
const activities = ref<any[]>([]);
const notices = ref<any[]>([]);
const shops = ref<any[]>([]);
const categories = ref<any[]>([]);
const searchPlaceholder = computed(() => {
  const search = design.homeComponents.find((component) => component.type === 'searchBar');
  return String(search?.props?.placeholder || '搜索商品');
});

async function load() {
  loading.value = true;
  try {
    if (!platform.ready) await platform.initialize();
    session.restore();
    await design.load();
    const customerId = session.profile?.customerId || '0';
    const [productPage, couponPage, activityList, noticeList, shopPage, categoryList] = await Promise.all([
      request<Paged<ProductItem>>('/gateway/shop/products/List', {
        method: 'POST',
        auth: false,
        data: { customerId, platformId: platform.platformId, page: 1, pageSize: 20 },
        silent: true,
      }).catch(() => ({ items: [], total: 0, page: 1, pageSize: 20 })),
      request<Paged<any>>('/gateway/coupons/Available', {
        method: 'POST',
        auth: false,
        data: { customerId, platformId: platform.platformId, page: 1, pageSize: 6 },
        silent: true,
      }).catch(() => ({ items: [], total: 0, page: 1, pageSize: 6 })),
      request<any[]>('/gateway/marketing/activities/Public', {
        method: 'POST',
        auth: false,
        data: { platformId: platform.platformId, limit: 6 },
        silent: true,
      }).catch(() => []),
      request<any[]>('/gateway/platforms/notices/Public', {
        method: 'POST',
        auth: false,
        data: { platformCode: platform.platformCode, limit: 20 },
        silent: true,
      }).catch(() => []),
      request<any>('/gateway/merchants/Shop', {
        auth: false,
        params: { platformId: platform.platformId, page: 1, pageSize: 5 },
        silent: true,
      }).catch(() => ({ items: [] })),
      request<any[]>('/gateway/shop/catalog/CategoryTree', {
        auth: false,
        params: { platformId: platform.platformId },
        silent: true,
      }).catch(() => []),
    ]);
    products.value = productPage.items || [];
    coupons.value = couponPage.items || [];
    activities.value = Array.isArray(activityList) ? activityList : [];
    notices.value = Array.isArray(noticeList) ? noticeList : [];
    shops.value = shopPage.items || [];
    categories.value = Array.isArray(categoryList) ? categoryList : [];
  } finally {
    loading.value = false;
    uni.stopPullDownRefresh();
  }
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad(load);
onPullDownRefresh(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.home {
  min-height: 100vh;
  padding: calc(env(safe-area-inset-top) + 16rpx) 24rpx calc(140rpx + env(safe-area-inset-bottom));
  background:
    radial-gradient(circle at 8% 2%, rgba(215, 234, 255, 0.72), transparent 22%),
    $bg;
}

.home__top {
  display: flex;
  align-items: center;
  gap: 16rpx;
  margin-bottom: 24rpx;
}

.platform-trigger {
  display: flex;
  align-items: center;
  width: 254rpx;
  height: 76rpx;
  padding: 0 12rpx 0 8rpx;
  gap: 10rpx;
  border: 0;
  border-radius: 999rpx;
  background: rgba(255, 255, 255, 0.86);
  box-shadow: $shadow;
  backdrop-filter: blur(16px);
}

.platform-trigger__logo {
  width: 54rpx;
  height: 54rpx;
  flex: 0 0 54rpx;
  border-radius: 999rpx;
}

.platform-trigger__logo--text {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #eef4ff;
  color: $brand;
  font-size: 24rpx;
  font-weight: 600;
}

.platform-trigger__name {
  flex: 1;
  overflow: hidden;
  font-size: 24rpx;
  font-weight: 500;
  text-align: left;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.platform-trigger__arrow {
  flex: 0 0 auto;
}

.home__search {
  display: flex;
  align-items: center;
  flex: 1;
  height: 76rpx;
  padding: 0 24rpx;
  gap: 10rpx;
  border: 0;
  border-radius: 999rpx;
  background: rgba(255, 255, 255, 0.88);
  box-shadow: $shadow;
  color: $muted;
  font-size: 25rpx;
  text-align: left;
  backdrop-filter: blur(16px);
}

.home__search-icon {
  flex: 0 0 auto;
}

.home__right-spacer {
  width: 0;
}

.home__content {
  animation: fade-up 250ms ease both;
}

.home__skeleton {
  display: flex;
  flex-direction: column;
  gap: 24rpx;
}

.skeleton {
  border-radius: 24rpx;
  background: linear-gradient(100deg, #eef0f3 20%, #f8f9fb 38%, #eef0f3 56%);
  background-size: 200% 100%;
  animation: shimmer 1.2s infinite linear;
}

.skeleton--hero {
  height: 320rpx;
}

.skeleton--grid {
  height: 480rpx;
}

@keyframes shimmer {
  to { background-position: -200% 0; }
}

@keyframes fade-up {
  from { opacity: 0; transform: translateY(8rpx); }
  to { opacity: 1; transform: translateY(0); }
}
</style>
