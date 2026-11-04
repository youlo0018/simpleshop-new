<template>
  <view class="favorites page-shell">
    <AppHeader title="我的收藏" back />
    <text class="favorites__count">已收藏 {{ items.length }}/20</text>
    <view class="product-grid">
      <view v-for="item in items" :key="item.spuId" class="favorite-card card">
        <button class="favorite-card__remove" aria-label="取消收藏" @click="remove(item)">
          <AppIcon type="heart-filled" :size="19" color="#e94a65" />
        </button>
        <button class="favorite-card__body" @click="open(item)">
          <view class="favorite-card__media">
            <image v-if="item.mainImage" :src="item.mainImage" mode="aspectFill" />
            <view v-else class="favorite-card__empty">商品</view>
            <text v-if="!item.available" class="favorite-card__invalid">已失效</text>
          </view>
          <text class="favorite-card__name">{{ item.spuName || '商品已下架' }}</text>
          <view class="favorite-card__price-row">
            <text>{{ money(item.price) }}</text>
            <text v-if="item.originalPrice > item.price">{{ money(item.originalPrice) }}</text>
          </view>
        </button>
      </view>
    </view>
    <EmptyState v-if="!items.length" title="还没有收藏商品" action="去逛逛" icon="heart" @action="goHome" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const items = ref<any[]>([]);

async function load() {
  session.restore();
  const result = await request<any>('/gateway/customers/favorites/List', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, page: 1, pageSize: 20 },
  });
  items.value = result.items || [];
}

async function remove(item: any) {
  const confirmed = await confirm('取消收藏', `确定不再收藏「${item.spuName}」吗？`);
  if (!confirmed) return;
  await request('/gateway/customers/favorites/Remove', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, spuId: item.spuId },
  });
  items.value = items.value.filter((row) => row.spuId !== item.spuId);
}

function open(item: any) {
  if (!item.available) {
    uni.showToast({ title: '商品已失效', icon: 'none' });
    return;
  }
  uni.navigateTo({ url: `/pages/product/detail?id=${item.spuId}` });
}

function goHome() {
  uni.reLaunch({ url: '/pages/index/index' });
}

function confirm(title: string, content: string) {
  return new Promise<boolean>((resolve) => {
    uni.showModal({ title, content, success: (result) => resolve(result.confirm), fail: () => resolve(false) });
  });
}

onLoad(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.favorites__count {
  display: block;
  padding: 20rpx 4rpx;
  color: $muted;
  font-size: 23rpx;
}

.product-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 20rpx;
}

.favorite-card {
  position: relative;
  overflow: hidden;
}

.favorite-card__remove {
  position: absolute;
  top: 10rpx;
  right: 10rpx;
  z-index: 2;
  width: 60rpx;
  height: 60rpx;
  padding: 0;
  border: 0;
  border-radius: 999rpx;
  background: rgba(255, 255, 255, 0.88);
  display: flex;
  align-items: center;
  justify-content: center;
}

.favorite-card__body {
  display: block;
  width: 100%;
  padding: 0;
  border: 0;
  background: transparent;
  text-align: left;
}

.favorite-card__media {
  position: relative;
  width: 100%;
  aspect-ratio: 1;
  background: #f1f2f4;
}

.favorite-card__media image {
  width: 100%;
  height: 100%;
}

.favorite-card__empty {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: $tertiary;
}

.favorite-card__invalid {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.38);
  color: #fff;
  font-size: 26rpx;
}

.favorite-card__name {
  display: -webkit-box;
  overflow: hidden;
  padding: 18rpx 18rpx 0;
  font-size: 27rpx;
  line-height: 1.35;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.favorite-card__price-row {
  display: flex;
  align-items: baseline;
  gap: 8rpx;
  padding: 10rpx 18rpx 18rpx;
}

.favorite-card__price-row text:first-child {
  color: $price;
  font-size: 32rpx;
  font-weight: 600;
}

.favorite-card__price-row text:last-child {
  color: $tertiary;
  font-size: 21rpx;
  text-decoration: line-through;
}
</style>
