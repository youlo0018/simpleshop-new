<template>
  <view class="profile page-shell">
    <view class="profile__top">
      <button class="platform-trigger" @click="switcherOpen = true">
        <view class="platform-trigger__logo">{{ (platform.current?.mallName || '商').slice(0, 1) }}</view>
        <text>{{ platform.current?.mallName || '切换商城' }}</text>
        <AppIcon type="arrow-down" :size="13" color="#8a8a8f" />
      </button>
      <button class="settings" @click="go('/pages/profile/edit')">
        <AppIcon type="settings" :size="17" color="#66666b" />
        <text>编辑</text>
      </button>
    </view>

    <view class="user-head">
      <image v-if="session.profile?.avatar" class="user-head__avatar" :src="session.profile.avatar" mode="aspectFill" />
      <view v-else class="user-head__avatar user-head__avatar--empty">
        {{ session.loggedIn ? session.displayName.slice(0, 1) : '访' }}
      </view>
      <view class="user-head__body">
        <template v-if="session.loggedIn">
          <text class="user-head__name">{{ session.displayName }}</text>
          <text class="user-head__phone">{{ maskPhone(session.profile?.phone) || '普通会员' }}</text>
        </template>
        <template v-else>
          <text class="user-head__name">登录 / 注册</text>
          <text class="user-head__phone">登录后查看订单与会员权益</text>
        </template>
      </view>
      <button v-if="!session.loggedIn" class="user-head__login" @click="go('/pages/login/index')">去登录</button>
    </view>

    <view class="member-card">
      <view class="member-card__head">
        <view>
          <text class="member-card__eyebrow">SIMPLE CLUB</text>
          <text class="member-card__title">普通会员</text>
        </view>
        <view class="member-card__badge">
          <AppIcon type="medal-filled" :size="14" color="#ffffff" />
          <text>MEMBER</text>
        </view>
      </view>
      <view class="member-card__stats">
        <button @click="go('/pages/coupon/mine')"><b>{{ couponCount }}</b><text>优惠券</text></button>
        <button @click="go('/pages/points/index')"><b>{{ pointBalance.available || 0 }}</b><text>积分</text></button>
        <button @click="go('/pages/favorite/list')"><b>{{ favoriteCount }}</b><text>收藏</text></button>
      </view>
    </view>

    <view class="services card">
      <button v-for="item in services" :key="item.label" @click="go(item.path)">
        <view class="services__icon" :class="`services__icon--${item.tone}`">
          <AppIcon :type="item.icon" :size="22" :color="item.color" />
        </view>
        <text>{{ item.label }}</text>
      </button>
    </view>

    <ViewDesignFloors />

    <view class="settings-list card">
      <button v-for="item in settings" :key="item.label" @click="item.action ? item.action() : go(item.path)">
        <view class="settings-list__label">
          <AppIcon :type="item.icon" :size="18" color="#7a7a80" />
          <text>{{ item.label }}</text>
        </view>
        <AppIcon class="settings-list__arrow" type="right" :size="16" color="#b8b8be" />
      </button>
    </view>

    <BottomNav active="profile" />
    <PlatformSwitcher :open="switcherOpen" @close="switcherOpen = false" />
  </view>
</template>

<script setup lang="ts">
import { defineComponent, h, ref, type FunctionalComponent } from 'vue';
import { onPullDownRefresh, onShow } from '@dcloudio/uni-app';
import BottomNav from '@/components/BottomNav.vue';
import PlatformSwitcher from '@/components/PlatformSwitcher.vue';
import DesignComponent from '@/components/DesignComponent.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request } from '@/core/http';
import { maskPhone } from '@/core/format';
import type { Paged } from '@/core/types';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';
import { useDesignStore } from '@/stores/design';

const platform = usePlatformStore();
const session = useSessionStore();
const design = useDesignStore();
const switcherOpen = ref(false);
const couponCount = ref(0);
const favoriteCount = ref(0);
const pointBalance = ref<any>({});

