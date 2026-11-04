<template>
  <view class="checkout page-shell">
    <AppHeader title="确认订单" back />
    <view v-if="preview" class="checkout__body">
      <button v-if="hasExpress" class="address card" @click="chooseAddress">
        <template v-if="address">
          <text class="address__name">{{ address.consigneeName }} {{ address.consigneePhone }}</text>
          <text class="address__detail">{{ address.regionPath }} {{ address.detailAddress }}</text>
        </template>
        <template v-else>
          <text class="address__name">请选择收货地址</text>
          <text class="address__detail">实物快递需要完整收货信息</text>
        </template>
        <AppIcon class="address__arrow" type="right" :size="16" color="#b8b8be" />
      </button>
      <view v-else-if="hasPickup" class="address card">
        <text class="address__name">到店自提</text>
        <input v-model="receiverName" class="address__input" placeholder="取货人姓名" />
        <input v-model="receiverPhone" class="address__input" placeholder="取货人手机号" />
      </view>

      <view class="line-card card">
        <view v-for="line in preview.lines" :key="line.skuId" class="line-row">
          <view class="line-row__body">
            <text class="line-row__name">{{ line.productName }}</text>
            <text class="line-row__spec">{{ line.skuSpecText }} · ×{{ line.quantity }}</text>
          </view>
          <text>{{ money(line.payableAmount) }}</text>
        </view>
      </view>

      <view class="options card">
        <button @click="couponOpen = !couponOpen">
          <view class="option-label"><AppIcon type="gift" :size="18" color="#e87935" /><text>优惠券</text></view>
          <view class="option-value">
            <text>{{ selectedCoupon ? `-${money(selectedCoupon.discountAmount)}` : '不使用' }}</text>
            <AppIcon type="right" :size="14" color="#b8b8be" />
          </view>
        </button>
        <view v-if="couponOpen" class="coupon-options">
          <button :class="{ active: !couponId }" @click="selectCoupon(0)">不使用优惠券</button>
          <button
            v-for="coupon in preview.couponOptions"
            :key="coupon.couponId"
            :class="{ active: couponId === coupon.couponId }"
            @click="selectCoupon(coupon.couponId)"
          >{{ coupon.couponTypeName }} -{{ money(coupon.discountAmount) }}</button>
        </view>
        <view class="points">
          <view class="points__label">
            <AppIcon type="wallet" :size="18" color="#00a998" />
            <view><text>使用积分</text><text>最多可用 {{ preview.maxPointsToUse }} 积分</text></view>
          </view>
          <switch :checked="usePoints" color="#0071e3" @change="togglePoints" />
        </view>
        <input
          v-if="usePoints"
          v-model="pointsToUse"
          type="number"
          class="points__input"
          :placeholder="`最多可用 ${preview.maxPointsToUse} 积分`"
          @blur="refreshPreview"
        />
        <input v-model="remark" class="remark" maxlength="512" placeholder="订单备注（选填，最多 512 字）" />
      </view>

      <view class="amount card">
        <view><text>商品总额</text><text>{{ money(grossGoodsTotal) }}</text></view>
        <view v-if="preview.activityDiscount"><text>活动优惠</text><text>-{{ money(preview.activityDiscount) }}</text></view>
        <view v-if="preview.couponDiscount"><text>券优惠</text><text>-{{ money(preview.couponDiscount) }}</text></view>
        <view v-if="preview.freight"><text>运费</text><text>{{ money(preview.freight) }}</text></view>
        <view v-if="preview.pointsDeduction"><text>积分抵扣</text><text>-{{ money(preview.pointsDeduction) }}</text></view>
        <view class="amount__total"><text>实付</text><text>{{ money(preview.payableAmount) }}</text></view>
      </view>
    </view>

    <view v-if="preview" class="submit-bar safe-bottom">
      <view>
        <text>实付</text>
        <text class="submit-bar__amount">{{ money(preview.payableAmount) }}</text>
      </view>
      <button :disabled="submitting" @click="submit">提交订单</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onLoad, onShow } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const preview = ref<any>(null);
const address = ref<any>(null);
const receiverName = ref('');
const receiverPhone = ref('');
const couponId = ref(0);
const couponOpen = ref(false);
const usePoints = ref(true);
const pointsToUse = ref(0);
const remark = ref('');
const submitting = ref(false);
const skuId = ref('');
const spuId = ref('');
const quantity = ref(1);
const merchantId = ref('0');
const cartItemIds = ref('');
const checkoutLines = ref<any[]>([]);
const idempotencyKey = `MP-${Date.now()}-${Math.random().toString(36).slice(2)}`;
const hasExpress = computed(() => preview.value?.lines?.some((line: any) => line.deliveryType === 1));
const hasPickup = computed(() => preview.value?.lines?.some((line: any) => line.deliveryType === 3));
const grossGoodsTotal = computed(() => Number(preview.value?.goodsTotal || 0)
  + Number(preview.value?.activityDiscount || 0)
  + Number(preview.value?.couponDiscount || 0));
