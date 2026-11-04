<template>
  <view class="auth page-shell">
    <AppHeader back title="注册" />
    <view class="auth__head">
      <text class="auth__title">创建 {{ platform.current?.mallName }} 账号</text>
      <text class="auth__desc">同一手机号可在不同平台分别注册</text>
    </view>
    <view class="auth-form card">
      <label v-for="field in fields" :key="field.key" class="field">
        <text>{{ field.label }}</text>
        <input
          v-model="form[field.key]"
          :password="field.password"
          :placeholder="field.placeholder"
          :maxlength="field.maxlength"
        />
      </label>
      <view class="agreement">
        <button class="check" :class="{ 'check--on': agreed }" @click="agreed = !agreed">{{ agreed ? '✓' : '' }}</button>
        <text>我已同意用户协议和隐私政策</text>
      </view>
      <button class="btn-primary" :disabled="submitting" @click="submit">注册并登录</button>
      <button class="auth__switch" @click="goBack">已有账号？去登录</button>
    </view>
  </view>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import AppHeader from '@/components/AppHeader.vue';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

const platform = usePlatformStore();
const session = useSessionStore();
const agreed = ref(false);
const submitting = ref(false);
const form = reactive<Record<string, string>>({
  customerName: '',
  phone: '',
  password: '',
  confirmPassword: '',
  nickName: '',
});
const fields = [
  { key: 'customerName', label: '登录名', placeholder: '3-64 位登录名', password: false, maxlength: 64 },
  { key: 'phone', label: '手机号', placeholder: '请输入手机号', password: false, maxlength: 20 },
  { key: 'password', label: '密码', placeholder: '至少 8 位，包含字母和数字', password: true, maxlength: 64 },
  { key: 'confirmPassword', label: '确认密码', placeholder: '请再次输入密码', password: true, maxlength: 64 },
  { key: 'nickName', label: '昵称（选填）', placeholder: '不填则使用登录名', password: false, maxlength: 64 },
];

async function submit() {
  if (!agreed.value) {
    uni.showToast({ title: '请先同意协议', icon: 'none' });
    return;
  }
  if (form.password !== form.confirmPassword) {
    uni.showToast({ title: '两次密码不一致', icon: 'none' });
    return;
  }
  submitting.value = true;
  try {
    await session.register(
      form.customerName.trim(),
      form.password,
      form.phone.trim(),
      form.nickName.trim() || form.customerName.trim(),
    );
    uni.showToast({ title: '注册成功', icon: 'success' });
    setTimeout(() => uni.reLaunch({ url: '/pages/index/index' }), 350);
  } finally {
    submitting.value = false;
  }
}

function goBack() {
  uni.navigateBack();
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.auth__head {
  padding: 46rpx 12rpx 30rpx;
}

.auth__title {
  display: block;
  font-size: 42rpx;
  font-weight: 600;
}

.auth__desc {
  display: block;
  margin-top: 10rpx;
  color: $muted;
  font-size: 24rpx;
}

.auth-form {
  padding: 30rpx;
}

.field {
  display: block;
  margin-bottom: 22rpx;
}

.field text {
  display: block;
  margin-bottom: 10rpx;
  color: $muted;
  font-size: 23rpx;
}

.field input {
  height: 88rpx;
  padding: 0 20rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  font-size: 26rpx;
}

.agreement {
  display: flex;
  align-items: center;
  gap: 10rpx;
  margin: 8rpx 0 28rpx;
  color: $muted;
  font-size: 22rpx;
}

.check {
  width: 36rpx;
  height: 36rpx;
  padding: 0;
  border: 2rpx solid #cfcfd4;
  border-radius: 999rpx;
  background: #fff;
  color: #fff;
  font-size: 20rpx;
}

.check--on {
  border-color: $brand;
  background: $brand;
}

.auth__switch {
  width: 100%;
  height: 76rpx;
  margin-top: 18rpx;
  border: 0;
  background: transparent;
  color: $muted;
  font-size: 24rpx;
}
</style>
