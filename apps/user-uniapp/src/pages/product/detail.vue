<template>
  <view class="product-detail">
    <AppHeader back title="" edge-actions>
      <template #right>
        <button class="header-action" aria-label="收藏" @click="favorite">
          <AppIcon :type="favorited ? 'heart-filled' : 'heart'" :size="22" :color="favorited ? '#e94a65' : '#1a1a1a'" />
        </button>
      </template>
    </AppHeader>
    <view v-if="detail" class="product-detail__content">
      <swiper class="gallery" circular indicator-dots>
        <swiper-item v-for="(image, index) in images" :key="index">
          <view v-if="failedImages[index]" class="gallery__fallback">
            <AppIcon type="image" :size="42" color="#c3c3c8" />
            <text>{{ detail.spuName }}</text>
          </view>
          <image
            v-else
            class="gallery__image"
            :src="image"
            mode="aspectFill"
            @click="preview(image)"
            @error="failedImages[index] = true"
          />
        </swiper-item>
      </swiper>
      <view class="summary">
        <view class="summary__price-row">
          <text class="summary__price">{{ money(currentPrice) }}</text>
          <text v-if="detail.skus?.[0]?.originalPrice > currentPrice" class="summary__original">
            {{ money(detail.skus[0].originalPrice) }}
          </text>
          <text class="summary__sales">已售 {{ detail.sales || 0 }}</text>
        </view>
        <text class="summary__title">{{ detail.spuName }}</text>
        <text v-if="detail.subTitle" class="summary__subtitle">{{ detail.subTitle }}</text>
        <view class="summary__badges">
          <text v-if="detail.merchantId === '0'">自营</text>
          <text>{{ deliveryLabel(detail.deliveryType) }}</text>
          <text v-if="serviceGuarantee">{{ serviceGuarantee }}</text>
        </view>
      </view>

      <button
        v-if="shopInfo && String(detail.merchantId) !== '0'"
        class="shop-entry card"
        @click="openShop"
      >
        <image v-if="shopInfo.logo" class="shop-entry__logo" :src="shopInfo.logo" mode="aspectFill" />
        <view v-else class="shop-entry__logo shop-entry__logo--empty">
          <AppIcon type="shop-filled" :size="20" color="#0071e3" />
        </view>
        <view class="shop-entry__body">
          <text class="shop-entry__name">{{ shopInfo.merchantName }}</text>
          <text class="shop-entry__desc">{{ shopInfo.description || '进入店铺查看更多商品' }}</text>
        </view>
        <view class="shop-entry__action">
          <text>进店</text>
          <AppIcon type="right" :size="14" color="#8a8a8f" />
        </view>
      </button>

      <button class="select-row card" @click="skuOpen = true">
        <text>选择规格</text>
        <view class="select-row__value">
          <text>{{ selectedSku?.skuSpecText || '请选择' }}</text>
          <AppIcon type="right" :size="14" color="#b8b8be" />
        </view>
      </button>
      <view class="service-card card">
        <view><text>配送</text><text>{{ deliveryNote || deliveryLabel(detail.deliveryType) }}</text></view>
        <view><text>服务</text><text>{{ serviceGuarantee || '正品保障 · 售后无忧' }}</text></view>
      </view>

      <view class="reviews card">
        <view class="section-head">
          <text class="section-title">商品评价</text>
          <button class="section-more section-more--icon" @click="openReviews">
            <text>全部评价</text>
            <AppIcon type="right" :size="13" color="#8a8a8f" />
          </button>
        </view>
        <view class="reviews__score">
          <text class="reviews__number">{{ Number(detail.evaluationScore || 0).toFixed(1) }}</text>
          <text class="muted">共 {{ detail.evaluationCount || 0 }} 条</text>
        </view>
        <view v-for="item in reviews" :key="item.evaluateId || item.id" class="review-row">
          <text class="review-row__name">{{ item.customerName || '匿名用户' }} {{ item.skuSpecText }}</text>
          <text class="review-row__content">{{ item.content }}</text>
        </view>
      </view>

      <view class="detail-images">
        <image
          v-for="(image, index) in detail.detailImages || []"
          :key="index"
          class="detail-images__image"
          :src="image"
          mode="widthFix"
          lazy-load
        />
      </view>
      <view class="bottom-space" />
    </view>
    <view v-if="detail" class="buy-bar safe-bottom">
      <button class="buy-bar__icon" aria-label="购物车" @click="go('/pages/cart/index')">
        <AppIcon type="cart-filled" :size="22" color="#1a1a1a" />
      </button>
      <button class="buy-bar__cart" @click="addToCart">
        <AppIcon type="cart-filled" :size="17" color="#ffffff" />
        <text>加入购物车</text>
      </button>
      <button class="buy-bar__buy" @click="buyNow">
        <AppIcon type="wallet" :size="17" color="#ffffff" />
        <text>立即购买</text>
      </button>
    </view>

    <view v-if="skuOpen" class="sku-overlay" @click.self="skuOpen = false">
      <view class="sku-sheet safe-bottom">
        <view class="sku-sheet__head">
          <image class="sku-sheet__image" :src="selectedSku?.image || detail?.mainImage" mode="aspectFill" />
          <view>
            <text class="sku-sheet__price">{{ money(currentPrice) }}</text>
            <text class="sku-sheet__selected">已选：{{ selectedSku?.skuSpecText || '请选择规格' }}</text>
          </view>
          <button aria-label="关闭规格选择" @click="skuOpen = false">
            <AppIcon type="closeempty" :size="20" color="#8a8a8f" />
          </button>
        </view>
        <view v-for="spec in detail?.specs || []" :key="spec.specId" class="sku-group">
          <text class="sku-group__title">{{ spec.specName }}</text>
          <view class="sku-group__values">
            <button
              v-for="value in spec.values"
              :key="value.specValueId"
              :class="{ active: selectedValueIds.includes(value.specValueId) }"
              @click="selectValue(spec, value)"
            >{{ value.valueName }}</button>
          </view>
        </view>
        <view class="sku-quantity">
          <text>数量</text>
          <view class="stepper">
            <button aria-label="减少数量" @click="quantity = Math.max(1, quantity - 1)"><AppIcon type="minus" :size="14" color="#66666b" /></button>
            <text>{{ quantity }}</text>
            <button aria-label="增加数量" @click="quantity = Math.min(99, quantity + 1)"><AppIcon type="plus" :size="14" color="#66666b" /></button>
          </view>
        </view>
        <button class="btn-primary" @click="skuOpen = false">确认</button>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const detail = ref<any>(null);
