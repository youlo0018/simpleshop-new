import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';

// 空闲连接不能被服务端先关掉。
//
// Node 的 http server 默认 keepAliveTimeout 只有 5 秒，而浏览器会把空闲连接保留几分钟：
// 页面开着放一会儿，浏览器复用的是一条**服务端已经关掉**的连接，
// 下一个请求（切换路由要加载的 chunk、列表页要发的接口）就会挂在那里 ——
// 现象正是「点导航没反应，刷新才能继续」，而且刷新后一定好（新连接）。
// 实测：preview 在 6 秒时关闭连接，8 秒后复用同一连接发请求，收不到任何响应。
// 把服务端超时提到 5 分钟（盖住浏览器的复用窗口），headersTimeout 必须比它更大。
const LONG_KEEP_ALIVE_MS = 300_000;

// 同一份设置要同时挂给 dev 与 preview —— 两个 server 是各自独立的实例。
// 参数故意用宽类型：Vite 的 httpServer 还可能是 Http2SecureServer，
// 而 HTTP/2 没有这两个字段（连接由 HTTP/2 自己管），强类型会直接编译不过。
function extendKeepAlive(server: { httpServer: unknown }) {
  const httpServer = server.httpServer as
    { keepAliveTimeout?: number; headersTimeout?: number } | null | undefined;
  if (!httpServer) return;

  if (typeof httpServer.keepAliveTimeout === 'number') {
    httpServer.keepAliveTimeout = LONG_KEEP_ALIVE_MS;
  }
  if (typeof httpServer.headersTimeout === 'number') {
    httpServer.headersTimeout = LONG_KEEP_ALIVE_MS + 1000;
  }
}

// 后台开发配置。
// 说明两点：
//   1. 代理指向本机网关 5008 —— 前端**只**跟网关打交道，
//      直连各业务服务端口会让 CORS 与鉴权两件事在前端各写一遍。
//   2. 仓库路径含中文，Vite 的 fs.allow 必须放开，否则热更新会报 403。
export default defineConfig({
  plugins: [
    vue(),
    {
      name: 'simpleshop-keep-alive',
      configureServer: extendKeepAlive,
      configurePreviewServer: extendKeepAlive,
    },
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    fs: {
      // 允许仓库根目录（中文路径），保证热更新能读到 src 外的共享资源
      allow: [fileURLToPath(new URL('../../', import.meta.url))],
    },
    proxy: {
      '/gateway': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
      },
    },
  },
  preview: {
    port: 5174,
    strictPort: true,
    // ⚠️ preview **不继承** server.proxy，必须单独再写一遍。
    // 漏掉的表现是：页面能打开、样式也正常，但一登录就 404 /gateway/auth/token，
    // 很容易被误判成「后端没起」或「令牌端点配错了」。
    proxy: {
      '/gateway': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    // 源码里有中文字段名（XxxText），不做压缩关键字改名
    chunkSizeWarningLimit: 1200,
  },
});
