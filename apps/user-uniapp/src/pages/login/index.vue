<template>
  <view class="auth page-shell">
    <AppHeader back title="" />
    <view class="auth__brand">
      <image v-if="platform.current?.logo" class="auth__logo" :src="platform.current.logo" mode="aspectFill" />
      <view v-else class="auth__logo auth__logo--text">{{ (platform.current?.mallName || '商').slice(0, 1) }}</view>
      <text class="auth__mall">{{ platform.current?.mallName }}</text>
      <text class="auth__welcome">欢迎回来</text>
    </view>
    <view class="auth-form">
      <label class="field">
        <text>登录名</text>
        <input v-model="customerName" placeholder="请输入登录名" autocomplete="username" />
      </label>
      <label class="field">
        <text>密码</text>
        <view class="field__password">
          <input v-model="password" :password="!visible" placeholder="请输入密码" />
          <button aria-label="显示或隐藏密码" @click="visible = !visible">
            <AppIcon :type="visible ? 'eye-slash' : 'eye'" :size="18" color="#8a8a8f" />
          </button>
        </view>
      </label>
      <view class="agreement">
        <button class="check" :class="{ 'check--on': agreed }" aria-label="同意协议" @click="agreed = !agreed">
          <AppIcon v-if="agreed" type="checkmarkempty" :size="14" color="#ffffff" />
        </button>
        <view class="agreement__text">
          <text>我已阅读并同意</text>
          <text class="link" @click="go('/pages/legal/agreement')">用户协议</text>
          <text>和</text>
          <text class="link" @click="go('/pages/legal/privacy')">隐私政策</text>
        </view>
      </view>
      <button class="btn-primary" :disabled="submitting" @click="submit">登录</button>
      <view class="auth__links">
        <text @click="go('/pages/password/forgot')">忘记密码</text>
        <text @click="go('/pages/register/index')">还没有账号？去注册</text>
      </view>
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import AppIcon from '@/components/AppIcon.vue';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const customerName = ref(String(uni.getStorageSync('simpleshop:last-login-name') || ''));
const password = ref('');
const visible = ref(false);
const agreed = ref(false);
const submitting = ref(false);
let redirect = '';

async function submit() {
  if (!customerName.value || !password.value) {
    uni.showToast({ title: '请填写登录名和密码', icon: 'none' });
    return;
  }
  if (!agreed.value) {
    uni.showToast({ title: '请先同意用户协议和隐私政策', icon: 'none' });
    return;
  }
  submitting.value = true;
  try {
    await session.login(customerName.value.trim(), password.value);
    uni.setStorageSync('simpleshop:last-login-name', customerName.value.trim());
    uni.showToast({ title: '登录成功', icon: 'success' });
    setTimeout(() => {
      if (redirect) uni.redirectTo({ url: redirect });
      else uni.reLaunch({ url: '/pages/index/index' });
    }, 350);
  } finally {
    submitting.value = false;
  }
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad((query) => {
  redirect = String(query?.redirect || '');
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.auth {
  padding-bottom: 80rpx;
}

.auth__brand {
  display: flex;
  align-items: center;
  flex-direction: column;
  padding: 70rpx 0 50rpx;
}

.auth__logo {
  width: 112rpx;
  height: 112rpx;
  border-radius: 28rpx;
  box-shadow: $shadow-float;
}

.auth__logo--text {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fff;
  color: $brand;
  font-size: 42rpx;
  font-weight: 600;
}

.auth__mall {
  margin-top: 18rpx;
  font-size: 26rpx;
  font-weight: 500;
}

.auth__welcome {
  margin-top: 16rpx;
  font-size: 46rpx;
  font-weight: 600;
}

.auth-form {
  padding: 30rpx;
  border-radius: 24rpx;
  background: #fff;
  box-shadow: $shadow;
}

.field {
  display: block;
  margin-bottom: 24rpx;
}

.field > text {
  display: block;
  margin-bottom: 10rpx;
  color: $muted;
  font-size: 23rpx;
}

.field input {
  width: 100%;
  height: 88rpx;
  padding: 0 20rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  font-size: 27rpx;
}

.field__password {
  display: flex;
  align-items: center;
  border-radius: 12rpx;
  background: #f5f6f8;
}

.field__password input {
  flex: 1;
  background: transparent;
}

.field__password button {
  height: 88rpx;
  padding: 0 20rpx;
  border: 0;
  background: transparent;
  color: $muted;
  font-size: 22rpx;
}

.agreement {
  display: flex;
  align-items: flex-start;
  gap: 8rpx;
  margin: 8rpx 0 28rpx;
  color: $muted;
  font-size: 21rpx;
  line-height: 1.65;
}

.check {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 36rpx;
  height: 36rpx;
  flex: 0 0 36rpx;
  margin: 2rpx 0 0;
  padding: 0;
  border: 2rpx solid #cfcfd4;
  border-radius: 999rpx;
  background: #fff;
}

.check--on {
  border-color: $brand;
  background: $brand;
}

.link {
  color: $brand;
}

.agreement__text {
  flex: 1;
  min-width: 0;
  line-height: 1.65;
}

.auth__links {
  display: flex;
  justify-content: space-between;
  margin-top: 28rpx;
  color: $muted;
  font-size: 23rpx;
}
</style>
