<template>
  <view class="product-list page-shell">
    <AppHeader :title="title" back>
      <template #right>
        <button class="header-search" aria-label="搜索" @click="go('/pages/search/index')">
          <AppIcon type="search" :size="20" color="#1a1a1a" />
        </button>
      </template>
    </AppHeader>
    <view class="filter-bar">
      <button :class="{ active: sortBy === 0 }" @click="setSort(0)">综合</button>
      <button :class="{ active: sortBy === 1 }" @click="setSort(1)">销量</button>
      <button :class="{ active: sortBy === 2 || sortBy === 3 }" @click="togglePrice">
        价格 {{ sortBy === 3 ? '↓' : '↑' }}
      </button>
      <button :class="{ active: sortBy === 4 }" @click="setSort(4)">上新</button>
      <button class="filter-trigger" @click="filterOpen = !filterOpen">
        <AppIcon type="tune" :size="14" color="#8a8a8f" />
        <text>筛选</text>
      </button>
    </view>
    <view v-if="filterOpen" class="filter-panel card">
      <text class="filter-panel__title">价格区间</text>
      <view class="filter-panel__chips">
        <button v-for="range in ranges" :key="range.label" @click="applyRange(range)">{{ range.label }}</button>
      </view>
      <view class="filter-panel__inputs">
        <input v-model="minPrice" type="digit" placeholder="最低价" />
        <text>—</text>
        <input v-model="maxPrice" type="digit" placeholder="最高价" />
      </view>
      <view class="filter-panel__actions">
        <button @click="reset">重置</button>
        <button class="filter-panel__confirm" @click="confirmFilter">确定</button>
      </view>
    </view>
    <view class="result-meta">共 {{ total }} 件商品</view>
    <view class="product-grid">
      <ProductCard v-for="item in products" :key="item.productId" :product="item" />
    </view>
    <EmptyState v-if="!loading && !products.length" title="没有找到相关商品" action="重置筛选" icon="search" @action="reset" />
    <view v-if="loading" class="loading">加载中...</view>
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

const platform = usePlatformStore();
const session = useSessionStore();
const products = ref<ProductItem[]>([]);
const total = ref(0);
const page = ref(1);
const loading = ref(false);
const title = ref('全部商品');
const keyword = ref('');
const categoryId = ref('');
const activityId = ref('');
const couponId = ref('');
const merchantId = ref('');
const sortBy = ref(0);
const minPrice = ref('');
const maxPrice = ref('');
const filterOpen = ref(false);
const ranges = [
  { label: '0-50', min: 0, max: 50 },
  { label: '50-100', min: 50, max: 100 },
  { label: '100-200', min: 100, max: 200 },
  { label: '200以上', min: 200, max: '' },
];

async function load(resetPage = true) {
  if (!platform.ready) await platform.initialize();
  if (resetPage) {
    page.value = 1;
    products.value = [];
  }
  loading.value = true;
  try {
    // 关键词搜索必须走 ES；/products/List 的 keyword 是数据库 LIKE，
    // 无法利用 IK 分词与同义词（例如“眼睫毛”不会命中“睫毛膏”）。
    const hasMerchantFilter = Boolean(merchantId.value) && merchantId.value !== '0';
    const useSearchIndex = Boolean(keyword.value.trim()) && !hasMerchantFilter;
    const endpoint = useSearchIndex ? '/gateway/shop/products/Search' : '/gateway/shop/products/List';
    const result = await request<Paged<ProductItem>>(endpoint, {
      method: 'POST',
      auth: false,
      data: {
        customerId: session.profile?.customerId || '0',
        platformId: platform.platformId,
        keyword: keyword.value,
        categoryId: categoryId.value || '0',
        activityId: activityId.value || '0',
        couponId: couponId.value || '0',
        merchantId: merchantId.value || '0',
        sortBy: sortBy.value,
        minPrice: minPrice.value ? Number(minPrice.value) : 0,
        maxPrice: maxPrice.value ? Number(maxPrice.value) : 0,
        page: page.value,
        pageSize: 20,
      },
    });
    total.value = result.total;
    products.value = resetPage ? result.items : [...products.value, ...result.items];
  } finally {
    loading.value = false;
  }
}

function setSort(value: number) {
  sortBy.value = value;
  load();
}

function togglePrice() {
  sortBy.value = sortBy.value === 2 ? 3 : 2;
  load();
}

function applyRange(range: any) {
  minPrice.value = String(range.min);
  maxPrice.value = range.max === '' ? '' : String(range.max);
  load();
}

function reset() {
  sortBy.value = 0;
  minPrice.value = '';
  maxPrice.value = '';
  filterOpen.value = false;
  load();
}

function confirmFilter() {
  load();
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad((query) => {
  keyword.value = String(query?.keyword || '');
  categoryId.value = String(query?.categoryId || '');
  activityId.value = String(query?.activityId || '');
  couponId.value = String(query?.couponId || '');
  merchantId.value = String(query?.merchantId || '0');
  title.value = String(query?.title || keyword.value || '全部商品');
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

.header-search {
  width: 88rpx;
  height: 88rpx;
  border: 0;
  background: transparent;
  justify-content: center;
}

.filter-bar {
  position: sticky;
  top: calc(88rpx + env(safe-area-inset-top));
  z-index: 20;
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  height: 84rpx;
  border-bottom: 1rpx solid $line;
  background: rgba(245, 246, 248, 0.94);
  backdrop-filter: blur(16px);
}

.filter-bar button {
  display: flex;
  align-items: center;
  justify-content: center;
  align-self: center;
  justify-self: center;
  height: 56rpx;
  padding: 0 16rpx;
  border: 0;
  border-radius: 999rpx;
  background: transparent;
  color: $muted;
  font-size: 24rpx;
  line-height: 1;
}

.filter-bar button.active {
  background: $selection-bg;
  color: $selection-text;
  font-weight: 500;
}

.filter-trigger {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4rpx;
}

.filter-panel {
  padding: 24rpx;
  margin-top: 16rpx;
}

.filter-panel__title {
  font-size: 28rpx;
  font-weight: 600;
}

.filter-panel__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 12rpx;
  margin-top: 18rpx;
}

.filter-panel__chips button {
  height: 58rpx;
  padding: 0 22rpx;
  border: 0;
  border-radius: 999rpx;
  background: #f2f3f5;
  font-size: 23rpx;
}

.filter-panel__inputs {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  align-items: center;
  gap: 14rpx;
  margin-top: 24rpx;
}

.filter-panel__inputs input {
  height: 76rpx;
  padding: 0 20rpx;
  border-radius: 12rpx;
  background: #f4f5f7;
  font-size: 25rpx;
}

.filter-panel__actions {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16rpx;
  margin-top: 24rpx;
}

.filter-panel__actions button {
  height: 76rpx;
  border: 0;
  border-radius: 999rpx;
  background: #f2f3f5;
}

.filter-panel__actions .filter-panel__confirm {
  background: $brand;
  color: #fff;
}

.result-meta {
  padding: 22rpx 4rpx 18rpx;
  color: $muted;
  font-size: 23rpx;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20rpx;
}

.loading {
  padding: 32rpx;
  color: $muted;
  font-size: 24rpx;
  text-align: center;
}
</style>