const services = [
  { label: '我的订单', icon: 'list', color: '#2f6bff', tone: 'blue', path: '/pages/order/list' },
  { label: '券包', icon: 'gift', color: '#e87935', tone: 'orange', path: '/pages/coupon/mine' },
  { label: '积分', icon: 'wallet', color: '#00a998', tone: 'green', path: '/pages/points/index' },
  { label: '收藏', icon: 'heart', color: '#e94a65', tone: 'rose', path: '/pages/favorite/list' },
  { label: '地址', icon: 'location', color: '#7659d8', tone: 'violet', path: '/pages/address/list' },
];

const settings = [
  { label: '我的评价', icon: 'chat', path: '/pages/evaluate/mine' },
  { label: '领券中心', icon: 'gift', path: '/pages/coupon/center' },
  { label: '个人资料', icon: 'person', path: '/pages/profile/edit' },
  { label: '修改密码', icon: 'locked', path: '/pages/password/change' },
  { label: '用户协议', icon: 'paperclip', path: '/pages/legal/agreement' },
  { label: '隐私政策', icon: 'auth', path: '/pages/legal/privacy' },
  { label: '关于我们', icon: 'info', path: '/pages/legal/about' },
  { label: '退出登录', icon: 'undo', path: '', action: logout },
];

const ViewDesignFloors = defineComponent({
  name: 'ViewDesignFloors',
  setup() {
    return () => h('view', { class: 'profile-floors' }, design.profileComponents.map((component, index) =>
      h(DesignComponent, { component, key: component.id || `profile-${index}` })));
  },
});

async function load() {
  session.restore();
  if (!platform.ready) await platform.initialize();
  await design.load();
  if (!session.loggedIn || !session.profile) {
    couponCount.value = 0;
    pointBalance.value = {};
    favoriteCount.value = 0;
    uni.stopPullDownRefresh();
    return;
  }
  const customerId = session.profile.customerId;
  const [coupons, points, favorites] = await Promise.all([
    request<Paged<any>>('/gateway/coupons/My', {
      method: 'POST',
      data: { customerId, status: 1, page: 1, pageSize: 20 },
      silent: true,
    }).catch(() => ({ total: 0 })),
    request<any>('/gateway/points/Balance', {
      params: { customerId },
      silent: true,
    }).catch(() => ({})),
    request<Paged<any>>('/gateway/customers/favorites/List', {
      method: 'POST',
      data: { customerId, page: 1, pageSize: 20 },
      silent: true,
    }).catch(() => ({ total: 0 })),
  ]);
  couponCount.value = coupons.total || 0;
  pointBalance.value = points || {};
  favoriteCount.value = favorites.total || 0;
  uni.stopPullDownRefresh();
}

async function logout() {
  await session.logout();
  uni.showToast({ title: '已退出登录', icon: 'none' });
  await load();
}

function go(path: string) {
  if (!path) return;
  if (!session.loggedIn && path !== '/pages/legal/about') {
    uni.navigateTo({ url: `/pages/login/index?redirect=${encodeURIComponent(path)}` });
    return;
  }
  uni.navigateTo({ url: path });
}

onShow(load);
onPullDownRefresh(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.profile {
  padding-top: calc(env(safe-area-inset-top) + 16rpx);
}

.profile__top {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  align-items: center;
  min-height: 88rpx;
  gap: 20rpx;
}

.platform-trigger {
  display: flex;
  align-items: center;
  justify-self: start;
  max-width: 100%;
  height: 72rpx;
  padding: 0 18rpx 0 8rpx;
  margin: 0;
  gap: 10rpx;
  border: 0;
  border-radius: 999rpx;
  background: rgba(255, 255, 255, 0.86);
  box-shadow: $shadow;
  font-size: 24rpx;
}

.platform-trigger > text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.platform-trigger__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 54rpx;
  height: 54rpx;
  flex: 0 0 54rpx;
  border-radius: 999rpx;
  background: #eef4ff;
  color: $brand;
  font-size: 24rpx;
  font-weight: 600;
}

