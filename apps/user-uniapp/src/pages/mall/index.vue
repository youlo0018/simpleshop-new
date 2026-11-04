<template>
  <view class="mall page-shell">
    <AppHeader title="商城">
      <template #right>
        <button class="header-search" aria-label="搜索" @click="go('/pages/search/index')">
          <AppIcon type="search" :size="20" color="#1a1a1a" />
        </button>
      </template>
    </AppHeader>
    <view class="tabs">
      <button v-for="tab in tabs" :key="tab.key" :class="{ active: active === tab.key }" @click="active = tab.key">{{ tab.label }}</button>
    </view>
    <template v-if="active === 'recommend'">
      <view class="sorts">
        <button v-for="sort in sorts" :key="sort.value" :class="{ active: sortBy === sort.value }" @click="changeSort(sort.value)">{{ sort.label }}</button>
      </view>
      <view class="product-grid"><ProductCard v-for="item in products" :key="item.productId" :product="item" /></view>
    </template>
    <template v-else-if="active === 'seckill'">
      <button class="feature card" @click="go('/pages/seckill/index')">
        <view class="feature__lead">
          <view class="feature__icon"><AppIcon type="fire-filled" :size="25" color="#ff7a00" /></view>
          <text>限时抢购</text>
        </view>
        <view class="feature__more"><text>查看今日场次</text><AppIcon type="right" :size="13" color="#8a8a8f" /></view>
      </button>
    </template>
    <template v-else>
      <view v-for="activity in activities" :key="activity.activityId" class="activity card" @click="openActivity(activity.activityId)">
        <image v-if="activity.coverImage" :src="activity.coverImage" mode="aspectFill" />
        <view><text>{{ activity.activityName }}</text><text>{{ activity.description }}</text></view>
      </view>
      <EmptyState v-if="!activities.length" title="活动正在准备中" icon="fire" />
    </template>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onReachBottom } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import ProductCard from '@/components/ProductCard.vue';
import EmptyState from '@/components/EmptyState.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import type { Paged, ProductItem } from '@/core/types';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';
import { useDesignStore } from '@/stores/design';

const platform = usePlatformStore();
const session = useSessionStore();
const design = useDesignStore();
const active = ref('recommend');
const sortBy = ref(0);
const products = ref<ProductItem[]>([]);
const activities = ref<any[]>([]);
const page = ref(1);
const total = ref(0);
const tabs = ref([
  { key: 'seckill', label: '秒杀' },
  { key: 'recommend', label: '推荐' },
  { key: 'activity', label: '活动' },
]);
const sorts = [
  { label: '综合', value: 0 },
  { label: '销量', value: 1 },
  { label: '价格', value: 2 },
  { label: '上新', value: 4 },
];

async function load(reset = true) {
  await platform.initialize();
  session.restore();
  await design.load();
  const configured = (design.config as any).mallTabs;
  if (Array.isArray(configured) && configured.length) {
    tabs.value = configured.filter((tab: any) => tab.visible !== false).map((tab: any) => ({
      key: tab.key || tab.type,
      label: tab.label || tab.text,
    }));
    if (!tabs.value.some((tab) => tab.key === active.value)) active.value = tabs.value[0]?.key || 'recommend';
  }
  if (reset) {
    page.value = 1;
    products.value = [];
  }
  if (active.value === 'recommend') {
    const result = await request<Paged<ProductItem>>('/gateway/shop/products/List', {
      method: 'POST',
      auth: false,
      data: {
        customerId: session.profile?.customerId || '0',
        platformId: platform.platformId,
        sortBy: sortBy.value,
        page: page.value,
        pageSize: 20,
      },
    });
    total.value = result.total;
    products.value = reset ? result.items : [...products.value, ...result.items];
  } else if (active.value === 'activity' && reset) {
    const result = await request<any[]>('/gateway/marketing/activities/Public', {
      method: 'POST',
      auth: false,
      data: { platformId: platform.platformId, limit: 20 },
    });
    activities.value = result || [];
  }
}

function changeSort(value: number) {
  sortBy.value = value;
  load();
}

function openActivity(id: string) {
  uni.navigateTo({ url: `/pages/activity/index?id=${id}` });
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad(() => load());
onReachBottom(() => {
  if (active.value !== 'recommend' || products.value.length >= total.value) return;
  page.value += 1;
  load(false);
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.header-search {
  width: 88rpx;
  height: 88rpx;
  border: 0;
  background: transparent;
  justify-content: center;
}

.tabs {
  display: flex;
  gap: 8rpx;
  padding: 16rpx 0;
  border-bottom: 1rpx solid $line;
}

.tabs button {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 64rpx;
  padding: 0 26rpx;
  border: 0;
  border-radius: 999rpx;
  background: transparent;
  color: $muted;
  font-size: 25rpx;
  line-height: 1;
}

.tabs button.active {
  background: $selection-bg;
  color: $selection-text;
  font-weight: 600;
}

.sorts {
  display: flex;
  gap: 8rpx;
  padding: 18rpx 0;
}

.sorts button {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 56rpx;
  padding: 0 18rpx;
  border: 0;
  border-radius: 999rpx;
  background: transparent;
  color: $muted;
  font-size: 23rpx;
  line-height: 1;
}

.sorts button.active {
  background: $selection-bg;
  color: $selection-text;
  font-weight: 500;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20rpx;
}

.feature,
.activity {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  padding: 30rpx;
  margin-top: 20rpx;
  border: 0;
  text-align: left;
}

.feature__lead,
.feature__more {
  display: flex;
  align-items: center;
}

.feature__lead {
  gap: 16rpx;
  font-size: 30rpx;
  font-weight: 600;
}

.feature__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 68rpx;
  height: 68rpx;
  border-radius: 20rpx;
  background: #fff0db;
}

.feature__more {
  gap: 2rpx;
  color: $muted;
  font-size: 23rpx;
}

.activity {
  justify-content: flex-start;
  gap: 16rpx;
}

.activity image {
  width: 210rpx;
  height: 130rpx;
  border-radius: 14rpx;
}

.activity view {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
}

.activity view text:last-child {
  color: $muted;
  font-size: 22rpx;
}
</style>
