<template>
  <view class="about page-shell">
    <AppHeader title="关于我们" back />
    <view class="about__brand">
      <image v-if="platform.current?.logo" :src="platform.current.logo" mode="aspectFill" />
      <view v-else class="about__logo">{{ (platform.current?.mallName || '商').slice(0, 1) }}</view>
      <text class="about__name">{{ platform.current?.mallName }}</text>
      <text class="about__version">SimpleShop 2.0</text>
    </view>
    <view class="about__card card">
      <text class="about__intro">{{ platform.current?.mallIntro || '精选商品，安心购物。' }}</text>
      <button v-if="platform.current?.servicePhone" @click="call">客服电话 {{ platform.current.servicePhone }}</button>
      <text v-if="platform.current?.icpNo" class="about__icp">{{ platform.current.icpNo }}</text>
    </view>
  </view>
</template>

<script setup lang="ts">
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import { usePlatformStore } from '@/stores/platform';

const platform = usePlatformStore();

function call() {
  if (platform.current?.servicePhone) {
    uni.makePhoneCall({ phoneNumber: platform.current.servicePhone });
  }
}

onLoad(async () => {
  await platform.initialize();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.about__brand {
  display: flex;
  align-items: center;
  flex-direction: column;
  padding: 70rpx 0 40rpx;
}

.about__brand image,
.about__logo {
  width: 132rpx;
  height: 132rpx;
  border-radius: 30rpx;
  box-shadow: $shadow-float;
}

.about__logo {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fff;
  color: $brand;
  font-size: 50rpx;
  font-weight: 600;
}

.about__name {
  margin-top: 20rpx;
  font-size: 36rpx;
  font-weight: 600;
}

.about__version {
  margin-top: 8rpx;
  color: $muted;
  font-size: 22rpx;
}

.about__card {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 24rpx;
  padding: 30rpx;
}

.about__intro {
  color: #3e3e42;
  font-size: 26rpx;
  line-height: 1.7;
  text-align: center;
}

.about__card button {
  width: 100%;
  height: 78rpx;
  border: 0;
  border-radius: 999rpx;
  background: #f4f5f7;
  color: $brand;
  font-size: 24rpx;
}

.about__icp {
  color: $muted;
  font-size: 21rpx;
}
</style>
