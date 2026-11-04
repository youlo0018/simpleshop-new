<template>
  <view class="coupon-center page-shell">
    <AppHeader title="领券中心" back />
    <view v-for="coupon in coupons" :key="coupon.activityId" class="coupon-card card">
      <view class="coupon-card__value">
        <text>{{ coupon.couponType === 2 ? `${coupon.discountRate}折` : money(coupon.discountAmount) }}</text>
        <text>{{ coupon.thresholdAmount > 0 ? `满 ${coupon.thresholdAmount} 可用` : '无门槛' }}</text>
      </view>
      <view class="coupon-card__body">
        <text class="coupon-card__name">{{ coupon.templateName }}</text>
        <text class="coupon-card__scope">{{ coupon.scopeText }}</text>
        <text class="coupon-card__valid">领取后 {{ coupon.validDays }} 天有效</text>
      </view>
      <button :disabled="coupon.isClaimed" @click="claim(coupon)">{{ coupon.isClaimed ? '已领取' : '领取' }}</button>
    </view>
    <EmptyState v-if="!loading && !coupons.length" title="暂无可领取的券" icon="gift" />
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
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const coupons = ref<any[]>([]);
const loading = ref(false);

async function load() {
  loading.value = true;
  try {
    await platform.initialize();
    session.restore();
    const result = await request<any>('/gateway/coupons/Available', {
      method: 'POST',
      auth: false,
      data: {
        platformId: platform.platformId,
        customerId: session.profile?.customerId || '0',
        page: 1,
        pageSize: 30,
      },
    });
    coupons.value = result.items || [];
  } finally {
    loading.value = false;
  }
}

async function claim(coupon: any) {
  if (!session.profile) {
    uni.navigateTo({ url: '/pages/login/index?redirect=/pages/coupon/center' });
    return;
  }
  await request('/gateway/coupons/Claim', {
    method: 'POST',
    data: { customerId: session.profile.customerId, activityId: coupon.activityId, quantity: 1 },
  });
  coupon.isClaimed = true;
  coupon.customerClaimedCount += 1;
  uni.showToast({ title: '领取成功', icon: 'success' });
}

onLoad(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.coupon-card {
  display: flex;
  align-items: center;
  gap: 20rpx;
  padding: 22rpx;
  margin-top: 20rpx;
}

.coupon-card__value {
  display: flex;
  align-items: center;
  flex: 0 0 170rpx;
  flex-direction: column;
  gap: 8rpx;
  min-height: 140rpx;
  justify-content: center;
  border-right: 1rpx dashed #e2e2e6;
}

.coupon-card__value text:first-child {
  color: $price;
  font-size: 36rpx;
  font-weight: 600;
}

.coupon-card__value text:last-child {
  color: $muted;
  font-size: 20rpx;
}

.coupon-card__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 7rpx;
  min-width: 0;
}

.coupon-card__name {
  font-size: 27rpx;
  font-weight: 500;
}

.coupon-card__scope,
.coupon-card__valid {
  color: $muted;
  font-size: 21rpx;
}

.coupon-card > button {
  height: 60rpx;
  padding: 0 20rpx;
  border: 0;
  border-radius: 999rpx;
  background: $brand;
  color: #fff;
  font-size: 22rpx;
}

.coupon-card > button[disabled] {
  background: #ececef;
  color: $muted;
}
</style>