const reviews = ref<any[]>([]);
const serviceGuarantee = ref('');
const deliveryNote = ref('');
const shopInfo = ref<any>(null);
const selectedValueIds = ref<number[]>([]);
const selectedSku = ref<any>(null);
const quantity = ref(1);
const skuOpen = ref(false);
const favorited = ref(false);
const productId = ref('');
const failedImages = ref<Record<number, boolean>>({});

const images = computed(() => {
  const rows = detail.value?.images || [];
  return rows.length ? rows : [detail.value?.mainImage].filter(Boolean);
});
const currentPrice = computed(() => Number(selectedSku.value?.finalPrice ?? detail.value?.skus?.[0]?.finalPrice ?? 0));

async function load() {
  if (!platform.ready) await platform.initialize();
  session.restore();
  detail.value = await request<any>('/gateway/shop/products/Detail', {
    method: 'POST',
    auth: false,
    data: {
      customerId: session.profile?.customerId || '0',
      productId: productId.value,
      platformId: platform.platformId,
    },
  });
  selectedSku.value = detail.value.skus?.[0] || null;
  selectedValueIds.value = [...(selectedSku.value?.specValueIds || [])];
  const shop = await request<any>(`/gateway/merchants/Shop/${detail.value.merchantId}`, {
    auth: false,
    silent: true,
  }).catch(() => null);
  shopInfo.value = shop;
  serviceGuarantee.value = shop?.serviceGuarantee || '';
  deliveryNote.value = shop?.deliveryNote || '';
  const page = await request<any>('/gateway/evaluates/List', {
    method: 'POST',
    auth: false,
    data: { spuId: productId.value, page: 1, pageSize: 2 },
    silent: true,
  }).catch(() => ({ items: [] }));
  reviews.value = page.items || [];
}

function selectValue(spec: any, value: any) {
  const otherSpecIds = spec.values.map((item: any) => item.specValueId);
  selectedValueIds.value = [
    ...selectedValueIds.value.filter((id) => !otherSpecIds.includes(id)),
    value.specValueId,
  ];
  const match = (detail.value?.skus || []).find((sku: any) =>
    selectedValueIds.value.every((id) => sku.specValueIds.includes(id)));
  if (match) selectedSku.value = match;
}

