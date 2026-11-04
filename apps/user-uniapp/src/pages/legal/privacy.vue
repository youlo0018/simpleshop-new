<template>
  <view class="legal page-shell">
    <AppHeader title="隐私政策" back />
    <view class="legal__body card">
      <rich-text v-if="content" :nodes="content" />
      <EmptyState v-else title="平台暂未配置隐私政策" />
    </view>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import { usePlatformStore } from '@/stores/platform';

const platform = usePlatformStore();
const content = ref('');

onLoad(async () => {
  await platform.initialize();
  content.value = platform.current?.privacyPolicy || '';
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.legal__body {
  min-height: 60vh;
  padding: 28rpx;
  margin-top: 20rpx;
  color: #3e3e42;
  font-size: 26rpx;
  line-height: 1.75;
}
</style>
