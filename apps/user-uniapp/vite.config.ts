import { defineConfig } from 'vite';
import uniImport from '@dcloudio/vite-plugin-uni';

// 该包以 CommonJS 发布，不同 Node / Vite 组合下 default 会多包一层。
const resolved = (uniImport as any)?.default ?? uniImport;
const uni = (resolved as any)?.default ?? resolved;

export default defineConfig({
  plugins: [uni()],
  server: {
    host: '0.0.0.0',
    port: 5173,
    proxy: {
      '/gateway': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
      },
    },
  },
  preview: {
    host: '0.0.0.0',
    port: 5174,
    proxy: {
      '/gateway': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
      },
    },
  },
});