async function addToCart() {
  if (!requireLogin()) return;
  await request('/gateway/carts/Add', {
    method: 'POST',
    data: {
      customerId: session.profile!.customerId,
      skuId: selectedSku.value?.skuId,
      quantity: quantity.value,
    },
  });
  uni.showToast({ title: '已加入购物车', icon: 'success' });
}

function buyNow() {
  if (!requireLogin()) return;
  uni.navigateTo({
    url: `/pages/checkout/index?skuId=${selectedSku.value?.skuId}&quantity=${quantity.value}`,
  });
}

function requireLogin() {
  if (session.loggedIn) return true;
  uni.navigateTo({ url: `/pages/login/index?redirect=${encodeURIComponent(`/pages/product/detail?id=${productId.value}`)}` });
  return false;
}

async function favorite() {
  if (!requireLogin()) return;
  const method = favorited.value ? 'Remove' : 'Add';
  await request(`/gateway/customers/favorites/${method}`, {
    method: 'POST',
    data: { customerId: session.profile!.customerId, spuId: productId.value },
  });
  favorited.value = !favorited.value;
}

function preview(image: string) {
  uni.previewImage({ current: image, urls: images.value });
}

function openReviews() {
  uni.navigateTo({ url: `/pages/evaluate/list?spuId=${productId.value}&title=${encodeURIComponent(detail.value.spuName)}` });
}

function openShop() {
  if (!detail.value?.merchantId || String(detail.value.merchantId) === '0') return;
  uni.navigateTo({ url: `/pages/shop/store?id=${detail.value.merchantId}` });
}

function go(url: string) {
  uni.navigateTo({ url });
}

function deliveryLabel(type: number) {
  return type === 2 ? '虚拟商品' : type === 3 ? '到店自提' : '快递配送';
}

onLoad((query) => {
  productId.value = String(query?.id || '');
  load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.product-detail {
  --page-gutter: 0rpx;
  --page-gutter-negative: 0rpx;
  min-height: 100vh;
  padding-bottom: calc(128rpx + env(safe-area-inset-bottom));
}

.header-action {
  width: 88rpx;
  height: 88rpx;
  border: 0;
  background: transparent;
  display: flex;
  align-items: center;
  justify-content: center;
}

.product-detail__content {
  padding: 0 24rpx;
}

.gallery {
  overflow: hidden;
  width: calc(100% + 48rpx);
  height: 750rpx;
  margin-left: -24rpx;
  background: #f0f1f3;
}

.gallery__image {
  width: 100%;
  height: 100%;
}

.gallery__fallback {
  display: flex;
  align-items: center;
  flex-direction: column;
  justify-content: center;
  gap: 18rpx;
  width: 100%;
  height: 100%;
  background:
    linear-gradient(135deg, rgba(255, 255, 255, 0.64), transparent),
    #f1f2f4;
  color: $muted;
  font-size: 24rpx;
}

.summary {
  padding: 28rpx 4rpx 24rpx;
}

.summary__price-row {
  display: flex;
  align-items: baseline;
  gap: 12rpx;
}

.summary__price {
  color: $price;
  font-size: 48rpx;
  font-weight: 600;
}

.summary__original {
  color: $tertiary;
  font-size: 24rpx;
  text-decoration: line-through;
}

.summary__sales {
  margin-left: auto;
  color: $muted;
  font-size: 22rpx;
}

.summary__title {
  display: block;
  margin-top: 18rpx;
  font-size: 34rpx;
  font-weight: 600;
  line-height: 1.35;
}

.summary__subtitle {
  display: block;
  margin-top: 8rpx;
  color: $muted;
  font-size: 24rpx;
}

.summary__badges {
  display: flex;
  flex-wrap: wrap;
  gap: 10rpx;
  margin-top: 18rpx;
}

.summary__badges text {
  padding: 6rpx 12rpx;
  border-radius: 8rpx;
  background: #f2f4f7;
  color: #5e6470;
  font-size: 20rpx;
}

.select-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  min-height: 92rpx;
  padding: 0 24rpx;
  border: 0;
  font-size: 26rpx;
}

.shop-entry {
  display: flex;
  align-items: center;
  width: 100%;
  gap: 16rpx;
  padding: 18rpx 22rpx;
  margin-bottom: 20rpx;
  border: 1rpx solid $line;
  background: #fff;
  text-align: left;
}

.shop-entry__logo {
  width: 72rpx;
  height: 72rpx;
  flex: 0 0 72rpx;
  border-radius: 18rpx;
  background: #f2f4f7;
}

.shop-entry__logo--empty {
  display: flex;
  align-items: center;
  justify-content: center;
}

.shop-entry__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 5rpx;
  min-width: 0;
}

