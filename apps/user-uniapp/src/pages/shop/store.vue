<template>
  <view class="store page-shell">
    <AppHeader :title="shop?.merchantName || '店铺'" back />
    <view v-if="shop" class="shop-hero">
      <image v-if="shop.logo" :src="shop.logo" mode="aspectFill" />
      <view v-else class="shop-hero__logo">{{ shop.merchantName.slice(0, 1) }}</view>
      <view class="shop-hero__body">
        <text class="shop-hero__name">{{ shop.merchantName }}</text>
        <text class="shop-hero__score">评分 {{ Number(shop.evaluationScore || 0).toFixed(1) }}</text>
        <text class="shop-hero__desc">{{ shop.description }}</text>
      </view>
    </view>
    <template v-if="storeComponents.length">
      <DesignComponent
        v-for="(component, index) in storeComponents"
        :key="component.id || `store-${index}`"
        :component="component"
        :products="products"
        :activities="activities"
        :categories="categories"
        :reviews="reviews"
        :merchant-id="merchantId"
      />
    </template>

    <template v-else>
    <view class="tabs">
      <button :class="{ active: tab === 'products' }" @click="tab = 'products'">商品</button>
      <button :class="{ active: tab === 'activities' }" @click="tab = 'activities'">活动</button>
    </view>
    <template v-if="tab === 'products'">
      <scroll-view scroll-x class="chips" :show-scrollbar="false">
        <view class="chips__track">
          <button :class="{ active: !categoryId }" @click="selectCategory('')">全部</button>
          <button v-for="category in categories" :key="category.categoryId" :class="{ active: categoryId === category.categoryId }" @click="selectCategory(category.categoryId)">{{ category.categoryName }}</button>
        </view>
      </scroll-view>
      <view class="product-grid"><ProductCard v-for="item in products" :key="item.productId" :product="item" /></view>
    </template>
    <template v-else>
      <view v-for="activity in activities" :key="activity.activityId" class="activity card" @click="openActivity(activity.activityId)">
        <image v-if="activity.coverImage" :src="activity.coverImage" mode="aspectFill" />
        <view><text>{{ activity.activityName }}</text><text>{{ activity.description }}</text></view>
      </view>
    </template>
    <view class="shop-reviews card">
      <view class="section-head">
        <text class="section-title">店铺评价</text>
        <button class="section-more section-more--icon" @click="openReviews">
          <text>全部评价</text><AppIcon type="right" :size="13" color="#8a8a8f" />
        </button>
      </view>
      <view v-for="review in reviews" :key="review.evaluateId" class="review">
        <text>{{ review.customerName || '匿名用户' }} {{ '★'.repeat(review.starScore) }}</text>
        <text>{{ review.content }}</text>
      </view>
    </view>
    </template>
    <button class="cart-float" aria-label="购物车" @click="go('/pages/cart/index')">
      <AppIcon type="cart-filled" :size="22" color="#ffffff" />
    </button>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import ProductCard from '@/components/ProductCard.vue';
import DesignComponent from '@/components/DesignComponent.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import type { Paged, ProductItem } from '@/core/types';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const merchantId = ref('');
const shop = ref<any>(null);
const tab = ref('products');
const categories = ref<any[]>([]);
const categoryId = ref('');
const products = ref<ProductItem[]>([]);
const activities = ref<any[]>([]);
const reviews = ref<any[]>([]);
const storeComponents = ref<any[]>([]);

