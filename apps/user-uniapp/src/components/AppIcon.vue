<template>
  <uni-icons
    :type="type"
    :size="size"
    :color="color"
    class="app-icon"
  />
</template>

<script setup lang="ts">
withDefaults(defineProps<{
  type: string;
  size?: number;
  color?: string;
}>(), {
  size: 20,
  color: '#1a1a1a',
});
</script>

<style scoped>
.app-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  line-height: 1;
}
</style>
