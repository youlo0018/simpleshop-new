<template>
  <view class="cart page-shell">
    <AppHeader title="购物车">
      <template #right>
        <button class="edit" @click="editing = !editing">
          <AppIcon :type="editing ? 'checkmarkempty' : 'compose'" :size="15" color="#66666b" />
          <text>{{ editing ? '完成' : '编辑' }}</text>
        </button>
      </template>
    </AppHeader>
    <view v-if="groups.length" class="cart__groups">
      <view v-for="group in groups" :key="group.merchantId" class="cart-group card">
        <button class="cart-group__shop" @click="openShop(group.merchantId)">
          <view class="cart-group__shop-name">
            <AppIcon type="shop" :size="17" color="#1a1a1a" />
            <text>{{ group.merchantName }}</text>
          </view>
          <AppIcon type="right" :size="15" color="#b8b8be" />
        </button>
        <view v-for="item in group.items" :key="item.id" class="cart-row" :class="{ 'cart-row--editing': editing }">
          <button
            class="check"
            :class="{ 'check--on': item.checked }"
            @click="toggle(item)"
          >{{ item.checked ? '✓' : '' }}</button>
          <image class="cart-row__image" :src="item.image" mode="aspectFill" />
          <view class="cart-row__body">
            <text class="cart-row__name">{{ item.skuName }}</text>
            <text class="cart-row__spec">{{ item.skuSpecText }}</text>
            <view class="cart-row__bottom">
              <view>
                <text class="cart-row__price">{{ money(item.price) }}</text>
                <text v-if="item.originalPrice > item.price" class="cart-row__deal">到手价</text>
              </view>
              <view class="stepper">
                <button aria-label="减少数量" @click="quantity(item, -1)"><AppIcon type="minus" :size="14" color="#66666b" /></button>
                <text>{{ item.quantity }}</text>
                <button aria-label="增加数量" @click="quantity(item, 1)"><AppIcon type="plus" :size="14" color="#66666b" /></button>
              </view>
            </view>
          </view>
          <button v-if="editing" class="cart-row__delete" aria-label="删除商品" @click="remove(item)">
            <AppIcon type="trash" :size="17" color="#ff3b30" />
          </button>
        </view>
      </view>
    </view>
    <EmptyState v-else title="购物车还是空的" description="去挑几件喜欢的商品吧" action="去逛逛" icon="cart" @action="goHome" />

    <view v-if="groups.length" class="checkout-bar safe-bottom">
      <button class="checkout-bar__all" @click="toggleAll">
        <view class="check" :class="{ 'check--on': allChecked }">{{ allChecked ? '✓' : '' }}</view>
        <text>全选</text>
      </button>
      <view class="checkout-bar__total">
        <text>合计</text>
        <text class="checkout-bar__amount">{{ money(total) }}</text>
      </view>
      <button class="checkout-bar__submit" :disabled="!selectedCount" @click="checkout">
        去结算({{ selectedCount }})
      </button>
    </view>
    <BottomNav active="cart" />
  </view>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import { onShow } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import BottomNav from '@/components/BottomNav.vue';
import EmptyState from '@/components/EmptyState.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import { money } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const items = ref<any[]>([]);
const editing = ref(false);

const groups = computed(() => {
  const result = new Map<string, any>();
  for (const item of items.value) {
    const key = String(item.merchantId || '0');
    if (!result.has(key)) {
      result.set(key, {
        merchantId: key,
        merchantName: item.merchantName || '平台自营',
        items: [],
      });
    }
    result.get(key).items.push(item);
  }
  return [...result.values()];
});
const selected = computed(() => items.value.filter((item) => item.checked));
const selectedCount = computed(() => selected.value.reduce((sum, item) => sum + Number(item.quantity || 0), 0));
const total = computed(() => selected.value.reduce(
  (sum, item) => sum + Number(item.price || 0) * Number(item.quantity || 0), 0,
));
const allChecked = computed(() => items.value.length > 0 && items.value.every((item) => item.checked));

async function load() {
  session.restore();
  if (!session.profile) {
    items.value = [];
    return;
  }
  items.value = await request<any[]>('/gateway/carts/List', {
    params: { customerId: session.profile.customerId },
    silent: true,
  }).catch(() => []);
}

async function toggle(item: any) {
  item.checked = !item.checked;
  if (item.checked) {
    for (const other of items.value) {
      if (other.merchantId !== item.merchantId) other.checked = false;
    }
  }
  await request('/gateway/carts/SetChecked', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, cartItemId: item.id, checked: item.checked },
    silent: true,
  }).catch(() => undefined);
}

async function toggleAll() {
  const next = !allChecked.value;
  const merchantId = items.value[0]?.merchantId;
  for (const item of items.value) {
    item.checked = item.merchantId === merchantId ? next : false;
  }
  uni.showToast({ title: '只能选择同一店铺商品', icon: 'none' });
}

