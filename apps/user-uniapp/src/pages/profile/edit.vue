<template>
  <view class="profile-edit page-shell">
    <AppHeader title="个人资料" back />
    <view class="avatar-block">
      <image v-if="form.avatar" class="avatar" :src="form.avatar" mode="aspectFill" />
      <view v-else class="avatar avatar--empty">{{ session.displayName.slice(0, 1) }}</view>
      <button @click="chooseAvatar">
        <AppIcon type="camera" :size="15" color="#0071e3" />
        <text>更换头像</text>
      </button>
    </view>
    <view class="form card">
      <label><text>昵称</text><input v-model="form.nickName" maxlength="64" /></label>
      <label><text>性别</text><picker :range="genders" :value="form.gender" @change="form.gender = Number($event.detail.value)"><view>{{ genders[form.gender] }}</view></picker></label>
      <label><text>生日</text><picker mode="date" :value="form.birthday" @change="form.birthday = $event.detail.value"><view>{{ form.birthday || '请选择' }}</view></picker></label>
      <label><text>登录名</text><input :value="session.profile?.customerName" disabled /></label>
      <label><text>手机号</text><input :value="maskPhone(session.profile?.phone)" disabled /></label>
      <button class="password-link" @click="go('/pages/password/change')">
        <view><AppIcon type="locked" :size="18" color="#7a7a80" /><text>修改密码</text></view>
        <AppIcon type="right" :size="15" color="#b8b8be" />
      </button>
    </view>
    <button class="btn-primary" :disabled="saving" @click="save">保存</button>
  </view>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request, uploadImage } from '@/core/http';
import { maskPhone } from '@/core/format';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const saving = ref(false);
const genders = ['未填写', '男', '女'];
const form = reactive({
  nickName: '',
  avatar: '',
  gender: 0,
  birthday: '',
});

async function load() {
  session.restore();
  await session.refreshProfile();
  form.nickName = session.profile?.nickName || '';
  form.avatar = session.profile?.avatar || '';
  form.gender = Number(session.profile?.gender || 0);
  form.birthday = String(session.profile?.birthday || '').slice(0, 10);
}

async function chooseAvatar() {
  const path = await new Promise<string>((resolve) => {
    uni.chooseImage({
      count: 1,
      success: (result) => resolve(String((result.tempFilePaths as string[])[0] || '')),
      fail: () => resolve(''),
    });
  });
  if (path) form.avatar = await uploadImage(path);
}

async function save() {
  saving.value = true;
  try {
    await request('/gateway/customers/UpdateProfile', {
      method: 'POST',
      data: {
        customerId: session.profile!.customerId,
        nickName: form.nickName,
        avatar: form.avatar,
        gender: form.gender,
        birthday: form.birthday || null,
      },
    });
    await session.refreshProfile();
    uni.showToast({ title: '已保存', icon: 'success' });
    setTimeout(() => uni.navigateBack(), 350);
  } finally {
    saving.value = false;
  }
}

function go(url: string) {
  uni.navigateTo({ url });
}

onLoad(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.avatar-block {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 14rpx;
  padding: 38rpx 0 28rpx;
}

.avatar {
  width: 140rpx;
  height: 140rpx;
  border-radius: 999rpx;
}

.avatar--empty {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #fff;
  color: $brand;
  font-size: 46rpx;
}

.avatar-block button {
  display: flex;
  align-items: center;
  gap: 5rpx;
  padding: 10rpx;
  border: 0;
  background: transparent;
  color: $brand;
  font-size: 23rpx;
}

.form {
  padding: 4rpx 24rpx;
  margin-bottom: 28rpx;
}

.form label {
  display: grid;
  grid-template-columns: 150rpx 1fr;
  align-items: center;
  min-height: 92rpx;
  border-top: 1rpx solid $line;
}

.form label:first-child {
  border-top: 0;
}

.form label > text {
  color: $muted;
  font-size: 24rpx;
}

.form input,
.form picker view {
  height: 76rpx;
  padding: 0 16rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  font-size: 25rpx;
  line-height: 76rpx;
}

.form input[disabled] {
  color: $muted;
}

.password-link {
  display: flex;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  height: 82rpx;
  padding: 0;
  border: 0;
  border-top: 1rpx solid $line;
  background: transparent;
  color: $brand;
  font-size: 24rpx;
  text-align: left;
}

.password-link view {
  display: flex;
  align-items: center;
  gap: 12rpx;
}
</style>
