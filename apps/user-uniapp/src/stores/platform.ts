import { defineStore } from 'pinia';
import { request } from '@/core/http';
import type { PlatformInfo } from '@/core/types';
import {
  clearPlatformBusinessCache,
  getLastPlatformCode,
  setLastPlatformCode,
} from '@/core/storage';

export const usePlatformStore = defineStore('platform', {
  state: () => ({
    platforms: [] as PlatformInfo[],
    current: null as PlatformInfo | null,
    ready: false,
  }),
  getters: {
    platformId: (state) => state.current?.platformId || '',
    platformCode: (state) => state.current?.platformCode || '',
    primaryColor: (state) => state.current?.primaryColor || '#0071e3',
  },
  actions: {
    async initialize() {
      if (this.ready) return;
      const list = await request<PlatformInfo[]>('/gateway/platforms/Public', {
        auth: false,
        silent: true,
      });
      this.platforms = (list || []).map((item) => ({
        ...item,
        platformId: String(item.platformId || item.id || ''),
      }));
      const remembered = getLastPlatformCode();
      const selected = this.platforms.find((item) => item.platformCode === remembered)
        || this.platforms[0];
      if (selected) await this.activate(selected.platformCode, false);
      this.ready = true;
    },
    async switchPlatform(code: string) {
      if (code === this.current?.platformCode) return;
      if (this.current) clearPlatformBusinessCache(this.current);
      await this.activate(code, true);
    },
    async activate(code: string, navigateHome: boolean) {
      const detail = await request<PlatformInfo>('/gateway/platforms/PublicDetail', {
        auth: false,
        params: { platformCode: code },
      });
      this.current = {
        ...detail,
        platformId: String(detail.platformId || detail.id || ''),
      };
      setLastPlatformCode(detail.platformCode);
      this.applyTheme(detail);
      if (navigateHome) uni.reLaunch({ url: '/pages/index/index' });
    },
    applyTheme(platform: PlatformInfo) {
      const brand = platform.primaryColor || '#0071e3';
      // #ifdef H5
      document.documentElement.style.setProperty('--brand', brand);
      document.documentElement.style.setProperty('--tab-color', platform.tabColor || brand);
      document.documentElement.style.setProperty('--page-bg', platform.backgroundColor || '#f5f6f8');
      // #endif
    },
  },
});
