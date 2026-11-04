<template>
  <view class="search page-shell">
    <view class="search__bar">
      <view class="search__input">
        <AppIcon type="search" :size="18" color="#8a8a8f" />
        <input v-model="keyword" focus placeholder="搜索商品" confirm-type="search" @confirm="submit" @input="suggest" />
        <button v-if="keyword" aria-label="清空" @click="keyword = ''">
          <AppIcon type="closeempty" :size="16" color="#b8b8be" />
        </button>
      </view>
      <button class="search__cancel" @click="cancel">取消</button>
    </view>
    <view v-if="suggestions.length" class="suggest card">
      <button v-for="word in suggestions" :key="word" @click="choose(word)">{{ word }}</button>
    </view>
    <template v-else>
      <view v-if="history.length" class="section-head">
        <text class="section-title">搜索历史</text>
        <text class="section-more" @click="clearHistory">清空</text>
      </view>
      <view v-if="history.length" class="chips">
        <button v-for="word in history" :key="word" @click="choose(word)">{{ word }}</button>
      </view>
      <view class="section-head">
        <text class="section-title">热门搜索</text>
      </view>
      <view class="chips">
        <button v-for="item in hotWords" :key="item.id || item.keyword" @click="choose(item.keyword)">{{ item.keyword }}</button>
      </view>
    </template>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import { request } from '@/core/http';
import AppIcon from '@/components/AppIcon.vue';
import { getSearchHistory, setSearchHistory } from '@/core/storage';
import { usePlatformStore } from '@/stores/platform';

const platform = usePlatformStore();
const keyword = ref('');
const history = ref<string[]>([]);
const hotWords = ref<any[]>([]);
const suggestions = ref<string[]>([]);
let timer: ReturnType<typeof setTimeout> | null = null;

async function load() {
  await platform.initialize();
  history.value = getSearchHistory(platform.platformCode);
  hotWords.value = await request<any[]>('/gateway/shop/search/HotWords', {
    auth: false,
    params: { platformId: platform.platformId, limit: 20 },
    silent: true,
  }).catch(() => []);
}

function suggest() {
  suggestions.value = [];
  if (timer) clearTimeout(timer);
  if (!keyword.value.trim()) return;
  timer = setTimeout(async () => {
    suggestions.value = await request<string[]>('/gateway/shop/search/Suggest', {
      auth: false,
      params: { platformId: platform.platformId, keyword: keyword.value, limit: 10 },
      silent: true,
    }).catch(() => []);
  }, 300);
}

function choose(word: string) {
  keyword.value = word;
  submit();
}

function submit() {
  const value = keyword.value.trim();
  if (!value) return;
  history.value = [value, ...history.value.filter((word) => word !== value)].slice(0, 10);
  setSearchHistory(platform.platformCode, history.value);
  uni.navigateTo({ url: `/pages/product/list?keyword=${encodeURIComponent(value)}&title=${encodeURIComponent(value)}` });
}

function clearHistory() {
  history.value = [];
  setSearchHistory(platform.platformCode, []);
}

function cancel() {
  uni.navigateBack();
}

onLoad(load);
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.search {
  padding-top: calc(env(safe-area-inset-top) + 18rpx);
}

.search__bar {
  display: flex;
  align-items: center;
  gap: 16rpx;
}

.search__input {
  display: flex;
  align-items: center;
  flex: 1;
  height: 76rpx;
  padding: 0 20rpx;
  gap: 10rpx;
  border-radius: 999rpx;
  background: #fff;
  box-shadow: $shadow;
}

.search__input input {
  flex: 1;
  font-size: 26rpx;
}

.search__input button {
  padding: 10rpx;
  border: 0;
  background: transparent;
  color: $muted;
}

.search__cancel {
  padding: 20rpx 0;
  border: 0;
  background: transparent;
  color: $brand;
  font-size: 26rpx;
}

.suggest {
  margin-top: 18rpx;
  padding: 0 20rpx;
}

.suggest button {
  width: 100%;
  height: 82rpx;
  padding: 0;
  border: 0;
  border-top: 1rpx solid $line;
  background: transparent;
  font-size: 26rpx;
  text-align: left;
}

.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 14rpx;
}

.chips button {
  height: 62rpx;
  padding: 0 24rpx;
  border: 0;
  border-radius: 999rpx;
  background: #fff;
  font-size: 24rpx;
}
</style>
