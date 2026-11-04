<script setup lang="ts">
import { onLaunch } from '@dcloudio/uni-app';
import { usePlatformStore } from '@/stores/platform';
import { useSessionStore } from '@/stores/session';

onLaunch(async () => {
  await usePlatformStore().initialize();
  useSessionStore().restore();
});
</script>

<style lang="scss">
@use '@/styles/base.scss';
</style>
