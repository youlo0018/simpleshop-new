<template>
  <button class="product-card" :class="{ 'product-card--row': row }" @click="open">
    <view class="product-card__media">
      <image
        v-if="product.mainImage && !imageFailed"
        class="product-card__image"
        :src="product.mainImage"
        mode="aspectFill"
        lazy-load
        @error="imageFailed = true"
      />
      <view v-else class="product-card__placeholder">
        <AppIcon type="image" :size="28" color="#c2c2c7" />
      </view>
      <text v-if="showSelf && String(product.merchantId || '0') === '0'" class="product-card__self">自营</text>
    </view>
    <view class="product-card__body">
      <text class="product-card__name">{{ product.spuName }}</text>
      <text v-if="showSubTitle && product.subTitle" class="product-card__sub">{{ product.subTitle }}</text>
      <view class="product-card__meta">
        <text class="product-card__price">{{ money(product.finalPrice) }}</text>
        <text v-if="product.originalPrice > product.finalPrice" class="product-card__original">
          {{ money(product.originalPrice) }}
        </text>
      </view>
      <text v-if="product.discountSourceName" class="product-card__source">{{ product.discountSourceName }}</text>
      <text class="product-card__sales">已售 {{ product.sales || 0 }}</text>
    </view>
  </button>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue';
import type { ProductItem } from '@/core/types';
import { money } from '@/core/format';
import AppIcon from './AppIcon.vue';

const props = withDefaults(defineProps<{
  product: ProductItem;
  row?: boolean;
  showSelf?: boolean;
  showSubTitle?: boolean;
}>(), {
  row: false,
  showSelf: true,
  showSubTitle: false,
});
const imageFailed = ref(false);
watch(() => props.product.mainImage, () => {
  imageFailed.value = false;
});

function open() {
  uni.navigateTo({ url: `/pages/product/detail?id=${props.product.productId}` });
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.product-card {
  overflow: hidden;
  width: 100%;
  padding: 0;
  border: 0;
  border-radius: 20rpx;
  background: #fff;
  box-shadow: $shadow;
  color: $text;
  text-align: left;
}

.product-card__media {
  position: relative;
  width: 100%;
  aspect-ratio: 1;
  background: #f1f2f4;
}

.product-card__image,
.product-card__placeholder {
  width: 100%;
  height: 100%;
}

.product-card__placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  color: $tertiary;
  font-size: 26rpx;
}

.product-card__self {
  position: absolute;
  top: 12rpx;
  left: 12rpx;
  padding: 4rpx 10rpx;
  border-radius: 8rpx;
  background: #eef4ff;
  color: $self;
  font-size: 20rpx;
}

.product-card__body {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  padding: 20rpx;
}

.product-card__name {
  display: -webkit-box;
  overflow: hidden;
  font-size: 28rpx;
  line-height: 1.4;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.product-card__sub {
  overflow: hidden;
  color: $muted;
  font-size: 22rpx;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.product-card__meta {
  display: flex;
  align-items: baseline;
  gap: 10rpx;
}

.product-card__price {
  color: $price;
  font-size: 36rpx;
  font-weight: 600;
}

.product-card__original {
  color: $tertiary;
  font-size: 22rpx;
  text-decoration: line-through;
}

.product-card__source {
  align-self: flex-start;
  padding: 4rpx 10rpx;
  border-radius: 8rpx;
  background: #fff2f0;
  color: $price;
  font-size: 20rpx;
}

.product-card__sales {
  color: $muted;
  font-size: 22rpx;
}

.product-card--row {
  display: flex;
  gap: 20rpx;
  padding: 20rpx;
}

.product-card--row .product-card__media {
  width: 200rpx;
  height: 200rpx;
  flex: 0 0 200rpx;
  border-radius: 16rpx;
}

.product-card--row .product-card__body {
  flex: 1;
  padding: 0;
}
</style>
