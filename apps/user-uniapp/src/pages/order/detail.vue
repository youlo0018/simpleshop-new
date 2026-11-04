<template>
  <view class="order-detail page-shell">
    <AppHeader title="订单详情" back />
    <view v-if="order" class="order-detail__body">
      <view class="status-card">
        <text class="status-card__name">{{ order.statusName }}</text>
        <text class="status-card__tip">{{ statusTip }}</text>
      </view>

      <view v-if="order.pickupCode" class="pickup card">
        <text class="pickup__title">取货码</text>
        <image v-if="qrDataUrl" class="pickup__qr" :src="qrDataUrl" mode="aspectFit" />
        <view v-else class="pickup__qr pickup__qr--placeholder">出示给店员核销</view>
        <text class="pickup__code" @click="copy(order.pickupCode)">{{ order.pickupCode }}</text>
        <text class="pickup__tip">到店后请出示二维码或取货码</text>
      </view>

      <view v-if="order.receiverName" class="address card">
        <text class="address__name">{{ order.receiverName }} {{ order.receiverPhone }}</text>
        <text v-if="order.receiverAddress" class="address__detail">{{ order.receiverAddress }}</text>
      </view>

      <view v-if="order.shipRemark" class="virtual card">
        <text class="virtual__label">发货内容</text>
        <text class="virtual__value">{{ order.shipRemark }}</text>
        <button class="btn-ghost" @click="copy(order.shipRemark)">复制</button>
      </view>

      <view v-if="latestRefund" class="refund-status card" @click="openRefunds">
        <view class="refund-status__head">
          <text>退款 / 售后</text>
          <text>{{ latestRefund.statusName }}</text>
        </view>
        <view class="refund-status__meta">
          <text>{{ latestRefund.refundNo }}</text>
          <text>{{ money(latestRefund.amount) }}</text>
        </view>
      </view>

      <view class="order-lines card">
        <button
          v-for="line in order.items"
          :key="line.orderItemId"
          class="order-line"
          @click="openProduct(line.spuId)"
        >
          <image class="order-line__image" :src="line.image" mode="aspectFill" />
          <view class="order-line__body">
            <text class="order-line__name">{{ line.productName }}</text>
            <text class="order-line__spec">{{ line.skuSpecText }}</text>
            <view class="order-line__bottom">
              <text>{{ money(line.price) }}</text>
              <text>×{{ line.quantity }}</text>
            </view>
            <text v-if="line.refundedQuantity" class="order-line__refund">已退 {{ line.refundedQuantity }} 件</text>
          </view>
        </button>
      </view>

      <view class="amount-card card">
        <view><text>商品总额</text><text>{{ money(grossGoodsTotal) }}</text></view>
        <view v-if="order.activityDiscount"><text>活动优惠</text><text>-{{ money(order.activityDiscount) }}</text></view>
        <view v-if="order.couponDiscount"><text>优惠券</text><text>-{{ money(order.couponDiscount) }}</text></view>
        <view v-if="order.freight"><text>运费</text><text>{{ money(order.freight) }}</text></view>
        <view v-if="order.pointsDeduction"><text>积分抵扣</text><text>-{{ money(order.pointsDeduction) }}</text></view>
        <view class="amount-card__total"><text>实付</text><text>{{ money(order.payableAmount) }}</text></view>
        <view v-if="order.refundedAmount"><text>已退款</text><text>{{ money(order.refundedAmount) }}</text></view>
      </view>

      <view class="meta card">
        <view><text>订单号</text><text selectable>{{ order.orderNo }}</text></view>
        <view><text>下单时间</text><text>{{ dateTime(order.createdAt) }}</text></view>
        <view v-if="order.remark"><text>备注</text><text>{{ order.remark }}</text></view>
      </view>
    </view>

    <view v-if="order" class="bottom-actions safe-bottom">
      <button v-if="order.canCancel" @click="cancel">取消订单</button>
      <button v-if="order.canPay" class="primary" @click="pay">去支付</button>
      <button v-if="order.canConfirmReceipt" @click="confirmReceipt">确认收货</button>
      <button v-if="pendingEvaluation" class="primary" @click="evaluate">去评价</button>
      <button v-if="order.canRefund" @click="refund">申请退款</button>
      <button @click="buyAgain">再次购买</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onLoad, onShow } from '@dcloudio/uni-app';
