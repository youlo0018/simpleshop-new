<template>
  <view class="mine page-shell">
    <AppHeader title="我的评价" back />
    <view v-for="item in items" :key="item.evaluateId" class="evaluate card">
      <view class="evaluate__head">
        <text>{{ item.spuName }}</text>
        <text>{{ item.statusName || '已发布' }}</text>
      </view>
      <text class="evaluate__spec">{{ item.skuSpecs }}</text>
      <text class="evaluate__stars">{{ '★'.repeat(item.starScore) }}</text>
      <text class="evaluate__content">{{ item.content }}</text>
      <view v-for="append in item.appends || []" :key="append.appendId" class="evaluate__append">
        <text>追评</text>
        <text>{{ append.content }}</text>
      </view>
      <view class="evaluate__actions">
        <button v-if="canAppend(item)" @click="append(item)">追评</button>
        <button @click="openProduct(item.spuId)">查看商品</button>
      </view>
    </view>
    <EmptyState v-if="!items.length" title="还没有发表过评价" action="去评价" icon="chat" @action="goOrders" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onReachBottom } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import { request } from '@/core/http';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const items = ref<any[]>([]);
const page = ref(1);
const total = ref(0);

async function load(reset = true) {
  if (reset) {
    page.value = 1;
    items.value = [];
  }
  const result = await request<any>('/gateway/evaluates/My', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, page: page.value, pageSize: 10 },
  });
  total.value = result.total;
  items.value = reset ? result.items : [...items.value, ...result.items];
}

function canAppend(item: any) {
  return (item.appends?.length || 0) < 3;
}

function append(item: any) {
  uni.navigateTo({ url: `/pages/evaluate/form?append=1&evaluateId=${item.evaluateId}` });
}

function openProduct(id: string) {
  uni.navigateTo({ url: `/pages/product/detail?id=${id}` });
}

function goOrders() {
  uni.navigateTo({ url: '/pages/order/list' });
}

onLoad(() => {
  session.restore();
  load();
});
onReachBottom(() => {
  if (items.value.length >= total.value) return;
  page.value += 1;
  load(false);
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.evaluate {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  padding: 24rpx;
  margin-top: 20rpx;
}

.evaluate__head {
  display: flex;
  justify-content: space-between;
  gap: 20rpx;
  font-size: 27rpx;
  font-weight: 500;
}

.evaluate__head text:last-child {
  color: $muted;
  font-size: 21rpx;
  font-weight: 400;
}

.evaluate__spec {
  color: $muted;
  font-size: 21rpx;
}

.evaluate__stars {
  color: $warning;
}

.evaluate__content {
  padding: 12rpx 0;
  font-size: 24rpx;
  line-height: 1.5;
}

.evaluate__append {
  display: flex;
  flex-direction: column;
  gap: 6rpx;
  padding: 14rpx 16rpx;
  border-radius: 12rpx;
  background: $selection-bg;
  font-size: 23rpx;
  line-height: 1.5;
}

.evaluate__append text:first-child {
  color: $muted;
  font-size: 20rpx;
}

.evaluate__actions {
  display: flex;
  justify-content: flex-end;
  gap: 12rpx;
  padding-top: 14rpx;
  border-top: 1rpx solid $line;
}

.evaluate__actions button {
  height: 58rpx;
  padding: 0 20rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 21rpx;
}
</style>
