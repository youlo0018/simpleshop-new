import type { RouteLocationRaw } from 'vue-router';
import router from '@/router';

const TRANSFER_PREFIX = 'simpleshop:admin-page-state:';
const TRANSFER_TTL_MS = 10 * 60 * 1000;

interface TransferPayload {
  state: unknown;
  expiresAt: number;
}

function transferKey() {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `${Date.now()}-${Math.random().toString(36).slice(2)}`;
}

function cleanupExpiredTransfers() {
  const now = Date.now();
  for (let i = localStorage.length - 1; i >= 0; i -= 1) {
    const key = localStorage.key(i);
    if (!key?.startsWith(TRANSFER_PREFIX)) continue;
    try {
      const payload = JSON.parse(localStorage.getItem(key) || '') as TransferPayload;
      if (!payload.expiresAt || payload.expiresAt <= now) localStorage.removeItem(key);
    } catch {
      localStorage.removeItem(key);
    }
  }
}

function appendTransferKey(href: string, key: string) {
  const separator = href.includes('?') ? '&' : '?';
  return `${href}${separator}_nav=${encodeURIComponent(key)}`;
}

/**
 * 在独立浏览器标签中打开后台页面。
 *
 * 列表页必须留在原标签里，才能保住关键词、页码与筛选条件。需要随页面
 * 一起带入的编辑行数据通过短期 localStorage 传输，因为新标签无法继承
 * vue-router 的内存 state。
 */
export function openAdminPage(to: RouteLocationRaw, options: { state?: unknown } = {}) {
  cleanupExpiredTransfers();

  let href = router.resolve(to).href;
  if (options.state !== undefined) {
    const key = transferKey();
    const payload: TransferPayload = {
      state: options.state,
      expiresAt: Date.now() + TRANSFER_TTL_MS,
    };
    localStorage.setItem(`${TRANSFER_PREFIX}${key}`, JSON.stringify(payload));
    href = appendTransferKey(href, key);
  }

  const url = new URL(`${window.location.pathname}${window.location.search}${href}`, window.location.origin).href;
  const opened = window.open(url, '_blank');
  if (!opened) {
    window.location.assign(url);
    return null;
  }
  opened.opener = null;
  return opened;
}

/** 读取新标签页携带的页面状态。数据会短暂保留，刷新编辑页也不会丢。 */
export function readAdminPageState(navKey: unknown) {
  const key = String(navKey || '').trim();
  if (!key) return undefined;
  const storageKey = `${TRANSFER_PREFIX}${key}`;
  try {
    const payload = JSON.parse(localStorage.getItem(storageKey) || '') as TransferPayload;
    if (!payload.expiresAt || payload.expiresAt <= Date.now()) {
      localStorage.removeItem(storageKey);
      return undefined;
    }
    return payload.state;
  } catch {
    localStorage.removeItem(storageKey);
    return undefined;
  }
}
