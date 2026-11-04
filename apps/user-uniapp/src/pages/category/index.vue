<template>
  <view class="category page-shell">
    <AppHeader title="分类">
      <template #right>
        <button class="header-search" aria-label="搜索" @click="go('/pages/search/index')">
          <AppIcon type="search" :size="20" color="#1a1a1a" />
        </button>
      </template>
    </AppHeader>
    <view class="category__body">
      <scroll-view scroll-y class="category__aside">
        <button
          v-for="item in tree"
          :key="item.id"
          class="category__tab"
          :class="{ 'category__tab--active': selected?.id === item.id }"
          @click="select(item)"
        >{{ item.categoryName }}</button>
      </scroll-view>
      <scroll-view scroll-y class="category__main">
        <view v-if="selected?.children?.length" class="category__icons">
          <button v-for="child in selected.children" :key="child.id" class="category__icon-item" @click="openCategory(child)">
            <image v-if="child.icon" class="category__icon-image" :src="child.icon" mode="aspectFit" />
            <view v-else class="category__icon-box">{{ child.categoryName.slice(0, 1) }}</view>
            <text>{{ child.categoryName }}</text>
          </button>
        </view>
        <view class="category__chips">
          <button
            v-for="chip in leafCategories"
            :key="chip.id"
            class="chip"
            @click="openCategory(chip)"
          >{{ chip.categoryName }}</button>
        </view>
        <view class="product-grid">
          <ProductCard v-for="item in products" :key="item.productId" :product="item" />
        </view>
        <EmptyState
          v-if="!loading && !products.length"
          title="该分类暂无商品"
          action="看看全部商品"
          @action="go('/pages/product/list')"
        />
      </scroll-view>
    </view>
    <BottomNav active="category" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import BottomNav from '@/components/BottomNav.vue';
import ProductCard from '@/components/ProductCard.vue';
import EmptyState from '@/components/EmptyState.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import type { Paged, ProductItem } from '@/core/types';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const tree = ref<any[]>([]);
const selected = ref<any>(null);
const products = ref<ProductItem[]>([]);
const loading = ref(false);

const leafCategories = computed(() => {
  const second = selected.value?.children || [];
  return second.flatMap((item: any) => item.children?.length ? item.children : [item]);
});

async function load() {
  if (!platform.ready) await platform.initialize();
  tree.value = await request<any[]>('/gateway/shop/catalog/CategoryTree', {
    auth: false,
    params: { platformId: platform.platformId },
    silent: true,
  }).catch(() => []);
  if (tree.value[0]) select(tree.value[0]);
}

async function select(item: any) {
  selected.value = item;
  await loadProducts(item.id);
}

async function loadProducts(categoryId: string) {
  loading.value = true;
  try {
    const page = await request<Paged<ProductItem>>('/gateway/shop/products/List', {
      method: 'POST',
      auth: false,
      data: {
        customerId: session.profile?.customerId || '0',
        platformId: platform.platformId,
        categoryId,
        page: 1,
        pageSize: 20,
      },
      silent: true,
    }).catch(() => ({ items: [], total: 0, page: 1, pageSize: 20 }));
    products.value = page.items || [];
  } finally {
    loading.value = false;
  }
}

function openCategory(item: any) {
  uni.navigateTo({ url: `/pages/product/list?categoryId=${item.id}&title=${encodeURIComponent(item.categoryName)}` });
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.category {
  padding-right: 0;
  padding-left: 0;
}

.header-search {
  width: 88rpx;
  height: 88rpx;
  border: 0;
  background: transparent;
  justify-content: center;
}

.category__body {
  display: flex;
  height: calc(100vh - 88rpx - env(safe-area-inset-top) - 120rpx);
}

.category__aside {
  width: 176rpx;
  flex: 0 0 176rpx;
  background: rgba(255, 255, 255, 0.7);
}

.category__tab {
  position: relative;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: 96rpx;
  padding: 12rpx;
  border: 0;
  background: transparent;
  color: $muted;
  font-size: 25rpx;
}

.category__tab--active {
  background: $selection-bg;
  color: $selection-text;
  font-weight: 600;
}

.category__tab--active::before {
  display: none;
}

.category__main {
  flex: 1;
  padding: 16rpx 24rpx 180rpx;
}

.category__icons {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 24rpx 12rpx;
  padding: 20rpx;
  border-radius: 20rpx;
  background: #fff;
}

.category__icon-item {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 10rpx;
  min-width: 0;
  padding: 0;
  border: 0;
  background: transparent;
  font-size: 22rpx;
}

.category__icon-image,
.category__icon-box {
  width: 72rpx;
  height: 72rpx;
  border-radius: 20rpx;
}

.category__icon-box {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f2f5f9;
  color: $brand;
}

.category__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 12rpx;
  padding: 24rpx 0;
}

.chip {
  height: 58rpx;
  padding: 0 22rpx;
  border: 0;
  border-radius: 999rpx;
  background: #fff;
  color: #4c4c50;
  font-size: 23rpx;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20rpx;
}
</style>
