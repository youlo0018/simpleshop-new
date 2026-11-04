import type { CustomerProfile, PlatformInfo } from './types';

const LAST_PLATFORM = 'simpleshop:last-platform';
const SEARCH_HISTORY = 'simpleshop:search-history:';
const SESSION = 'simpleshop:session:';

export function getLastPlatformCode() {
  return String(uni.getStorageSync(LAST_PLATFORM) || '');
}

export function setLastPlatformCode(code: string) {
  uni.setStorageSync(LAST_PLATFORM, code);
}

export function getToken(platformCode?: string) {
  const code = platformCode || getLastPlatformCode();
  return String(uni.getStorageSync(`${SESSION}${code}:token`) || '');
}

export function setToken(token: string, platformCode?: string) {
  const key = `${SESSION}${platformCode || getLastPlatformCode()}:token`;
  if (token) uni.setStorageSync(key, token);
  else uni.removeStorageSync(key);
}

export function getProfile(platformCode?: string): CustomerProfile | null {
  const code = platformCode || getLastPlatformCode();
  return (uni.getStorageSync(`${SESSION}${code}:profile`) as CustomerProfile) || null;
}

export function setProfile(profile: CustomerProfile | null, platformCode?: string) {
  const key = `${SESSION}${platformCode || getLastPlatformCode()}:profile`;
  if (profile) uni.setStorageSync(key, profile);
  else uni.removeStorageSync(key);
}

export function getSearchHistory(platformCode: string): string[] {
  const value = uni.getStorageSync(`${SEARCH_HISTORY}${platformCode}`) as string[] | string;
  if (Array.isArray(value)) return value;
  if (!value) return [];
  try { return JSON.parse(String(value)) as string[]; } catch { return []; }
}

export function setSearchHistory(platformCode: string, words: string[]) {
  uni.setStorageSync(`${SEARCH_HISTORY}${platformCode}`, words.slice(0, 10));
}

export function clearPlatformBusinessCache(platform: PlatformInfo) {
  const prefixes = [
    `simpleshop:design:${platform.platformCode}`,
    `simpleshop:regions:${platform.platformCode}`,
    `simpleshop:hotwords:${platform.platformCode}`,
    SEARCH_HISTORY,
  ];
  const info = uni.getStorageInfoSync();
  for (const key of info.keys) {
    if (prefixes.some((prefix) => key.startsWith(prefix))) uni.removeStorageSync(key);
  }
}
