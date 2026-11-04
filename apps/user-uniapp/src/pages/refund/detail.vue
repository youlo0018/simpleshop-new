<template>
  <view class="refund-detail page-shell">
    <AppHeader title="退款详情" back />
    <view v-if="refund" class="refund-detail__body">
      <view class="status card">
        <text class="status__name">{{ refund.statusName }}</text>
        <text class="status__amount">{{ money(refund.amount) }}</text>
        <text class="status__no">{{ refund.refundNo }}</text>
      </view>
      <view class="info card">
        <view><text>订单号</text><text selectable>{{ refund.orderNo }}</text></view>
        <view><text>退款类型</text><text>{{ refund.refundTypeName }}</text></view>
        <view><text>申请时间</text><text>{{ refund.createdAt }}</text></view>
        <view v-if="refund.approvedAt"><text>完成时间</text><text>{{ refund.approvedAt }}</text></view>
        <view><text>申请原因</text><text>{{ refund.reason }}</text></view>
        <view v-if="refund.rejectReason"><text>拒绝原因</text><text>{{ refund.rejectReason }}</text></view>
      </view>
      <view class="items card">
        <text class="section-title">退款明细</text>
        <view v-for="item in refund.items" :key="item.orderItemId" class="item-row">
          <view><text>{{ item.productName }}</text><text>{{ item.skuSpecText }}</text></view>
          <text>{{ money(item.amount) }}</text>
        </view>
      </view>
      <view v-if="refund.images?.length" class="images card">
        <text class="section-title">凭证图片</text>
        <view class="images__grid">
          <image v-for="(image, index) in refund.images" :key="index" :src="image" mode="aspectFill" @click="preview(image)" />
        </view>
      </view>
      <view class="actions">
        <button class="btn-ghost" @click="openOrder">查看订单</button>
        <button v-if="refund.status === 10" class="danger" @click="cancel">撤销申请</button>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const refundId = ref('');
const refund = ref<any>(null);

async function load() {
  session.restore();
  refund.value = await request<any>('/gateway/refunds/MyDetail', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, refundId: refundId.value },
  });
}

function preview(current: string) {
  uni.previewImage({ current, urls: refund.value.images });
}

function openOrder() {
  uni.navigateTo({ url: `/pages/order/detail?orderNo=${refund.value.orderNo}` });
}

async function cancel() {
  await request('/gateway/refunds/CancelMy', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, refundId: refundId.value },
  });
  await load();
  uni.showToast({ title: '已撤销', icon: 'success' });
}

onLoad((query) => {
  refundId.value = String(query?.refundId || '');
  load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.refund-detail__body {
  display: flex;
  flex-direction: column;
  gap: 20rpx;
  padding-top: 20rpx;
}

.status {
  display: flex;
  align-items: center;
  flex-direction: column;
  padding: 32rpx;
}

.status__name {
  color: $warning;
  font-size: 36rpx;
  font-weight: 600;
}

.status__amount {
  margin-top: 12rpx;
  color: $price;
  font-size: 50rpx;
  font-weight: 600;
}

.status__no {
  margin-top: 10rpx;
  color: $muted;
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 21rpx;
}

.info,
.items,
.images {
  padding: 24rpx;
}

.info view {
  display: grid;
  grid-template-columns: 130rpx 1fr;
  gap: 18rpx;
  padding: 10rpx 0;
  color: $muted;
  font-size: 23rpx;
}

.info view text:last-child {
  color: $text;
  text-align: right;
}

.item-row {
  display: flex;
  justify-content: space-between;
  gap: 20rpx;
  padding: 16rpx 0;
  border-top: 1rpx solid $line;
}

.item-row view {
  display: flex;
  flex-direction: column;
  gap: 6rpx;
  font-size: 24rpx;
}

.item-row view text:last-child {
  color: $muted;
  font-size: 21rpx;
}

.images__grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10rpx;
  margin-top: 16rpx;
}

.images__grid image {
  width: 100%;
  aspect-ratio: 1;
  border-radius: 12rpx;
}

.actions {
  display: flex;
  gap: 16rpx;
}

.actions button {
  flex: 1;
}

.actions .danger {
  height: 72rpx;
  border: 1rpx solid $danger;
  border-radius: 999rpx;
  background: #fff;
  color: $danger;
  font-size: 25rpx;
}
</style>