async function load() {
  await platform.initialize();
  session.restore();
  const [shopDetail, categoryList, activityPage, reviewPage, designConfig] = await Promise.all([
    request<any>(`/gateway/merchants/Shop/${merchantId.value}`, { auth: false, silent: true }).catch(() => null),
    request<any[]>('/gateway/shop/catalog/MerchantCategories', {
      auth: false,
      params: { merchantId: merchantId.value, platformId: platform.platformId },
      silent: true,
    }).catch(() => []),
    request<any[]>('/gateway/marketing/activities/Public', {
      method: 'POST',
      auth: false,
      data: { merchantId: merchantId.value, platformId: platform.platformId, limit: 10 },
      silent: true,
    }).catch(() => []),
    request<any>('/gateway/evaluates/Merchant', {
      method: 'POST',
      auth: false,
      data: { merchantId: merchantId.value, platformId: platform.platformId, page: 1, pageSize: 3 },
      silent: true,
    }).catch(() => ({ items: [] })),
    request<any>('/gateway/design/Store', {
      auth: false,
      params: { merchantId: merchantId.value },
      silent: true,
    }).catch(() => null),
  ]);
  shop.value = shopDetail;
  categories.value = categoryList || [];
  activities.value = Array.isArray(activityPage) ? activityPage : [];
  reviews.value = reviewPage.items || [];
  if (designConfig?.configJson) {
    try {
      const config = typeof designConfig.configJson === 'string'
        ? JSON.parse(designConfig.configJson)
        : designConfig.configJson;
      storeComponents.value = config?.pages?.store?.components || [];
    } catch {
      storeComponents.value = [];
    }
  }
  await loadProducts();
}

async function loadProducts() {
  const result = await request<Paged<ProductItem>>('/gateway/shop/products/List', {
    method: 'POST',
    auth: false,
    data: {
      customerId: session.profile?.customerId || '0',
      platformId: platform.platformId,
      merchantId: merchantId.value,
      categoryId: categoryId.value || '0',
      page: 1,
      pageSize: 30,
    },
  });
  products.value = result.items || [];
}

function selectCategory(id: string) {
  categoryId.value = id;
  loadProducts();
}

function openActivity(id: string) {
  uni.navigateTo({ url: `/pages/activity/index?id=${id}` });
}

function openReviews() {
  uni.navigateTo({ url: `/pages/evaluate/list?merchantId=${merchantId.value}&title=${encodeURIComponent(shop.value?.merchantName + '评价')}` });
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad((query) => {
  merchantId.value = String(query?.id || '');
  load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.shop-hero {
  display: flex;
  align-items: center;
  gap: 20rpx;
  padding: 28rpx 8rpx;
}

.shop-hero image,
.shop-hero__logo {
  width: 112rpx;
  height: 112rpx;
  flex: 0 0 112rpx;
  border-radius: 24rpx;
}

.shop-hero__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fff;
  color: $brand;
  font-size: 38rpx;
  font-weight: 600;
}

.shop-hero__body {
  display: flex;
  flex-direction: column;
  gap: 7rpx;
}

.shop-hero__name {
  font-size: 34rpx;
  font-weight: 600;
}

.shop-hero__score,
.shop-hero__desc {
  color: $muted;
  font-size: 22rpx;
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
  padding: 0 28rpx;
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

.chips {
  width: 100%;
  padding: 18rpx 0;
}

.chips__track {
  display: flex;
  gap: 12rpx;
}

.chips button {
  flex: 0 0 auto;
  height: 58rpx;
  padding: 0 22rpx;
  border: 0;
  border-radius: 999rpx;
  background: #fff;
  color: $muted;
  font-size: 22rpx;
}

.chips button.active {
  background: $selection-bg;
  color: $selection-text;
  font-weight: 500;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20rpx;
}

.activity {
  display: flex;
  gap: 16rpx;
  padding: 18rpx;
  margin-bottom: 16rpx;
}

.activity image {
  width: 200rpx;
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

.shop-reviews {
  padding: 0 24rpx 20rpx;
  margin-top: 24rpx;
}

.review {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  padding: 16rpx 0;
  border-top: 1rpx solid $line;
  font-size: 24rpx;
}

.review text:first-child {
  color: $muted;
  font-size: 21rpx;
}

.cart-float {
  position: fixed;
  right: 26rpx;
  bottom: calc(40rpx + env(safe-area-inset-bottom));
  z-index: 30;
  width: 88rpx;
  height: 88rpx;
  padding: 0;
  border: 0;
  border-radius: 999rpx;
  background: #171719;
  box-shadow: $shadow-float;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
}
</style>
