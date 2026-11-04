<template>
  <view class="mine page-shell">
    <AppHeader title="我的券包" back>
      <template #right><button class="header-link" @click="go('/pages/coupon/center')">领券中心</button></template>
    </AppHeader>
    <view class="tabs">
      <button v-for="tab in tabs" :key="tab.value" :class="{ active: status === tab.value }" @click="change(tab.value)">{{ tab.label }}</button>
    </view>
    <view v-for="coupon in coupons" :key="coupon.couponId" class="coupon-row card">
      <view class="coupon-row__value">{{ coupon.couponType === 2 ? `${coupon.discountRate}折` : money(coupon.discountAmount) }}</view>
      <view class="coupon-row__body">
        <text>{{ coupon.templateName }}</text>
        <text>{{ coupon.thresholdAmount > 0 ? `满 ${coupon.thresholdAmount} 可用` : '无门槛' }}</text>
        <text>{{ coupon.scopeText }}</text>
        <text>有效期至 {{ coupon.expireAt }}</text>
      </view>
      <button v-if="coupon.status === 1 && status === 1" @click="use(coupon)">去使用</button>
    </view>
    <EmptyState v-if="!loading && !coupons.length" title="这个分类还没有券" icon="gift" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const coupons = ref<any[]>([]);
const status = ref(1);
const loading = ref(false);
const tabs = [
  { label: '未使用', value: 1 },
  { label: '已核销', value: 3 },
  { label: '已过期', value: 4 },
];

async function load() {
  loading.value = true;
  try {
    session.restore();
    const result = await request<any>('/gateway/coupons/My', {
      method: 'POST',
      data: { customerId: session.profile?.customerId, status: status.value, page: 1, pageSize: 50 },
    });
    coupons.value = result.items || [];
  } finally {
    loading.value = false;
  }
}

function change(value: number) {
  status.value = value;
  load();
}

function use(coupon: any) {
  uni.navigateTo({ url: `/pages/product/list?couponId=${coupon.couponId}&title=${encodeURIComponent('券可用商品')}` });
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.header-link {
  padding: 20rpx 0;
  border: 0;
  background: transparent;
  color: $brand;
  font-size: 23rpx;
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
  padding: 0 26rpx;
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

.coupon-row {
  display: flex;
  align-items: center;
  gap: 18rpx;
  padding: 22rpx;
  margin-top: 20rpx;
}

.coupon-row__value {
  flex: 0 0 150rpx;
  color: $price;
  font-size: 34rpx;
  font-weight: 600;
  text-align: center;
}

.coupon-row__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 6rpx;
  color: $muted;
  font-size: 21rpx;
}

.coupon-row__body text:first-child {
  color: $text;
  font-size: 26rpx;
}

.coupon-row button {
  height: 60rpx;
  padding: 0 18rpx;
  border: 0;
  border-radius: 999rpx;
  background: $brand;
  color: #fff;
  font-size: 21rpx;
}
</style>
