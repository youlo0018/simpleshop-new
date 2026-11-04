<template>
  <view class="addresses page-shell">
    <AppHeader title="收货地址" back />
    <view v-for="item in items" :key="item.addressId" class="address-card card" @click="choose(item)">
      <view class="address-card__head">
        <text>{{ item.consigneeName }}</text>
        <text>{{ item.consigneePhone }}</text>
        <text v-if="item.label" class="tag">{{ item.label }}</text>
        <text v-if="item.isDefault" class="tag tag--default">默认</text>
      </view>
      <text class="address-card__detail">{{ item.regionPath }} {{ item.detailAddress }}</text>
      <view class="address-card__actions" @click.stop>
        <button v-if="!item.isDefault" @click="setDefault(item)"><AppIcon type="checkmarkempty" :size="14" color="#8a8a8f" />设为默认</button>
        <button @click="edit(item)"><AppIcon type="compose" :size="14" color="#8a8a8f" />编辑</button>
        <button class="danger" @click="remove(item)"><AppIcon type="trash" :size="14" color="#ff3b30" />删除</button>
      </view>
    </view>
    <EmptyState v-if="!items.length" title="还没有收货地址" icon="location" />
    <button class="btn-primary add-btn" @click="add">
      <AppIcon type="plusempty" :size="18" color="#ffffff" />
      <text>新增收货地址</text>
    </button>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onShow } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const items = ref<any[]>([]);
const selecting = ref(false);

async function load() {
  session.restore();
  const result = await request<any>('/gateway/customers/addresses/List', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, page: 1, pageSize: 100 },
  });
  items.value = result.items || [];
}

function choose(item: any) {
  if (!selecting.value) return;
  uni.setStorageSync('simpleshop:selected-address', item);
  uni.navigateBack();
}

async function setDefault(item: any) {
  await request('/gateway/customers/addresses/SetDefault', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, addressId: item.addressId },
  });
  load();
}

function edit(item: any) {
  uni.navigateTo({ url: `/pages/address/form?id=${item.addressId}` });
}

async function remove(item: any) {
  const confirmed = await modal('删除地址', `${item.consigneeName} ${item.regionPath}${item.detailAddress}`);
  if (!confirmed) return;
  await request('/gateway/customers/addresses/Delete', {
    method: 'POST',
    data: { customerId: session.profile!.customerId, addressId: item.addressId },
  });
  load();
}

function add() {
  uni.navigateTo({ url: '/pages/address/form' });
}

function modal(title: string, content: string) {
  return new Promise<boolean>((resolve) => {
    uni.showModal({ title, content, success: (result) => resolve(result.confirm), fail: () => resolve(false) });
  });
}

onLoad((query) => {
  selecting.value = String(query?.select || '') === '1';
});
onShow(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.address-card {
  padding: 24rpx;
  margin-top: 20rpx;
}

.address-card__head {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12rpx;
  font-size: 27rpx;
  font-weight: 500;
}

.tag {
  padding: 3rpx 10rpx;
  border-radius: 8rpx;
  background: #f2f3f5;
  color: $muted;
  font-size: 20rpx;
  font-weight: 400;
}

.tag--default {
  background: #eef4ff;
  color: $brand;
}

.address-card__detail {
  display: block;
  margin-top: 10rpx;
  color: $muted;
  font-size: 23rpx;
  line-height: 1.5;
}

.address-card__actions {
  display: flex;
  justify-content: flex-end;
  gap: 16rpx;
  margin-top: 18rpx;
  padding-top: 16rpx;
  border-top: 1rpx solid $line;
}

.address-card__actions button {
  display: flex;
  align-items: center;
  gap: 4rpx;
  padding: 8rpx 0;
  border: 0;
  background: transparent;
  color: $muted;
  font-size: 22rpx;
}

.address-card__actions button.danger {
  color: $danger;
}

.add-btn {
  gap: 8rpx;
  margin-top: 30rpx;
}
</style>