async function quantity(item: any, delta: number) {
  const next = Math.max(1, Math.min(99, Number(item.quantity) + delta));
  if (next === item.quantity) return;
  item.quantity = next;
  await request('/gateway/carts/SetQuantity', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, cartItemId: item.id, quantity: next },
    silent: true,
  });
}

async function remove(item: any) {
  const confirmed = await new Promise<boolean>((resolve) => {
    uni.showModal({
      title: '删除商品',
      content: `确定从购物车移除「${item.skuName}」吗？`,
      success: (result) => resolve(result.confirm),
      fail: () => resolve(false),
    });
  });
  if (!confirmed) return;
  await request('/gateway/carts/Remove', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, cartItemId: item.id },
  });
  items.value = items.value.filter((row) => row.id !== item.id);
}

function checkout() {
  const first = selected.value[0];
  if (!first) return;
  const ids = selected.value.map((item) => item.id).join(',');
  uni.navigateTo({ url: `/pages/checkout/index?cartItemIds=${ids}&merchantId=${first.merchantId}` });
}

function openShop(id: string) {
  if (id !== '0') uni.navigateTo({ url: `/pages/shop/store?id=${id}` });
}

function goHome() {
  uni.reLaunch({ url: '/pages/index/index' });
}

onShow(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.edit {
  display: flex;
  align-items: center;
  gap: 4rpx;
  padding: 20rpx;
  border: 0;
  background: transparent;
  color: $muted;
  font-size: 26rpx;
}

.cart__groups {
  padding-top: 20rpx;
}

.cart-group {
  padding: 8rpx 20rpx 20rpx;
  margin-bottom: 20rpx;
}

.cart-group__shop {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  height: 78rpx;
  padding: 0;
  border: 0;
  background: transparent;
  font-size: 27rpx;
  font-weight: 500;
}

.cart-group__shop-name {
  display: flex;
  align-items: center;
  gap: 10rpx;
}

.cart-row {
  position: relative;
  display: flex;
  align-items: center;
  gap: 16rpx;
  padding: 16rpx 0;
  border-top: 1rpx solid $line;
}

.cart-row__delete {
  position: absolute;
  right: 4rpx;
  bottom: 16rpx;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 60rpx;
  height: 52rpx;
  padding: 0;
  border: 0;
  border-radius: 12rpx;
  background: #fff2f1;
}

.cart-row--editing .stepper {
  margin-right: 70rpx;
}

.check {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 42rpx;
  height: 42rpx;
  flex: 0 0 42rpx;
  padding: 0;
  border: 2rpx solid #cfcfd4;
  border-radius: 999rpx;
  background: #fff;
  color: #fff;
  font-size: 24rpx;
}

.check--on {
  border-color: $brand;
  background: $brand;
}

.cart-row__image {
  width: 164rpx;
  height: 164rpx;
  flex: 0 0 164rpx;
  border-radius: 16rpx;
  background: #f1f2f4;
}

.cart-row__body {
  display: flex;
  align-self: stretch;
  flex: 1;
  flex-direction: column;
  gap: 8rpx;
  min-width: 0;
}

.cart-row__name {
  display: -webkit-box;
  overflow: hidden;
  font-size: 27rpx;
  line-height: 1.35;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
}

.cart-row__spec {
  color: $muted;
  font-size: 22rpx;
}

.cart-row__bottom {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  margin-top: auto;
}

.cart-row__price {
  color: $price;
  font-size: 32rpx;
  font-weight: 600;
}

.cart-row__deal {
  margin-left: 8rpx;
  padding: 2rpx 8rpx;
  border-radius: 6rpx;
  background: #fff2f0;
  color: $price;
  font-size: 18rpx;
}

.stepper {
  display: flex;
  align-items: center;
  overflow: hidden;
  height: 56rpx;
  border-radius: 12rpx;
  background: #f3f4f6;
}

.stepper button,
.stepper text {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 56rpx;
  height: 56rpx;
  border: 0;
  background: transparent;
  font-size: 24rpx;
}

.checkout-bar {
  position: fixed;
  right: 0;
  bottom: 100rpx;
  left: 0;
  z-index: 40;
  display: flex;
  align-items: center;
  gap: 16rpx;
  min-height: 108rpx;
  padding: 12rpx 24rpx;
  border-top: 1rpx solid $line;
  background: #fff;
  box-shadow: $shadow-float;
}

.checkout-bar__all {
  display: flex;
  align-items: center;
  gap: 8rpx;
  padding: 0;
  border: 0;
  background: transparent;
  font-size: 24rpx;
}

.checkout-bar__total {
  display: flex;
  align-items: flex-end;
  flex: 1;
  flex-direction: column;
  color: $muted;
  font-size: 21rpx;
}

.checkout-bar__amount {
  color: $price;
  font-size: 34rpx;
  font-weight: 600;
}

.checkout-bar__submit {
  height: 76rpx;
  padding: 0 26rpx;
  border: 0;
  border-radius: 999rpx;
  background: $brand;
  color: #fff;
  font-size: 26rpx;
}

.checkout-bar__submit[disabled] {
  opacity: 0.4;
}
</style>