const selectedCoupon = computed(() => preview.value?.couponOptions?.find(
  (coupon: any) => coupon.couponId === couponId.value,
));

async function initialize() {
  if (!platform.ready) await platform.initialize();
  session.restore();
  if (!session.profile) {
    uni.redirectTo({ url: '/pages/login/index' });
    return;
  }
  receiverName.value = session.profile.nickName || session.profile.customerName;
  if (cartItemIds.value) {
    const cart = await request<any[]>('/gateway/carts/List', {
      params: { customerId: session.profile.customerId },
    });
    const selectedIds = new Set(cartItemIds.value.split(',').filter(Boolean));
    checkoutLines.value = cart
      .filter((item) => selectedIds.has(String(item.id)))
      .map((item) => ({
        spuId: String(item.spuId || item.productId),
        skuId: String(item.skuId),
        quantity: Number(item.quantity),
      }));
    merchantId.value = String(cart.find((item) => selectedIds.has(String(item.id)))?.merchantId || '0');
  } else {
    checkoutLines.value = [{
      spuId: spuId.value,
      skuId: skuId.value,
      quantity: quantity.value,
    }];
  }
  const addresses = await request<any>('/gateway/customers/addresses/List', {
    method: 'POST',
    data: { customerId: session.profile.customerId, page: 1, pageSize: 100 },
    silent: true,
  }).catch(() => ({ items: [] }));
  applyAddress((addresses.items || []).find((item: any) => item.isDefault) || addresses.items?.[0] || null);
  await refreshPreview();
}

function applyAddress(value: any) {
  address.value = value || null;
  if (!value) return;
  receiverName.value = value.consigneeName || value.receiverName || receiverName.value;
  receiverPhone.value = value.consigneePhone || value.receiverPhone || receiverPhone.value;
}

async function refreshPreview() {
  const result = await request<any>('/gateway/orders/Preview', {
    method: 'POST',
    data: {
      customerId: session.profile!.customerId,
      platformId: platform.platformId,
      merchantId: merchantId.value,
      lines: checkoutLines.value,
      couponId: couponId.value,
      pointsToUse: usePoints.value ? Number(pointsToUse.value || 0) : 0,
    },
  });
  preview.value = result;
  if (usePoints.value && !pointsToUse.value) {
    pointsToUse.value = result.maxPointsToUse || 0;
    await refreshPreview();
  }
}

async function selectCoupon(id: number) {
  couponId.value = id;
  couponOpen.value = false;
  await refreshPreview();
}

function togglePoints(event: any) {
  usePoints.value = Boolean(event.detail.value);
  if (usePoints.value && !pointsToUse.value) pointsToUse.value = preview.value.maxPointsToUse || 0;
  refreshPreview();
}

function chooseAddress() {
  uni.navigateTo({ url: '/pages/address/list?select=1' });
}

async function submit() {
  if (hasExpress.value && !address.value) {
    uni.showToast({ title: '请选择收货地址', icon: 'none' });
    return;
  }
  if (hasPickup.value && (!receiverName.value || !receiverPhone.value)) {
    uni.showToast({ title: '请填写取货人和手机号', icon: 'none' });
    return;
  }
  submitting.value = true;
  try {
    const created = await request<any>('/gateway/orders/Create', {
      method: 'POST',
      data: {
        customerId: session.profile!.customerId,
        platformId: platform.platformId,
        merchantId: merchantId.value,
        idempotencyKey,
        receiverName: receiverName.value,
        receiverPhone: receiverPhone.value,
        receiverAddress: address.value
          ? `${address.value.regionPath || ''}${address.value.detailAddress || ''}`
          : '',
        lines: preview.value.lines.map((line: any) => ({
          spuId: line.spuId,
          skuId: line.skuId,
          quantity: line.quantity,
          unitPrice: line.unitPrice,
          productName: line.productName,
          skuSpecText: line.skuSpecText,
          deliveryType: line.deliveryType,
        })),
        couponId: couponId.value,
        pointsToUse: usePoints.value ? Number(pointsToUse.value || 0) : 0,
        freight: preview.value.freight,
        remark: remark.value,
      },
    });
    if (Number(created.payableAmount) > 0) {
      uni.redirectTo({ url: `/pages/payment/index?orderNo=${created.orderNo}` });
    } else {
      uni.redirectTo({ url: `/pages/order/detail?orderNo=${created.orderNo}` });
    }
  } finally {
    submitting.value = false;
  }
}

