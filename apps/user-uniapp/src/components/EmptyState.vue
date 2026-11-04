<template>
  <view class="empty">
    <view class="empty__icon">
      <AppIcon :type="icon" :size="34" color="#b8b8be" />
    </view>
    <text class="empty__title">{{ title }}</text>
    <text v-if="description" class="empty__description">{{ description }}</text>
    <button v-if="action" class="btn-ghost empty__action" @click="emit('action')">{{ action }}</button>
  </view>
</template>

<script setup lang="ts">
import AppIcon from './AppIcon.vue';

withDefaults(defineProps<{
  title: string;
  description?: string;
  action?: string;
  icon?: string;
}>(), {
  description: '',
  action: '',
  icon: 'info',
});

const emit = defineEmits<{ action: [] }>();
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.empty {
  display: flex;
  align-items: center;
  flex-direction: column;
  justify-content: center;
  gap: 16rpx;
  min-height: 360rpx;
  padding: 48rpx 24rpx;
}

.empty__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 144rpx;
  height: 144rpx;
  border-radius: 999rpx;
  background: #fff;
  color: $tertiary;
}

.empty__title {
  font-size: 30rpx;
  font-weight: 500;
}

.empty__description {
  color: $muted;
  font-size: 24rpx;
  text-align: center;
}

.empty__action {
  margin-top: 12rpx;
}
</style>