import QRCode from 'qrcode';
import AppHeader from '@/components/AppHeader.vue';
import { request } from '@/core/http';
import { dateTime, money } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const order = ref<any>(null);
const qrDataUrl = ref('');
const orderNo = ref('');
const latestRefund = ref<any>(null);
const evaluableItems = ref<any[]>([]);
const grossGoodsTotal = computed(() => Number(order.value?.goodsTotal || 0)
  + Number(order.value?.activityDiscount || 0)
  + Number(order.value?.couponDiscount || 0));
const pendingEvaluation = computed(() => evaluableItems.value.find((item) => !item.evaluated) || null);
const statusTip = computed(() => ({
  10: '请尽快完成支付，超时将自动关闭',
  20: '商家正在准备你的商品',
  30: '商品已发出，请注意查收',
  40: '商品已备好，请凭取货码到店取货',
  50: '订单已完成，感谢你的购买',
  60: '退款已处理',
  90: '订单已关闭',
  91: '订单已超时关闭',
}[order.value?.status as number] || ''));

async function load() {
  session.restore();
  if (!session.profile) {
    uni.redirectTo({ url: '/pages/login/index' });
    return;
  }
  const [orderDetail, refundPage, evaluable] = await Promise.all([
    request<any>('/gateway/orders/Detail', {
      params: { customerId: session.profile.customerId, orderNo: orderNo.value },
    }),
    request<any>('/gateway/refunds/My', {
      method: 'POST',
      data: { customerId: session.profile.customerId, page: 1, pageSize: 50 },
      silent: true,
    }).catch(() => ({ items: [] })),
    request<any[]>('/gateway/evaluates/Evaluable', {
      method: 'POST',
      data: { customerId: session.profile.customerId, orderNo: orderNo.value },
      silent: true,
    }).catch(() => []),
  ]);
  order.value = orderDetail;
  evaluableItems.value = evaluable || [];
  latestRefund.value = (refundPage.items || [])
    .find((item: any) => item.orderNo === orderNo.value) || null;
  if (order.value.pickupCode) {
    // #ifdef H5
    qrDataUrl.value = await QRCode.toDataURL(order.value.pickupCode, {
      width: 220,
      margin: 1,
      color: { dark: '#111111', light: '#ffffff' },
    });
    // #endif
  }
}

async function cancel() {
  await request('/gateway/orders/Cancel', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, orderNo: orderNo.value },
  });
  load();
}

async function confirmReceipt() {
  await request('/gateway/orders/ConfirmReceipt', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, orderNo: orderNo.value },
  });
  load();
}

function pay() {
  uni.navigateTo({ url: `/pages/payment/index?orderNo=${orderNo.value}` });
}

function refund() {
  uni.navigateTo({ url: `/pages/refund/form?orderNo=${orderNo.value}` });
}

function evaluate() {
  if (!pendingEvaluation.value) return;
  uni.navigateTo({
    url: `/pages/evaluate/form?orderNo=${orderNo.value}&spuId=${pendingEvaluation.value.spuId}`,
  });
}

async function buyAgain() {
  for (const line of order.value.items || []) {
    await request('/gateway/carts/Add', {
      method: 'POST',
      data: {
        customerId: session.profile!.customerId,
        skuId: line.skuId,
        quantity: line.quantity,
      },
    }).catch(() => undefined);
  }
  uni.navigateTo({ url: '/pages/cart/index' });
}

function openProduct(id: string) {
  uni.navigateTo({ url: `/pages/product/detail?id=${id}` });
}

function openRefunds() {
  uni.navigateTo({ url: '/pages/refund/list' });
}

function copy(value: string) {
  uni.setClipboardData({ data: value });
}

onLoad((query) => {
  orderNo.value = String(query?.orderNo || '');
  load();
});

