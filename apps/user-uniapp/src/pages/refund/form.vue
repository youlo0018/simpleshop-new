<template>
  <view class="refund-form page-shell">
    <AppHeader title="申请退款" back />
    <view v-if="order" class="refund-form__body">
      <view v-for="line in order.items" :key="line.orderItemId" class="line card">
        <view class="line__head">
          <button class="check" :class="{ 'check--on': selected[line.orderItemId] }" @click="toggle(line)">
            {{ selected[line.orderItemId] ? '✓' : '' }}
          </button>
          <image class="line__image" :src="line.image" mode="aspectFill" />
          <view class="line__body">
            <text>{{ line.productName }}</text>
            <text class="muted">{{ line.skuSpecText }}</text>
            <text>可退 {{ line.quantity - (line.refundedQuantity || 0) }} 件</text>
          </view>
        </view>
        <view v-if="selected[line.orderItemId]" class="line__form">
          <view><text>退件数</text><input v-model.number="quantities[line.orderItemId]" type="number" /></view>
          <view><text>退款金额</text><input v-model.number="amounts[line.orderItemId]" type="digit" /></view>
        </view>
      </view>
      <view class="reasons card">
        <text class="section-title">退款原因</text>
        <view class="reasons__chips">
          <button v-for="reason in reasons" :key="reason" :class="{ active: selectedReason === reason }" @click="selectedReason = reason">{{ reason }}</button>
        </view>
        <input v-model="reason" maxlength="200" placeholder="请补充说明（2-200 字）" />
      </view>
      <view class="uploader card">
        <text class="section-title">凭证图片（最多 9 张）</text>
        <view class="uploader__grid">
          <image v-for="(image, index) in images" :key="index" :src="image" mode="aspectFill" />
          <button v-if="images.length < 9" aria-label="添加图片" @click="chooseImages">
            <AppIcon type="plusempty" :size="24" color="#8a8a8f" />
          </button>
        </view>
      </view>
      <view class="tips card">
        部分退款不退运费；退款不退券；积分按比例回收；当前为模拟支付，审批通过后原路退回模拟账户。
      </view>
      <button class="btn-primary" :disabled="submitting" @click="submit">提交退款申请</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request, uploadImage } from '@/core/http';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const order = ref<any>(null);
const orderNo = ref('');
const selected = reactive<Record<string, boolean>>({});
const quantities = reactive<Record<string, number>>({});
const amounts = reactive<Record<string, number>>({});
const reasons = ['不喜欢 / 不想要', '质量问题', '发错货', '少件漏发', '其他'];
const selectedReason = ref('');
const reason = ref('');
const images = ref<string[]>([]);
const submitting = ref(false);

async function load() {
  session.restore();
  order.value = await request<any>('/gateway/orders/Detail', {
    params: { customerId: session.profile?.customerId, orderNo: orderNo.value },
  });
  for (const line of order.value.items || []) {
    const remainingQuantity = Math.max(0, Number(line.quantity || 0) - Number(line.refundedQuantity || 0));
    const refundableAmount = Number(line.refundableAmount > 0
      ? line.refundableAmount
      : line.payableAmount || 0);
    if (remainingQuantity > 0 && refundableAmount > 0) {
      selected[line.orderItemId] = true;
      quantities[line.orderItemId] = remainingQuantity;
      amounts[line.orderItemId] = Number(refundableAmount.toFixed(2));
    }
  }
}

function toggle(line: any) {
  selected[line.orderItemId] = !selected[line.orderItemId];
}

async function chooseImages() {
  const result = await new Promise<string[]>((resolve) => {
    uni.chooseImage({
      count: 9 - images.value.length,
      success: (value) => resolve(value.tempFilePaths as string[]),
      fail: () => resolve([]),
    });
  });
  for (const path of result) {
    try { images.value.push(await uploadImage(path)); } catch { /* 单张失败可重试 */ }
  }
}

async function submit() {
  const items = Object.keys(selected)
    .filter((id) => selected[id])
    .map((id) => ({
      orderItemId: id,
      quantity: Number(quantities[id] || 0),
      amount: Number(amounts[id] || 0),
    }));
  if (!items.length) {
    uni.showToast({ title: '请选择退款商品', icon: 'none' });
    return;
  }
  const finalReason = [selectedReason.value, reason.value.trim()].filter(Boolean).join('：');
  if (finalReason.length < 2) {
    uni.showToast({ title: '请选择或填写退款原因', icon: 'none' });
    return;
  }
  submitting.value = true;
  try {
    await request('/gateway/refunds/Apply', {
      method: 'POST',
      data: {
        customerId: session.profile!.customerId,
        orderId: order.value.orderId,
        orderNo: orderNo.value,
        items,
        reason: finalReason,
        images: images.value,
      },
    });
    uni.showToast({ title: '已提交', icon: 'success' });
    setTimeout(() => uni.redirectTo({ url: '/pages/refund/list' }), 350);
  } finally {
    submitting.value = false;
  }
}

onLoad((query) => {
  orderNo.value = String(query?.orderNo || '');
  load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.refund-form__body {
  display: flex;
  flex-direction: column;
  gap: 20rpx;
  padding-top: 20rpx;
}

.line,
.reasons,
.uploader,
.tips {
  padding: 22rpx;
}

.line__head {
  display: flex;
  align-items: center;
  gap: 16rpx;
}

.check {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 42rpx;
  height: 42rpx;
  flex: 0 0 42rpx;
  border: 2rpx solid #cfcfd4;
  border-radius: 999rpx;
  color: #fff;
}

.check--on {
  border-color: $brand;
  background: $brand;
}

.line__image {
  width: 136rpx;
  height: 136rpx;
  flex: 0 0 136rpx;
  border-radius: 16rpx;
  background: #f1f2f4;
}

.line__body {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  font-size: 24rpx;
}

.line__form {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16rpx;
  margin-top: 18rpx;
  padding-top: 18rpx;
  border-top: 1rpx solid $line;
}

.line__form view {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  color: $muted;
  font-size: 22rpx;
}

.line__form input,
.reasons input {
  height: 72rpx;
  padding: 0 16rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  color: $text;
  font-size: 24rpx;
}

.reasons__chips {
  display: flex;
  flex-wrap: wrap;
  gap: 12rpx;
  margin: 18rpx 0;
}

.reasons__chips button {
  height: 58rpx;
  padding: 0 20rpx;
  border: 1rpx solid $line;
  border-radius: 999rpx;
  background: #fff;
  font-size: 22rpx;
}

.reasons__chips button.active {
  border-color: $selection-border;
  background: $selection-bg;
  color: $selection-text;
  font-weight: 500;
}

.uploader__grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12rpx;
  margin-top: 18rpx;
}

.uploader__grid image,
.uploader__grid button {
  width: 100%;
  aspect-ratio: 1;
  border-radius: 12rpx;
}

.uploader__grid button {
  border: 1rpx dashed #c9c9ce;
  background: #fafafa;
  font-size: 40rpx;
}

.tips {
  color: $muted;
  font-size: 22rpx;
  line-height: 1.6;
}
</style>
