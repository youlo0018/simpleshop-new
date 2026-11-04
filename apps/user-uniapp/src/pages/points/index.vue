<template>
  <view class="points-page page-shell">
    <AppHeader title="积分中心" back />
    <view class="points-hero">
      <text class="points-hero__label">可用积分</text>
      <text class="points-hero__value">{{ balance.available || 0 }}</text>
      <text class="points-hero__frozen">冻结 {{ balance.frozen || 0 }} 积分</text>
      <text v-if="balance.expiringPoints" class="points-hero__expire">
        {{ balance.expiringPoints }} 积分将在 {{ balance.expiringAt }} 前过期
      </text>
    </view>
    <view class="sign-card card">
      <view class="sign-card__head">
        <view>
          <text>连续签到 {{ balance.signInStreak || 0 }} 天</text>
          <text>下一次签到 +{{ balance.nextSignInReward || 0 }} 积分</text>
        </view>
        <button :disabled="balance.signedToday" @click="signIn">{{ balance.signedToday ? '今日已签到' : '签到' }}</button>
      </view>
      <view class="sign-card__days">
        <view v-for="(reward, index) in balance.signInRewards || []" :key="index" :class="{ active: index < (balance.signInStreak || 0) }">
          <text>D{{ index + 1 }}</text><text>+{{ reward }}</text>
        </view>
      </view>
    </view>
    <view class="rule card">
      <text>积分抵扣规则</text>
      <text>{{ balance.pointsPerYuan || 0 }} 积分抵扣 1 元，最多抵扣商品应付金额</text>
    </view>
    <view class="section-head"><text class="section-title">积分流水</text></view>
    <view class="records card">
      <view v-for="record in records" :key="record.id" class="record">
        <view>
          <text>{{ record.actionName }}</text>
          <text>{{ record.bizNo }} · {{ record.createdAt }}</text>
        </view>
        <view>
          <text :class="{ plus: record.quantity > 0, minus: record.quantity < 0 }">{{ record.quantity > 0 ? '+' : '' }}{{ record.quantity }}</text>
          <text>余额 {{ record.availableAfter }}</text>
        </view>
      </view>
      <EmptyState v-if="!records.length" title="暂无积分流水" icon="wallet" />
    </view>
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
const balance = ref<any>({});
const records = ref<any[]>([]);
const page = ref(1);
const loading = ref(false);

async function load(reset = true) {
  session.restore();
  if (!session.profile) return;
  if (reset) {
    page.value = 1;
    records.value = [];
  }
  loading.value = true;
  try {
    const [account, recordList] = await Promise.all([
      request<any>('/gateway/points/Balance', {
        params: { customerId: session.profile.customerId },
      }),
      request<any[]>('/gateway/points/Records', {
        params: { customerId: session.profile.customerId, page: page.value, pageSize: 20 },
      }),
    ]);
    balance.value = account;
    records.value = reset ? recordList : [...records.value, ...recordList];
  } finally {
    loading.value = false;
  }
}

async function signIn() {
  await request('/gateway/points/SignIn', {
    method: 'POST',
    params: { customerId: session.profile!.customerId },
  });
  await load();
}

onLoad(() => load());
onReachBottom(() => {
  if (loading.value) return;
  page.value += 1;
  load(false);
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.points-hero {
  display: flex;
  align-items: center;
  flex-direction: column;
  padding: 46rpx 28rpx;
  margin-top: 20rpx;
  border-radius: 24rpx;
  background: linear-gradient(135deg, #e9f2ff, #f6f9ff 55%, #eafaf6);
}

.points-hero__label {
  color: #5a6b80;
  font-size: 23rpx;
}

.points-hero__value {
  margin: 8rpx 0;
  color: #132b46;
  font-size: 74rpx;
  font-weight: 600;
}

.points-hero__frozen,
.points-hero__expire {
  color: #65758b;
  font-size: 22rpx;
}

.points-hero__expire {
  margin-top: 8rpx;
  color: $warning;
}

.sign-card,
.rule,
.records {
  padding: 24rpx;
  margin-top: 20rpx;
}

.sign-card__head {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  gap: 24rpx;
}

.sign-card__head > view {
  display: flex;
  flex-direction: column;
  gap: 7rpx;
  min-width: 0;
}

.sign-card__head > view text:first-child {
  font-size: 29rpx;
  font-weight: 600;
}

.sign-card__head > view text:last-child {
  color: $muted;
  font-size: 22rpx;
}

.sign-card__head button {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 68rpx;
  justify-self: end;
  padding: 0 28rpx;
  margin: 0;
  border: 0;
  border-radius: 999rpx;
  background: $brand;
  color: #fff;
  font-size: 24rpx;
  line-height: 1.2;
  white-space: nowrap;
}

.sign-card__head button[disabled] {
  background: #e9e9ec;
  color: $muted;
}

.sign-card__days {
  display: grid;
  grid-template-columns: repeat(7, 1fr);
  gap: 8rpx;
  margin-top: 24rpx;
}

.sign-card__days view {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 4rpx;
  padding: 12rpx 2rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  color: $muted;
  font-size: 18rpx;
}

.sign-card__days view.active {
  background: #eaf2ff;
  color: $brand;
}

.rule {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
}

.rule text:first-child {
  font-size: 27rpx;
  font-weight: 500;
}

.rule text:last-child {
  color: $muted;
  font-size: 22rpx;
}

.record {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20rpx 0;
  border-top: 1rpx solid $line;
}

.record:first-child {
  border-top: 0;
}

.record view {
  display: flex;
  flex-direction: column;
  gap: 5rpx;
  font-size: 24rpx;
}

.record view text:last-child {
  color: $muted;
  font-size: 20rpx;
}

.record view:last-child {
  align-items: flex-end;
}

.plus {
  color: $success;
  font-size: 28rpx;
}

.minus {
  color: $price;
  font-size: 28rpx;
}
</style>