onLoad((query) => {
  skuId.value = String(query?.skuId || '');
  spuId.value = String(query?.spuId || '');
  quantity.value = Number(query?.quantity || 1);
  merchantId.value = String(query?.merchantId || '0');
  cartItemIds.value = String(query?.cartItemIds || '');
  initialize();
});

onShow(() => {
  const selectedAddress = uni.getStorageSync('simpleshop:selected-address');
  if (!selectedAddress) return;
  uni.removeStorageSync('simpleshop:selected-address');
  applyAddress(selectedAddress);
  if (preview.value) refreshPreview();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.checkout {
  padding-bottom: calc(130rpx + env(safe-area-inset-bottom));
}

.checkout__body {
  display: flex;
  flex-direction: column;
  gap: 20rpx;
  padding-top: 20rpx;
}

.address {
  position: relative;
  display: flex;
  align-items: flex-start;
  flex-direction: column;
  width: 100%;
  gap: 8rpx;
  padding: 26rpx;
  border: 0;
  text-align: left;
}

.address__name {
  font-size: 29rpx;
  font-weight: 600;
}

.address__detail {
  color: $muted;
  font-size: 23rpx;
  line-height: 1.5;
}

.address__arrow {
  position: absolute;
  top: 40rpx;
  right: 24rpx;
}

.address__input {
  width: 100%;
  height: 76rpx;
  padding: 0 18rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  font-size: 24rpx;
}

.line-card,
.options,
.amount {
  padding: 22rpx;
}

.line-row {
  display: flex;
  justify-content: space-between;
  gap: 20rpx;
  padding: 16rpx 0;
  border-top: 1rpx solid $line;
  font-size: 24rpx;
}

.line-row:first-child {
  border-top: 0;
}

.line-row__body {
  display: flex;
  flex-direction: column;
  gap: 6rpx;
}

.line-row__name {
  font-size: 27rpx;
}

.line-row__spec {
  color: $muted;
  font-size: 22rpx;
}

.options > button:first-child {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  min-height: 76rpx;
  padding: 0;
  border: 0;
  background: transparent;
  font-size: 25rpx;
}

.options > button:first-child text:last-child {
  color: $muted;
}

.option-label,
.option-value,
.points__label {
  display: flex;
  align-items: center;
}

.option-label {
  gap: 10rpx;
}

.option-value {
  gap: 2rpx;
  color: $muted;
}

.coupon-options {
  display: flex;
  flex-wrap: wrap;
  gap: 12rpx;
  padding: 12rpx 0 20rpx;
}

.coupon-options button {
  height: 58rpx;
  padding: 0 18rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 21rpx;
}

.coupon-options button.active {
  border-color: $selection-border;
  background: $selection-bg;
  color: $selection-text;
  font-weight: 500;
}

.points {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 82rpx;
  border-top: 1rpx solid $line;
}

.points view {
  display: flex;
  flex-direction: column;
  gap: 6rpx;
  font-size: 25rpx;
}

.points__label {
  gap: 12rpx;
}

.points__label > view {
  gap: 6rpx;
}

.points view text:last-child {
  color: $muted;
  font-size: 21rpx;
}

.points__input,
.remark {
  width: 100%;
  height: 76rpx;
  padding: 0 18rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  font-size: 24rpx;
}

.remark {
  margin-top: 18rpx;
}

.amount view {
  display: flex;
  justify-content: space-between;
  min-height: 56rpx;
  color: $muted;
  font-size: 24rpx;
}

.amount view text:last-child {
  color: $text;
}

.amount__total {
  margin-top: 8rpx;
  padding-top: 14rpx;
  border-top: 1rpx solid $line;
  font-size: 30rpx !important;
  font-weight: 600;
}

.amount__total text:last-child {
  color: $price !important;
}

.submit-bar {
  position: fixed;
  right: 0;
  bottom: 0;
  left: 0;
  z-index: 40;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14rpx 24rpx;
  border-top: 1rpx solid $line;
  background: #fff;
}

.submit-bar view {
  display: flex;
  flex-direction: column;
  color: $muted;
  font-size: 21rpx;
}

.submit-bar__amount {
  color: $price;
  font-size: 36rpx;
  font-weight: 600;
}

.submit-bar button {
  height: 82rpx;
  padding: 0 40rpx;
  border: 0;
  border-radius: 999rpx;
  background: $brand;
  color: #fff;
  font-size: 27rpx;
}
</style>
