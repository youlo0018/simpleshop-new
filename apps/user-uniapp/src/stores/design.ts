import { defineStore } from 'pinia';
import { request } from '@/core/http';
import type { DesignComponentConfig, PlatformInfo } from '@/core/types';
import { usePlatformStore } from './platform';

interface DesignResult {
  configJson: string;
  version: number;
}

interface DesignConfig {
  pages?: Record<string, { components?: DesignComponentConfig[]; tabs?: any[] }>;
  mallTabs?: any[];
  tabBar?: any[];
}

const fallback = (): DesignComponentConfig[] => [
  { id: 'search', type: 'searchBar', props: {} },
  { id: 'banner', type: 'banner', props: { images: [] } },
  { id: 'grid', type: 'kingKong', props: { items: [] } },
  { id: 'title', type: 'title', props: { title: '为你推荐' } },
  { id: 'products', type: 'productGrid', props: { columns: 2, limit: 10 } },
];

export const useDesignStore = defineStore('design', {
  state: () => ({
    config: {} as DesignConfig,
    loadedCode: '',
  }),
  getters: {
    homeComponents: (state): DesignComponentConfig[] =>
      state.config.pages?.index?.components?.length
        ? state.config.pages.index.components!
        : fallback(),
    profileComponents: (state): DesignComponentConfig[] =>
      state.config.pages?.profile?.components || [],
    tabs: (state) => state.config.tabBar || [],
  },
  actions: {
    async load(force = false) {
      const platform = usePlatformStore();
      if (!platform.current) await platform.initialize();
      if (!force && this.loadedCode === platform.platformCode) return;
      const result = await request<DesignResult>('/gateway/design/PlatformStore', {
        auth: false,
        params: { platformCode: platform.platformCode },
        silent: true,
      }).catch(() => null);
      if (result?.configJson) {
        this.config = typeof result.configJson === 'string'
          ? JSON.parse(result.configJson) as DesignConfig
          : result.configJson as unknown as DesignConfig;
      } else {
        this.config = {};
      }
      this.loadedCode = platform.platformCode;
    },
  },
});