.shop-entry__name {
  overflow: hidden;
  font-size: 27rpx;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.shop-entry__desc {
  overflow: hidden;
  color: $muted;
  font-size: 21rpx;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.shop-entry__action {
  display: flex;
  align-items: center;
  gap: 2rpx;
  color: $brand;
  font-size: 22rpx;
}

.select-row__value {
  display: flex;
  align-items: center;
  gap: 2rpx;
  color: $muted;
}

.service-card {
  display: flex;
  flex-direction: column;
  gap: 18rpx;
  padding: 24rpx;
  margin-top: 20rpx;
}

.service-card view {
  display: grid;
  grid-template-columns: 90rpx 1fr;
  font-size: 24rpx;
}

.service-card view text:first-child {
  color: $muted;
}

.reviews {
  padding: 8rpx 24rpx 20rpx;
  margin-top: 20rpx;
}

.reviews__score {
  display: flex;
  align-items: baseline;
  gap: 12rpx;
  padding-bottom: 12rpx;
}

.reviews__number {
  font-size: 48rpx;
  font-weight: 600;
}

.review-row {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  padding: 18rpx 0;
  border-top: 1rpx solid $line;
}

.review-row__name {
  color: $muted;
  font-size: 22rpx;
}

.review-row__content {
  font-size: 25rpx;
}

.detail-images {
  margin-top: 20rpx;
}

.detail-images__image {
  display: block;
  width: 100%;
}

.buy-bar {
  position: fixed;
  right: 0;
  bottom: 0;
  left: 0;
  z-index: 50;
  display: flex;
  align-items: center;
  gap: 16rpx;
  padding: 14rpx 24rpx;
  border-top: 1rpx solid $line;
  background: #fff;
  box-shadow: $shadow-float;
}

.buy-bar button {
  height: 84rpx;
  border: 0;
  border-radius: 999rpx;
}

.buy-bar__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 88rpx;
  flex: 0 0 88rpx;
  background: #f2f3f5;
}

.buy-bar__cart,
.buy-bar__buy {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8rpx;
  flex: 1;
  color: #fff;
  font-size: 27rpx;
}

.buy-bar__cart {
  background: linear-gradient(90deg, #ffc53d, #ff9f0a);
}

.buy-bar__buy {
  background: linear-gradient(90deg, #ff9f0a, #ff3b30);
}

.sku-overlay {
  position: fixed;
  inset: 0;
  z-index: 100;
  display: flex;
  align-items: flex-end;
  background: rgba(0, 0, 0, 0.4);
}

.sku-sheet {
  width: 100%;
  max-height: 78vh;
  overflow: auto;
  padding: 24rpx;
  border-radius: 24rpx 24rpx 0 0;
  background: #fff;
}

.sku-sheet__head {
  display: flex;
  align-items: center;
  gap: 20rpx;
}

.sku-sheet__image {
  width: 150rpx;
  height: 150rpx;
  border-radius: 16rpx;
}

.sku-sheet__head > view {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 10rpx;
}

.sku-sheet__head button {
  align-self: flex-start;
  border: 0;
  background: transparent;
  color: $muted;
  display: flex;
  align-items: center;
  justify-content: center;
}

.sku-sheet__price {
  color: $price;
  font-size: 38rpx;
  font-weight: 600;
}

.sku-sheet__selected {
  color: $muted;
  font-size: 23rpx;
}

.sku-group {
  margin-top: 26rpx;
}

.sku-group__title {
  font-size: 26rpx;
  font-weight: 500;
}

.sku-group__values {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-start;
  gap: 12rpx;
  width: 100%;
  margin-top: 14rpx;
}

.sku-group__values button {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 62rpx;
  padding: 0 24rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 23rpx;
  margin: 0;
  line-height: 1.2;
  text-align: center;
}

.sku-group__values button.active {
  border-color: $selection-border;
  background: $selection-bg;
  color: $selection-text;
  font-weight: 500;
}

.sku-quantity {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin: 28rpx 0;
}

.stepper {
  display: flex;
  overflow: hidden;
  border-radius: 12rpx;
  background: #f2f3f5;
}

.stepper button,
.stepper text {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 64rpx;
  height: 58rpx;
}

.stepper button {
  border: 0;
  background: transparent;
}
</style>
