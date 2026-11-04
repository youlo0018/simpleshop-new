<template>
  <view class="evaluates page-shell">
    <AppHeader :title="title || '商品评价'" back />
    <view class="summary card">
      <text class="summary__score">{{ Number(result.displayScore || 5).toFixed(1) }}</text>
      <view>
        <text>{{ stars(result.displayScore) }}</text>
        <text>共 {{ result.count || 0 }} 条评价</text>
      </view>
    </view>
    <view class="filters">
      <button v-for="filter in filters" :key="filter.label" :class="{ active: activeFilter === filter.key }" @click="apply(filter)">{{ filter.label }}</button>
    </view>
    <view v-for="item in result.items" :key="item.evaluateId" class="evaluate card">
      <view class="evaluate__head">
        <view class="avatar">{{ (item.customerName || '匿').slice(0, 1) }}</view>
        <view>
          <text>{{ item.customerName || '匿名用户' }}</text>
          <text>{{ item.skuSpecs }}</text>
        </view>
        <text class="evaluate__stars">{{ stars(item.starScore) }}</text>
      </view>
      <text class="evaluate__content">{{ item.content }}</text>
      <view v-if="item.images?.length" class="evaluate__images">
        <image v-for="(image, index) in item.images" :key="index" :src="image" mode="aspectFill" @click="preview(item.images, image)" />
      </view>
      <view v-for="reply in item.replies || []" :key="reply.replyId" class="reply">
        <text>{{ reply.replyTypeName }}</text>
        <text>{{ reply.content }}</text>
      </view>
      <view v-for="append in item.appends || []" :key="append.appendId" class="append">
        <text>追评</text>
        <text>{{ append.content }}</text>
      </view>
    </view>
    <EmptyState v-if="!loading && !result.items?.length" title="暂无评价" icon="chat" />
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad, onReachBottom, onPullDownRefresh } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import EmptyState from '@/components/EmptyState.vue';
import { request } from '@/core/http';

const spuId = ref('');
const merchantId = ref('');
const title = ref('');
const page = ref(1);
const loading = ref(false);
const result = ref<any>({ items: [] });
const activeFilter = ref('all');
const filters = [
  { key: 'all', label: '全部', hasImage: false, minStar: 0, maxStar: 0 },
  { key: 'image', label: '有图', hasImage: true, minStar: 0, maxStar: 0 },
  { key: 'good', label: '好评', hasImage: false, minStar: 4, maxStar: 5 },
  { key: 'middle', label: '中评', hasImage: false, minStar: 3, maxStar: 3 },
  { key: 'bad', label: '差评', hasImage: false, minStar: 1, maxStar: 2 },
];

async function load(reset = true) {
  if (reset) {
    page.value = 1;
    result.value = { items: [] };
  }
  loading.value = true;
  const filter = filters.find((item) => item.key === activeFilter.value)!;
  try {
    const data = merchantId.value
      ? await request<any>('/gateway/evaluates/Merchant', {
          method: 'POST',
          auth: false,
          data: { merchantId: merchantId.value, page: page.value, pageSize: 10 },
        })
      : await request<any>('/gateway/evaluates/List', {
          method: 'POST',
          auth: false,
          data: {
            spuId: spuId.value,
            page: page.value,
            pageSize: 10,
            hasImage: filter.hasImage,
            minStar: filter.minStar,
            maxStar: filter.maxStar,
          },
        });
    result.value = {
      ...data,
      items: reset ? data.items : [...(result.value.items || []), ...data.items],
    };
  } finally {
    loading.value = false;
    uni.stopPullDownRefresh();
  }
}

function apply(filter: any) {
  activeFilter.value = filter.key;
  load();
}

function stars(score: number) {
  const count = Math.round(Number(score || 0));
  return '★'.repeat(count) + '☆'.repeat(5 - count);
}

function preview(urls: string[], current: string) {
  uni.previewImage({ urls, current });
}

onLoad((query) => {
  spuId.value = String(query?.spuId || '');
  merchantId.value = String(query?.merchantId || '');
  title.value = String(query?.title || '');
  load();
});
onReachBottom(() => {
  if (loading.value || result.value.items?.length >= result.value.total) return;
  page.value += 1;
  load(false);
});
onPullDownRefresh(() => load());
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.summary {
  display: flex;
  align-items: center;
  gap: 22rpx;
  padding: 26rpx;
  margin-top: 20rpx;
}

.summary__score {
  font-size: 58rpx;
  font-weight: 600;
}

.summary view {
  display: flex;
  flex-direction: column;
  gap: 6rpx;
  color: $muted;
  font-size: 22rpx;
}

.summary view text:first-child {
  color: $warning;
  font-size: 26rpx;
}

.filters {
  display: flex;
  flex-wrap: wrap;
  gap: 12rpx;
  padding: 22rpx 0;
}

.filters button {
  height: 58rpx;
  padding: 0 22rpx;
  border: 0;
  border-radius: 999rpx;
  background: #fff;
  color: $muted;
  font-size: 22rpx;
}

.filters button.active {
  background: $selection-bg;
  color: $selection-text;
  font-weight: 500;
}

.evaluate {
  padding: 24rpx;
  margin-bottom: 18rpx;
}

.evaluate__head {
  display: flex;
  align-items: center;
  gap: 14rpx;
}

.avatar {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 64rpx;
  height: 64rpx;
  border-radius: 999rpx;
  background: #f0f3f7;
  color: $brand;
}

.evaluate__head view:nth-child(2) {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 4rpx;
  font-size: 24rpx;
}

.evaluate__head view:nth-child(2) text:last-child {
  color: $muted;
  font-size: 20rpx;
}

.evaluate__stars {
  color: $warning;
  font-size: 22rpx;
}

.evaluate__content {
  display: block;
  padding: 18rpx 0;
  font-size: 25rpx;
  line-height: 1.55;
}

.evaluate__images {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10rpx;
}

.evaluate__images image {
  width: 100%;
  aspect-ratio: 1;
  border-radius: 12rpx;
}

.reply,
.append {
  display: flex;
  flex-direction: column;
  gap: 5rpx;
  padding: 14rpx;
  margin-top: 12rpx;
  border-radius: 12rpx;
  background: #f6f7f9;
  font-size: 22rpx;
}

.reply text:first-child,
.append text:first-child {
  color: $brand;
  font-weight: 500;
}
</style>
