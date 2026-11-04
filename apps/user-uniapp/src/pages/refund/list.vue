<template>
  <view class="refunds page-shell">
    <AppHeader title="退款 / 售后" back />
    <scroll-view scroll-x class="tabs" :show-scrollbar="false">
      <view class="tabs__track">
        <button v-for="tab in tabs" :key="tab.value" :class="{ active: status === tab.value }" @click="change(tab.value)">{{ tab.label }}</button>
      </view>
    </scroll-view>
    <view v-for="item in refunds" :key="item.refundId" class="refund-card card" @click="open(item)">
      <view class="refund-card__head">
        <text>退款单 {{ item.refundNo }}</text>
        <text :class="`status status--${item.status}`">{{ item.statusName }}</text>
      </view>
      <text class="refund-card__order">订单 {{ item.orderNo }}</text>
      <view class="refund-card__amount"><text>申请金额</text><text>{{ money(item.amount) }}</text></view>
      <view class="refund-card__actions">
        <button v-if="item.status === 10" @click.stop="cancel(item)">撤销申请</button>
        <button @click.stop="openOrder(item.orderNo)">查看订单</button>
      </view>
    </view>
    <EmptyState v-if="!loading && !refunds.length" title="暂无退款记录" icon="chatboxes" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onReachBottom } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const refunds = ref<any[]>([]);
const status = ref(0);
const page = ref(1);
const total = ref(0);
const loading = ref(false);
const tabs = [
  { label: '全部', value: 0 },
  { label: '待审批', value: 10 },
  { label: '已退款', value: 20 },
  { label: '已拒绝', value: 90 },
  { label: '已撤销', value: 95 },
];

async function load(reset = true) {
  session.restore();
  if (!session.profile) {
    uni.redirectTo({ url: '/pages/login/index' });
    return;
  }
  if (reset) {
    page.value = 1;
    refunds.value = [];
  }
  loading.value = true;
  try {
    const result = await request<any>('/gateway/refunds/My', {
      method: 'POST',
      data: { customerId: session.profile.customerId, status: status.value, page: page.value, pageSize: 10 },
    });
    total.value = result.total;
    refunds.value = reset ? result.items : [...refunds.value, ...result.items];
  } finally {
    loading.value = false;
  }
}

function change(value: number) {
  status.value = value;
  load();
}

async function cancel(item: any) {
  await request('/gateway/refunds/CancelMy', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, refundId: item.refundId },
  });
  load();
}

function openOrder(orderNo: string) {
  uni.navigateTo({ url: `/pages/order/detail?orderNo=${orderNo}` });
}

function open(item: any) {
  uni.navigateTo({ url: `/pages/refund/detail?refundId=${item.refundId}` });
}

onLoad(() => load());
onReachBottom(() => {
  if (loading.value || refunds.value.length >= total.value) return;
  page.value += 1;
  load(false);
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.tabs {
  width: calc(100% + 48rpx);
  margin-left: -24rpx;
  border-bottom: 1rpx solid $line;
}

.tabs__track {
  display: flex;
  gap: 8rpx;
  padding: 14rpx 24rpx;
}

.tabs button {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 60rpx;
  flex: 0 0 auto;
  padding: 0 24rpx;
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

.refund-card {
  padding: 22rpx;
  margin-top: 20rpx;
}

.refund-card__head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  font-size: 26rpx;
  font-weight: 500;
}

.status {
  font-size: 23rpx;
}

.status--10 { color: $warning; }
.status--20 { color: $success; }
.status--90,
.status--95 { color: $muted; }

.refund-card__order {
  display: block;
  margin-top: 8rpx;
  color: $muted;
  font-size: 22rpx;
}

.refund-card__amount {
  display: flex;
  justify-content: space-between;
  padding: 18rpx 0;
  margin-top: 14rpx;
  border-top: 1rpx solid $line;
  font-size: 24rpx;
}

.refund-card__amount text:last-child {
  color: $price;
  font-weight: 600;
}

.refund-card__actions {
  display: flex;
  justify-content: flex-end;
  gap: 12rpx;
}

.refund-card__actions button {
  height: 62rpx;
  padding: 0 22rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 22rpx;
}
</style>
