<template>
  <view class="payment page-shell">
    <AppHeader title="收银台" back />
    <view class="payment__panel card">
      <text class="payment__label">待支付金额</text>
      <text class="payment__amount">{{ money(amount) }}</text>
      <text class="payment__order">订单号 {{ orderNo }}</text>
      <view class="countdown">订单将在 {{ countdown }} 后自动关闭</view>
      <view class="method">
        <view class="method__icon">S</view>
        <view>
          <text>模拟支付</text>
          <text>当前为模拟支付，不产生真实扣款</text>
        </view>
        <text class="method__selected">✓</text>
      </view>
    </view>
    <button class="btn-primary payment__confirm" :disabled="paying" @click="confirm">确认支付</button>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const orderNo = ref('');
const amount = ref(0);
const remaining = ref(1800);
const paying = ref(false);
let timer: ReturnType<typeof setInterval> | null = null;

const countdown = computed(() => {
  const minutes = Math.floor(remaining.value / 60).toString().padStart(2, '0');
  const seconds = (remaining.value % 60).toString().padStart(2, '0');
  return `${minutes}:${seconds}`;
});

async function load() {
  session.restore();
  const payment = await request<any>('/gateway/payments/Create', {
    method: 'POST',
    data: { orderNo: orderNo.value },
  });
  amount.value = payment.amount || 0;
  timer = setInterval(() => {
    remaining.value = Math.max(0, remaining.value - 1);
  }, 1000);
}

async function confirm() {
  paying.value = true;
  try {
    await request<any>('/gateway/payments/Confirm', {
      method: 'POST',
      data: { orderNo: orderNo.value },
    });
    uni.showToast({ title: '支付成功', icon: 'success' });
    setTimeout(() => {
      uni.redirectTo({ url: `/pages/order/detail?orderNo=${orderNo.value}` });
    }, 350);
  } finally {
    paying.value = false;
  }
}

onLoad((query) => {
  orderNo.value = String(query?.orderNo || '');
  load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.payment__panel {
  display: flex;
  align-items: center;
  flex-direction: column;
  padding: 46rpx 28rpx 30rpx;
  margin-top: 30rpx;
}

.payment__label {
  color: $muted;
  font-size: 24rpx;
}

.payment__amount {
  margin-top: 10rpx;
  color: $text;
  font-size: 68rpx;
  font-weight: 600;
}

.payment__order {
  margin-top: 12rpx;
  color: $muted;
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 22rpx;
}

.countdown {
  margin: 24rpx 0;
  padding: 8rpx 18rpx;
  border-radius: 999rpx;
  background: #fff7e8;
  color: $warning;
  font-size: 22rpx;
}

.method {
  display: flex;
  align-items: center;
  width: 100%;
  gap: 16rpx;
  padding: 24rpx;
  border-top: 1rpx solid $line;
}

.method__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 70rpx;
  height: 70rpx;
  border-radius: 16rpx;
  background: #111;
  color: #fff;
  font-weight: 600;
}

.method > view:nth-child(2) {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 6rpx;
}

.method > view:nth-child(2) text:first-child {
  font-size: 27rpx;
  font-weight: 500;
}

.method > view:nth-child(2) text:last-child {
  color: $muted;
  font-size: 21rpx;
}

.method__selected {
  color: $success;
  font-size: 28rpx;
}

.payment__confirm {
  margin-top: 30rpx;
}
</style>
