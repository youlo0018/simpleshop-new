<template>
  <view class="seckill page-shell">
    <AppHeader title="限时抢购" back />
    <view v-for="session in sessions" :key="session.sessionId" class="session card">
      <view class="session__head">
        <view>
          <text class="session__name">{{ session.sessionName }}</text>
          <text class="session__time">{{ shortTime(session.startTime) }} - {{ shortTime(session.endTime) }}</text>
        </view>
        <text class="session__status">{{ session.statusName }}</text>
      </view>
      <view class="session__items">
        <button v-for="item in session.items || []" :key="item.skuId" @click="buy(item)">
          <image :src="item.image" mode="aspectFill" />
          <text>{{ item.productName }}</text>
          <text class="price">{{ money(item.seckillPrice) }}</text>
        </button>
      </view>
    </view>
    <EmptyState v-if="!sessions.length" title="暂无秒杀场次" icon="fire" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { usePlatformStore } from '@/stores/platform';

const platform = usePlatformStore();
const sessions = ref<any[]>([]);

async function load() {
  await platform.initialize();
  sessions.value = await request<any[]>('/gateway/marketing/seckill/sessions/Public', {
    method: 'POST',
    auth: false,
    data: { platformId: platform.platformId },
    silent: true,
  }).catch(() => []);
}

function shortTime(value: string) {
  return value ? String(value).slice(5, 16).replace('T', ' ') : '';
}

function buy(item: any) {
  uni.navigateTo({ url: `/pages/product/detail?id=${item.spuId}` });
}

onLoad(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.session {
  padding: 22rpx;
  margin-top: 20rpx;
}

.session__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.session__head view {
  display: flex;
  flex-direction: column;
  gap: 6rpx;
}

.session__name {
  font-size: 29rpx;
  font-weight: 600;
}

.session__time {
  color: $muted;
  font-size: 21rpx;
}

.session__status {
  color: $success;
  font-size: 22rpx;
}

.session__items {
  display: flex;
  gap: 16rpx;
  overflow-x: auto;
  margin-top: 20rpx;
}

.session__items button {
  display: flex;
  width: 220rpx;
  flex: 0 0 220rpx;
  flex-direction: column;
  gap: 8rpx;
  padding: 0;
  border: 0;
  background: transparent;
  text-align: left;
}

.session__items image {
  width: 220rpx;
  height: 220rpx;
  border-radius: 16rpx;
  background: #f1f2f4;
}

.session__items text {
  overflow: hidden;
  font-size: 24rpx;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.session__items .price {
  color: $price;
  font-size: 30rpx;
  font-weight: 600;
}
</style>
