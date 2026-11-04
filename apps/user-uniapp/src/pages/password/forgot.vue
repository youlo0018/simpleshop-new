<template>
  <view class="password page-shell">
    <AppHeader title="忘记密码" back />
    <view class="password__intro">
      <text>通过注册手机号重置密码</text>
      <text>{{ platform.current?.mallName }}</text>
    </view>
    <view class="form card">
      <label><text>手机号</text><input v-model="phone" maxlength="20" placeholder="请输入注册手机号" /></label>
      <label>
        <text>验证码</text>
        <view class="code-row">
          <input v-model="code" maxlength="8" placeholder="请输入验证码" />
          <button :disabled="cooldown > 0" @click="sendCode">{{ cooldown ? `${cooldown}s` : '获取验证码' }}</button>
        </view>
      </label>
      <label><text>新密码</text><input v-model="newPassword" password placeholder="至少 8 位，含字母和数字" /></label>
      <label><text>确认密码</text><input v-model="confirmPassword" password placeholder="请再次输入新密码" /></label>
      <text v-if="debugCode" class="debug">本地调试验证码：{{ debugCode }}</text>
    </view>
    <button class="btn-primary" :disabled="submitting" @click="submit">重置密码</button>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import AppHeader from '@/components/AppHeader.vue';
import { request } from '@/core/http';
import { usePlatformStore } from '@/stores/platform';

const platform = usePlatformStore();
const phone = ref('');
const code = ref('');
const newPassword = ref('');
const confirmPassword = ref('');
const debugCode = ref('');
const cooldown = ref(0);
const submitting = ref(false);
let timer: ReturnType<typeof setInterval> | null = null;

async function sendCode() {
  const result = await request<any>('/gateway/customers/SendPasswordResetCode', {
    method: 'POST',
    auth: false,
    data: { platformCode: platform.platformCode, phone: phone.value },
  });
  debugCode.value = result.debugCode || '';
  cooldown.value = 60;
  timer = setInterval(() => {
    cooldown.value = Math.max(0, cooldown.value - 1);
    if (!cooldown.value && timer) clearInterval(timer);
  }, 1000);
}

async function submit() {
  if (newPassword.value !== confirmPassword.value) {
    uni.showToast({ title: '两次密码不一致', icon: 'none' });
    return;
  }
  submitting.value = true;
  try {
    await request('/gateway/customers/ResetPassword', {
      method: 'POST',
      auth: false,
      data: {
        platformCode: platform.platformCode,
        phone: phone.value,
        code: code.value,
        newPassword: newPassword.value,
      },
    });
    uni.showToast({ title: '密码已重置', icon: 'success' });
    setTimeout(() => uni.redirectTo({ url: '/pages/login/index' }), 350);
  } finally {
    submitting.value = false;
  }
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.password__intro {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  padding: 42rpx 8rpx 28rpx;
}

.password__intro text:first-child {
  font-size: 36rpx;
  font-weight: 600;
}

.password__intro text:last-child {
  color: $muted;
  font-size: 24rpx;
}

.form {
  padding: 24rpx;
  margin-bottom: 28rpx;
}

.form label {
  display: block;
  margin-bottom: 22rpx;
}

.form label > text {
  display: block;
  margin-bottom: 10rpx;
  color: $muted;
  font-size: 23rpx;
}

.form input {
  width: 100%;
  height: 84rpx;
  padding: 0 18rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  font-size: 25rpx;
}

.code-row {
  display: flex;
  gap: 12rpx;
}

.code-row input {
  flex: 1;
}

.code-row button {
  width: 200rpx;
  height: 84rpx;
  border: 0;
  border-radius: 12rpx;
  background: #eef4ff;
  color: $brand;
  font-size: 22rpx;
}

.debug {
  color: $warning;
  font-size: 22rpx;
}
</style>