.settings {
  display: flex;
  align-items: center;
  justify-self: end;
  height: 72rpx;
  gap: 6rpx;
  padding: 0 4rpx 0 20rpx;
  margin: 0;
  border: 0;
  background: transparent;
  color: #66666b;
  font-size: 23rpx;
  line-height: 1;
  white-space: nowrap;
}

.user-head {
  display: flex;
  align-items: center;
  gap: 20rpx;
  padding: 36rpx 8rpx 28rpx;
}

.user-head__avatar {
  width: 112rpx;
  height: 112rpx;
  flex: 0 0 112rpx;
  border-radius: 999rpx;
  box-shadow: 0 8rpx 24rpx rgba(0, 0, 0, 0.08);
}

.user-head__avatar--empty {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fff;
  color: $brand;
  font-size: 38rpx;
  font-weight: 600;
}

.user-head__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 8rpx;
  min-width: 0;
}

.user-head__name {
  overflow: hidden;
  font-size: 38rpx;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.user-head__phone {
  color: $muted;
  font-size: 24rpx;
}

.user-head__login {
  height: 64rpx;
  padding: 0 24rpx;
  border: 0;
  border-radius: 999rpx;
  background: #fff;
  color: $brand;
  font-size: 25rpx;
}

.member-card {
  overflow: hidden;
  padding: 32rpx;
  border-radius: 24rpx;
  background: linear-gradient(120deg, #7fd8c9 0%, #6fa8f5 55%, #a87bf0 100%);
  box-shadow: 0 14rpx 40rpx rgba(81, 135, 194, 0.22);
  color: #fff;
}

.member-card__head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
}

.member-card__eyebrow,
.member-card__title {
  display: block;
}

.member-card__eyebrow {
  opacity: 0.75;
  font-size: 18rpx;
  letter-spacing: 2rpx;
}

.member-card__title {
  margin-top: 8rpx;
  font-size: 38rpx;
  font-weight: 600;
}

.member-card__badge {
  display: flex;
  align-items: center;
  gap: 6rpx;
  padding: 8rpx 14rpx;
  border-radius: 999rpx;
  background: rgba(255, 255, 255, 0.2);
  font-size: 18rpx;
}

.member-card__stats {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  margin-top: 34rpx;
}

.member-card__stats button {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 8rpx;
  border: 0;
  background: transparent;
  color: #fff;
}

.member-card__stats b {
  font-size: 42rpx;
}

.member-card__stats text {
  opacity: 0.76;
  font-size: 22rpx;
}

.services {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 8rpx;
  padding: 26rpx 12rpx;
  margin-top: 24rpx;
}

.services button {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 10rpx;
  padding: 0;
  border: 0;
  background: transparent;
  font-size: 22rpx;
}

.services__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 64rpx;
  height: 64rpx;
  border-radius: 20rpx;
  border-radius: 19rpx;
}

.services__icon--blue {
  background: #edf3ff;
}

.services__icon--orange {
  background: #fff4ea;
}

.services__icon--green {
  background: #e9faf6;
}

.services__icon--rose {
  background: #fff0f3;
}

.services__icon--violet {
  background: #f2efff;
}

.profile-floors {
  margin-top: 24rpx;
}

.settings-list {
  padding: 4rpx 24rpx;
  margin-top: 24rpx;
}

.settings-list button {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  min-height: 92rpx;
  padding: 0;
  border: 0;
  border-top: 1rpx solid $line;
  background: transparent;
  font-size: 27rpx;
}

.settings-list button:first-child {
  border-top: 0;
}

.settings-list__label {
  display: flex;
  align-items: center;
  gap: 16rpx;
}

.settings-list__arrow {
  opacity: 0.9;
}
</style>
