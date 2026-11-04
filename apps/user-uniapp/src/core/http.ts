import { getToken } from './storage';

const API_BASE = String(import.meta.env.VITE_API_BASE || '');

export interface RequestOptions {
  method?: 'GET' | 'POST' | 'PUT' | 'DELETE';
  data?: Record<string, unknown> | unknown[];
  params?: Record<string, unknown>;
  auth?: boolean;
  silent?: boolean;
}

interface Envelope<T> {
  success: boolean;
  code: number;
  message: string;
  data: T;
  errors?: Record<string, string[]>;
}

function messageOf(payload: Envelope<unknown> | null, fallback: string) {
  if (payload?.errors) {
    const first = Object.values(payload.errors).flat().find(Boolean);
    if (first) return first;
  }
  return payload?.message || fallback;
}

export function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const query = options.params
    ? Object.entries(options.params)
        .filter(([, value]) => value !== undefined && value !== null && value !== '')
        .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(String(value))}`)
        .join('&')
    : '';
  const token = options.auth === false ? '' : getToken();

  return new Promise<T>((resolve, reject) => {
    uni.request({
      url: `${API_BASE}${path}${query ? `?${query}` : ''}`,
      method: options.method || 'GET',
      data: options.data,
      header: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      success(response) {
        const payload = response.data as Envelope<T> | null;
        if (!payload || payload.success !== true) {
          const message = messageOf(payload, `请求失败（${response.statusCode}）`);
          if (!options.silent) uni.showToast({ title: message, icon: 'none' });
          reject(new Error(message));
          return;
        }
        resolve(payload.data);
      },
      fail(error) {
        const message = error.errMsg || '网络连接失败';
        if (!options.silent) uni.showToast({ title: message, icon: 'none' });
        reject(new Error(message));
      },
    });
  });
}

export function uploadImage(filePath: string): Promise<string> {
  const token = getToken();
  return new Promise((resolve, reject) => {
    uni.uploadFile({
      url: `${API_BASE}/gateway/files/Upload`,
      filePath,
      name: 'file',
      header: token ? { Authorization: `Bearer ${token}` } : {},
      success(response) {
        try {
          const payload = JSON.parse(String(response.data)) as Envelope<{ publicUrl: string }>;
          if (payload.success && payload.data?.publicUrl) resolve(payload.data.publicUrl);
          else reject(new Error(messageOf(payload, '图片上传失败')));
        } catch {
          reject(new Error('上传响应格式不正确'));
        }
      },
      fail(error) {
        reject(new Error(error.errMsg || '图片上传失败'));
      },
    });
  });
}
