<template>
  <view class="password page-shell">
    <AppHeader title="修改密码" back />
    <view class="form card">
      <label><text>旧密码</text><input v-model="oldPassword" password placeholder="请输入旧密码" /></label>
      <label><text>新密码</text><input v-model="newPassword" password placeholder="至少 8 位，含字母和数字" /></label>
      <label><text>确认新密码</text><input v-model="confirmPassword" password placeholder="请再次输入新密码" /></label>
    </view>
    <button class="btn-primary" :disabled="submitting" @click="submit">保存</button>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import AppHeader from '@/components/AppHeader.vue';
import { request } from '@/core/http';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const oldPassword = ref('');
const newPassword = ref('');
const confirmPassword = ref('');
const submitting = ref(false);

async function submit() {
  if (newPassword.value !== confirmPassword.value) {
    uni.showToast({ title: '两次密码不一致', icon: 'none' });
    return;
  }
  submitting.value = true;
  try {
    await request('/gateway/customers/ChangePassword', {
      method: 'POST',
      data: {
        customerId: session.profile?.customerId,
        oldPassword: oldPassword.value,
        newPassword: newPassword.value,
      },
    });
    uni.showToast({ title: '密码修改成功', icon: 'success' });
    setTimeout(() => uni.navigateBack(), 350);
  } finally {
    submitting.value = false;
  }
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.form {
  padding: 24rpx;
  margin: 28rpx 0;
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
</style>
