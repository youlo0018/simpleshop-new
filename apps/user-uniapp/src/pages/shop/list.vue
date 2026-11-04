<template>
  <view class="shops page-shell">
    <AppHeader title="店铺" back />
    <view class="search-box"><AppIcon type="search" :size="18" color="#8a8a8f" /><input v-model="keyword" placeholder="搜索店铺名" @confirm="load" /></view>
    <view v-for="shop in shops" :key="shop.merchantId" class="shop-card card" @click="open(shop.merchantId)">
      <image v-if="shop.logo" :src="shop.logo" mode="aspectFill" />
      <view v-else class="shop-card__logo">{{ (shop.merchantName || '店').slice(0, 1) }}</view>
      <view class="shop-card__body">
        <text class="shop-card__name">{{ shop.merchantName }}</text>
        <text class="shop-card__desc">{{ shop.description || '精选好店' }}</text>
        <text class="shop-card__score">评分 {{ Number(shop.evaluationScore || 0).toFixed(1) }}</text>
      </view>
      <button>进店</button>
    </view>
    <EmptyState v-if="!loading && !shops.length" title="暂无店铺" icon="shop" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onReachBottom } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';

const shops = ref<any[]>([]);
const keyword = ref('');
const page = ref(1);
const total = ref(0);
const loading = ref(false);

async function load(reset = true) {
  if (reset) {
    page.value = 1;
    shops.value = [];
  }
  loading.value = true;
  try {
    const result = await request<any>('/gateway/merchants/Shop', {
      auth: false,
      params: { keyword: keyword.value, page: page.value, pageSize: 20 },
    });
    total.value = result.total;
    shops.value = reset ? result.items : [...shops.value, ...result.items];
  } finally {
    loading.value = false;
  }
}

function open(id: string) {
  uni.navigateTo({ url: `/pages/shop/store?id=${id}` });
}

onLoad(() => load());
onReachBottom(() => {
  if (loading.value || shops.value.length >= total.value) return;
  page.value += 1;
  load(false);
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.search-box {
  display: flex;
  align-items: center;
  height: 76rpx;
  padding: 0 22rpx;
  gap: 10rpx;
  border-radius: 999rpx;
  background: #fff;
  box-shadow: $shadow;
}

.search-box input {
  flex: 1;
  font-size: 25rpx;
}

.shop-card {
  display: flex;
  align-items: center;
  gap: 18rpx;
  padding: 22rpx;
  margin-top: 20rpx;
}

.shop-card > image,
.shop-card__logo {
  width: 104rpx;
  height: 104rpx;
  flex: 0 0 104rpx;
  border-radius: 18rpx;
}

.shop-card__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f1f3f6;
  color: $brand;
  font-size: 34rpx;
}

.shop-card__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 7rpx;
  min-width: 0;
}

.shop-card__name {
  font-size: 29rpx;
  font-weight: 600;
}

.shop-card__desc,
.shop-card__score {
  overflow: hidden;
  color: $muted;
  font-size: 22rpx;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.shop-card button {
  height: 62rpx;
  padding: 0 22rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 22rpx;
}
</style>