onShow(() => {
  if (orderNo.value && order.value) load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.order-detail {
  padding-bottom: calc(130rpx + env(safe-area-inset-bottom));
}

.order-detail__body {
  display: flex;
  flex-direction: column;
  gap: 20rpx;
  padding-top: 20rpx;
}

.status-card {
  padding: 34rpx 28rpx;
  border-radius: 24rpx;
  background: linear-gradient(135deg, #e9f2ff, #f8fbff);
}

.status-card__name,
.status-card__tip {
  display: block;
}

.status-card__name {
  color: #163c68;
  font-size: 40rpx;
  font-weight: 600;
}

.status-card__tip {
  margin-top: 8rpx;
  color: #58708b;
  font-size: 23rpx;
}

.address,
.virtual,
.order-lines,
.amount-card,
.meta,
.pickup {
  padding: 24rpx;
}

.pickup {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 12rpx;
}

.pickup__title {
  font-size: 30rpx;
  font-weight: 600;
}

.pickup__qr {
  width: 300rpx;
  height: 300rpx;
}

.pickup__qr--placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 16rpx;
  background: #f3f4f6;
  color: $muted;
  font-size: 23rpx;
}

.pickup__code {
  color: #28282b;
  font-family: ui-monospace, SFMono-Regular, monospace;
  font-size: 26rpx;
}

.pickup__tip {
  color: $muted;
  font-size: 22rpx;
}

.address__name,
.address__detail {
  display: block;
}

.address__name {
  font-size: 28rpx;
  font-weight: 600;
}

.address__detail {
  margin-top: 10rpx;
  color: $muted;
  font-size: 24rpx;
  line-height: 1.5;
}

.virtual__label,
.virtual__value {
  display: block;
}

.refund-status {
  padding: 22rpx;
}

.refund-status__head,
.refund-status__meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.refund-status__head {
  font-size: 27rpx;
  font-weight: 600;
}

.refund-status__head text:last-child {
  color: $warning;
  font-size: 23rpx;
  font-weight: 500;
}

.refund-status__meta {
  margin-top: 10rpx;
  color: $muted;
  font-size: 22rpx;
}

.virtual__label {
  color: $muted;
  font-size: 23rpx;
}

.virtual__value {
  margin: 12rpx 0 18rpx;
  font-size: 28rpx;
  font-weight: 500;
}

.order-line {
  display: flex;
  width: 100%;
  gap: 18rpx;
  padding: 16rpx 0;
  border: 0;
  border-top: 1rpx solid $line;
  background: transparent;
  text-align: left;
}

.order-line:first-child {
  border-top: 0;
}

.order-line__image {
  width: 160rpx;
  height: 160rpx;
  flex: 0 0 160rpx;
  border-radius: 16rpx;
  background: #f1f2f4;
}

.order-line__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 8rpx;
}

.order-line__name {
  font-size: 27rpx;
}

.order-line__spec,
.order-line__refund {
  color: $muted;
  font-size: 22rpx;
}

.order-line__bottom {
  display: flex;
  justify-content: space-between;
  margin-top: auto;
  font-size: 24rpx;
}

.amount-card view,
.meta view {
  display: flex;
  justify-content: space-between;
  gap: 24rpx;
  min-height: 58rpx;
  color: $muted;
  font-size: 24rpx;
}

.amount-card view text:last-child,
.meta view text:last-child {
  color: $text;
  text-align: right;
}

.amount-card__total {
  margin-top: 8rpx;
  padding-top: 14rpx;
  border-top: 1rpx solid $line;
  font-size: 30rpx !important;
  font-weight: 600;
}

.amount-card__total text:last-child {
  color: $price !important;
}

.bottom-actions {
  position: fixed;
  right: 0;
  bottom: 0;
  left: 0;
  z-index: 30;
  display: flex;
  justify-content: flex-end;
  gap: 12rpx;
  padding: 14rpx 24rpx;
  border-top: 1rpx solid $line;
  background: #fff;
}

.bottom-actions button {
  height: 70rpx;
  padding: 0 22rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 23rpx;
}

.bottom-actions button.primary {
  border-color: $brand;
  background: $brand;
  color: #fff;
}
</style>
