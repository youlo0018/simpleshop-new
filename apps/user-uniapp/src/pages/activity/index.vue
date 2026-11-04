<template>
  <view class="activity-page page-shell">
    <AppHeader :title="activity?.activityName || '活动专区'" back />
    <view v-if="activity" class="hero card">
      <image v-if="activity.coverImage" :src="activity.coverImage" mode="aspectFill" />
      <view class="hero__body">
        <text class="hero__title">{{ activity.activityName }}</text>
        <text class="hero__time">{{ dateTime(activity.startTime) }} - {{ dateTime(activity.endTime) }}</text>
        <text class="hero__desc">{{ activity.description }}</text>
      </view>
    </view>
    <view class="result-meta">活动商品 {{ total }} 件</view>
    <view class="product-grid"><ProductCard v-for="item in products" :key="item.productId" :product="item" /></view>
    <EmptyState v-if="!products.length" title="该活动暂无商品" icon="fire" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onReachBottom } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import ProductCard from '@/components/ProductCard.vue';
import EmptyState from '@/components/EmptyState.vue';
import { request } from '@/core/http';
import { dateTime } from '@/core/format';
import type { Paged, ProductItem } from '@/core/types';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const activityId = ref('');
const activity = ref<any>(null);
const products = ref<ProductItem[]>([]);
const total = ref(0);
const page = ref(1);
const loading = ref(false);

async function load(reset = true) {
  await platform.initialize();
  session.restore();
  if (reset) {
    page.value = 1;
    products.value = [];
    const list = await request<any[]>('/gateway/marketing/activities/Public', {
      method: 'POST',
      auth: false,
      data: { activityId: activityId.value, platformId: platform.platformId, limit: 1 },
    });
    activity.value = list[0] || null;
  }
  loading.value = true;
  try {
    const result = await request<Paged<ProductItem>>('/gateway/shop/products/List', {
      method: 'POST',
      auth: false,
      data: {
        customerId: session.profile?.customerId || '0',
        platformId: platform.platformId,
        activityId: activityId.value,
        page: page.value,
        pageSize: 20,
      },
    });
    total.value = result.total;
    products.value = reset ? result.items : [...products.value, ...result.items];
  } finally {
    loading.value = false;
  }
}

onLoad((query) => {
  activityId.value = String(query?.id || '');
  load();
});
onReachBottom(() => {
  if (loading.value || products.value.length >= total.value) return;
  page.value += 1;
  load(false);
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.hero {
  overflow: hidden;
  margin-top: 20rpx;
}

.hero > image {
  width: 100%;
  height: 320rpx;
}

.hero__body {
  display: flex;
  flex-direction: column;
  gap: 10rpx;
  padding: 26rpx;
}

.hero__title {
  font-size: 36rpx;
  font-weight: 600;
}

.hero__time,
.hero__desc {
  color: $muted;
  font-size: 22rpx;
}

.hero__desc {
  line-height: 1.55;
}

.result-meta {
  padding: 24rpx 4rpx 18rpx;
  color: $muted;
  font-size: 23rpx;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20rpx;
}
</style>
