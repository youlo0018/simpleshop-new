<template>
  <view class="orders page-shell">
    <AppHeader title="我的订单" back>
      <template #right>
        <button class="refund-link" @click="go('/pages/refund/list')">
          <AppIcon type="chatboxes" :size="15" color="#8a8a8f" />
          <text>退款 / 售后</text>
        </button>
      </template>
    </AppHeader>
    <scroll-view scroll-x class="tabs" :show-scrollbar="false">
      <view class="tabs__track">
        <button
          v-for="tab in tabs"
          :key="tab.value"
          :class="{ active: status === tab.value }"
          @click="changeStatus(tab.value)"
        >{{ tab.label }}</button>
      </view>
    </scroll-view>
    <view v-for="order in orders" :key="order.orderNo" class="order-card card">
      <button class="order-card__head" @click="open(order)">
        <text>{{ order.merchantName || '平台自营' }}</text>
        <text class="order-card__status">{{ order.statusName }}</text>
      </button>
      <button class="order-card__body" @click="open(order)">
        <view class="order-card__images">
          <image v-for="(image, index) in order.images || []" :key="index" :src="image" mode="aspectFill" />
          <view v-if="!(order.images || []).length" class="order-card__image-empty">商品</view>
          <text>共 {{ order.itemCount }} 件</text>
        </view>
        <view class="order-card__summary">
          <text>{{ order.firstProductName }}</text>
          <text>实付 {{ money(order.payableAmount) }}</text>
        </view>
      </button>
      <view class="order-card__actions">
        <button v-if="order.canCancel" @click="cancel(order)"><AppIcon type="closeempty" :size="13" color="#66666b" />取消订单</button>
        <button v-if="order.canPay" class="primary" @click="pay(order)"><AppIcon type="wallet" :size="13" color="#ffffff" />去支付</button>
        <button v-if="order.canConfirmReceipt" @click="confirm(order)"><AppIcon type="checkmarkempty" :size="13" color="#66666b" />确认收货</button>
        <button v-if="order.canRefund" @click="refund(order)"><AppIcon type="undo" :size="13" color="#66666b" />申请退款</button>
        <button @click="open(order)"><AppIcon type="right" :size="13" color="#66666b" />查看详情</button>
      </view>
    </view>
    <EmptyState v-if="!loading && !orders.length" title="暂无订单" action="去逛逛" icon="list" @action="goHome" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onPullDownRefresh, onReachBottom } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import type { Paged } from '@/core/types';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const orders = ref<any[]>([]);
const status = ref(0);
const page = ref(1);
const total = ref(0);
const loading = ref(false);
const tabs = [
  { label: '全部', value: 0 },
  { label: '待支付', value: 10 },
  { label: '待发货', value: 20 },
  { label: '待收货', value: 30 },
  { label: '待取货', value: 40 },
  { label: '已完成', value: 50 },
  { label: '已取消', value: 90 },
];

async function load(resetPage = true) {
  session.restore();
  if (!session.profile) {
    uni.redirectTo({ url: '/pages/login/index' });
    return;
  }
  if (resetPage) {
    page.value = 1;
    orders.value = [];
  }
  loading.value = true;
  try {
    const result = await request<Paged<any>>('/gateway/orders/List', {
      params: {
        customerId: session.profile.customerId,
        status: status.value,
        page: page.value,
        pageSize: 10,
      },
    });
    total.value = result.total;
    orders.value = resetPage ? result.items : [...orders.value, ...result.items];
  } finally {
    loading.value = false;
    uni.stopPullDownRefresh();
  }
}

function changeStatus(value: number) {
  status.value = value;
  load();
}

function open(order: any) {
  uni.navigateTo({ url: `/pages/order/detail?orderNo=${order.orderNo}` });
}

async function cancel(order: any) {
  await request('/gateway/orders/Cancel', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, orderNo: order.orderNo },
  });
  load();
}

function pay(order: any) {
  uni.navigateTo({ url: `/pages/payment/index?orderNo=${order.orderNo}` });
}

async function confirm(order: any) {
  await request('/gateway/orders/ConfirmReceipt', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, orderNo: order.orderNo },
  });
  load();
}

function refund(order: any) {
  uni.navigateTo({ url: `/pages/refund/form?orderNo=${order.orderNo}` });
}

function go(path: string) {
  uni.navigateTo({ url: path });
}

function goHome() {
  uni.reLaunch({ url: '/pages/index/index' });
}

onLoad(() => load());
onPullDownRefresh(() => load());
onReachBottom(() => {
  if (loading.value || orders.value.length >= total.value) return;
  page.value += 1;
  load(false);
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.refund-link {
  display: flex;
  align-items: center;
  gap: 4rpx;
  padding: 20rpx 0;
  border: 0;
  background: transparent;
  color: $muted;
  font-size: 23rpx;
}

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
  flex: 0 0 auto;
  height: 60rpx;
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

.order-card {
  padding: 20rpx;
  margin-top: 20rpx;
}

.order-card__head,
.order-card__body {
  display: flex;
  width: 100%;
  padding: 0;
  border: 0;
  background: transparent;
  text-align: left;
}

.order-card__head {
  align-items: center;
  justify-content: space-between;
  padding-bottom: 18rpx;
  font-size: 26rpx;
  font-weight: 500;
}

.order-card__status {
  color: $brand;
  font-size: 24rpx;
}

.order-card__body {
  gap: 20rpx;
  padding: 18rpx 0;
  border-top: 1rpx solid $line;
}

.order-card__images {
  display: flex;
  align-items: center;
  gap: 8rpx;
}

.order-card__images image,
.order-card__image-empty {
  width: 112rpx;
  height: 112rpx;
  border-radius: 12rpx;
  background: #f1f2f4;
}

.order-card__image-empty {
  display: flex;
  align-items: center;
  justify-content: center;
  color: $tertiary;
  font-size: 21rpx;
}

.order-card__images text {
  color: $muted;
  font-size: 21rpx;
}

.order-card__summary {
  display: flex;
  align-items: flex-end;
  flex: 1;
  flex-direction: column;
  justify-content: space-between;
  gap: 8rpx;
  color: $muted;
  font-size: 22rpx;
}

.order-card__summary text:first-child {
  color: $text;
  font-size: 26rpx;
}

.order-card__actions {
  display: flex;
  justify-content: flex-end;
  gap: 12rpx;
  padding-top: 16rpx;
  border-top: 1rpx solid $line;
}

.order-card__actions button {
  display: flex;
  align-items: center;
  gap: 4rpx;
  height: 62rpx;
  padding: 0 22rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 22rpx;
}

.order-card__actions button.primary {
  border-color: $brand;
  background: $brand;
  color: #fff;
}
</style>
