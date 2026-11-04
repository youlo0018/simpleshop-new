import { createRouter, createWebHashHistory, type RouteRecordRaw } from 'vue-router';
import { isLoggedIn } from '@/api/auth';

// 菜单结构同时驱动侧边栏与路由，单一来源（DESIGN_SPEC「不凑合」原则：
// 菜单与路由各写一份，加页面时必然漏一处）。
// 后续页面按模块拆到各自文件，由 modules.ts 汇总。
import { adminRoutes } from './modules';

// 页面模块（chunk）加载的超时保护。
//
// 长时间开着页面后再点菜单，chunk 请求可能挂在一条**服务端已经关掉**的空闲连接上
// （见 vite.config.ts 里 keep-alive 的说明）。ES module 的加载 Promise 一旦挂住
// 就一直 pending，vue-router 只能干等 —— 表现就是「点导航没反应，刷新才好」。
// 模块加载无法取消（重复 import 拿到的是同一个 Promise），所以超时后自动刷新一次
// 是唯一可靠的恢复手段；用 sessionStorage 防死循环，加载成功后再清掉标记。
const RELOAD_FLAG = 'simpleshop_chunk_reload';
const CHUNK_TIMEOUT_MS = 10_000;

function lazyPage(loader: () => Promise<unknown>) {
  return async () => {
    let timer: number | undefined;
    const timeout = new Promise<never>((_, reject) => {
      timer = window.setTimeout(
        () => reject(new Error('页面资源加载超时')), CHUNK_TIMEOUT_MS);
    });

    try {
      const mod = await Promise.race([loader(), timeout]);
      sessionStorage.removeItem(RELOAD_FLAG);
      return mod;
    } catch (error) {
      if (!sessionStorage.getItem(RELOAD_FLAG)) {
        sessionStorage.setItem(RELOAD_FLAG, '1');
        window.location.reload();
      }
      throw error;
    } finally {
      window.clearTimeout(timer);
    }
  };
}

// 递归包装 modules.ts 里的懒加载页面（component 都是 () => import(...)）。
function withLazyGuard(routes: RouteRecordRaw[]): RouteRecordRaw[] {
  return routes.map((route) => {
    const copy = { ...route } as RouteRecordRaw & { component?: unknown };
    if (typeof copy.component === 'function') {
      copy.component = lazyPage(copy.component as () => Promise<unknown>);
    }
    if (copy.children) copy.children = withLazyGuard(copy.children);
    return copy;
  });
}

const routes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'login',
    component: lazyPage(() => import('@/views/LoginView.vue')) as never,
    meta: { public: true, title: '登录' },
  },
  {
    path: '/',
    component: lazyPage(() => import('@/layouts/AdminLayout.vue')) as never,
    redirect: '/dashboard',
    children: withLazyGuard(adminRoutes),
  },
  {
    // 兜底：未匹配到的路径回工作台，而不是白屏
    path: '/:pathMatch(.*)*',
    redirect: '/dashboard',
  },
];

const router = createRouter({
  // hash 模式：后台是纯静态托管，history 模式需要服务端把任意路径回退到 index.html。
  // Nginx 没配 rewrite 时 history 模式会直接 404。
  history: createWebHashHistory(),
  routes,
  scrollBehavior: () => ({ top: 0 }),
});

router.beforeEach((to) => {
  if (to.meta?.public) return true;
  if (isLoggedIn()) return true;
  // 未登录一律回登录页，并带上原路径，登录后跳回
  return { path: '/login', query: { redirect: to.fullPath } };
});

router.afterEach((to) => {
  const title = to.meta?.title;
  document.title = title ? `${title} · SimpleShop` : 'SimpleShop 管理后台';
});

export default router;
