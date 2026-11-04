<template>
  <view class="evaluate-form page-shell">
    <AppHeader :title="append ? '追评' : '发表评价'" back />
    <view v-if="!append" class="product card">
      <image v-if="evaluable?.mainImage" :src="evaluable.mainImage" mode="aspectFill" />
      <view>
        <text>{{ evaluable?.spuName }}</text>
        <text>{{ evaluable?.skuSpecs }}</text>
      </view>
    </view>
    <view v-if="!append" class="stars card">
      <text>商品评分</text>
      <view>
        <button v-for="star in 5" :key="star" :class="{ active: star <= starScore }" @click="starScore = star">★</button>
      </view>
    </view>
    <view class="content card">
      <textarea v-model="content" maxlength="1000" placeholder="分享你的使用感受..." />
      <text>{{ content.length }}/1000</text>
      <view class="images">
        <image v-for="(image, index) in images" :key="index" :src="image" mode="aspectFill" @click="remove(index)" />
        <button v-if="images.length < 9" aria-label="添加图片" @click="chooseImages">
          <AppIcon type="plusempty" :size="24" color="#8a8a8f" />
        </button>
      </view>
    </view>
    <view v-if="!append" class="anonymous card">
      <text>匿名评价</text>
      <switch :checked="anonymous" color="#0071e3" @change="setAnonymous" />
    </view>
    <button class="btn-primary" :disabled="submitting" @click="submit">提交</button>
  </view>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import AppIcon from '@/components/AppIcon.vue';
import { request, uploadImage } from '@/core/http';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const orderNo = ref('');
const spuId = ref('');
const evaluateId = ref('');
const append = ref(false);
const evaluable = ref<any>(null);
const starScore = ref(5);
const content = ref('');
const images = ref<string[]>([]);
const anonymous = ref(false);
const submitting = ref(false);

async function load() {
  session.restore();
  if (append.value) return;
  const items = await request<any[]>('/gateway/evaluates/Evaluable', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, orderNo: orderNo.value },
  });
  evaluable.value = items.find((item) => String(item.spuId) === spuId.value) || items[0];
  if (evaluable.value) spuId.value = String(evaluable.value.spuId);
}

async function chooseImages() {
  const paths = await new Promise<string[]>((resolve) => {
    uni.chooseImage({
      count: 9 - images.value.length,
      success: (result) => resolve(result.tempFilePaths as string[]),
      fail: () => resolve([]),
    });
  });
  for (const path of paths) {
    try { images.value.push(await uploadImage(path)); } catch { /* 可重试 */ }
  }
}

function setAnonymous(event: any) {
  anonymous.value = Boolean(event.detail.value);
}

function remove(index: number) {
  images.value.splice(index, 1);
}

async function submit() {
  if (!content.value.trim() && !images.value.length) {
    uni.showToast({ title: '请填写内容或上传图片', icon: 'none' });
    return;
  }
  submitting.value = true;
  try {
    if (append.value) {
      await request('/gateway/evaluates/Append', {
        method: 'POST',
        data: {
          customerId: session.profile!.customerId,
          evaluateId: evaluateId.value,
          starScore: 0,
          content: content.value,
          images: images.value,
        },
      });
    } else {
      await request('/gateway/evaluates/Publish', {
        method: 'POST',
        data: {
          customerId: session.profile!.customerId,
          orderNo: orderNo.value,
          spuId: spuId.value,
          starScore: starScore.value,
          content: content.value,
          images: images.value,
          isAnonymous: anonymous.value,
        },
      });
    }
    uni.showToast({ title: '已提交', icon: 'success' });
    setTimeout(() => uni.navigateBack(), 350);
  } finally {
    submitting.value = false;
  }
}

onLoad((query) => {
  orderNo.value = String(query?.orderNo || '');
  spuId.value = String(query?.spuId || '');
  evaluateId.value = String(query?.evaluateId || '');
  append.value = String(query?.append || '') === '1';
  load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.product {
  display: flex;
  align-items: center;
  gap: 18rpx;
  padding: 20rpx;
  margin-top: 20rpx;
}

.product image {
  width: 130rpx;
  height: 130rpx;
  border-radius: 14rpx;
}

.product view {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
}

.product view text:last-child {
  color: $muted;
  font-size: 22rpx;
}

.stars,
.anonymous {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 24rpx;
  margin-top: 20rpx;
}

.stars view {
  display: flex;
  gap: 8rpx;
}

.stars button {
  padding: 0;
  border: 0;
  background: transparent;
  color: #d6d7db;
  font-size: 46rpx;
}

.stars button.active {
  color: $warning;
}

.content {
  padding: 24rpx;
  margin-top: 20rpx;
}

.content textarea {
  width: 100%;
  height: 220rpx;
  font-size: 26rpx;
  line-height: 1.6;
}

.content > text {
  display: block;
  color: $muted;
  font-size: 21rpx;
  text-align: right;
}

.images {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12rpx;
  margin-top: 18rpx;
}

.images image,
.images button {
  width: 100%;
  aspect-ratio: 1;
  border-radius: 12rpx;
}

.images button {
  border: 1rpx dashed #c9c9ce;
  background: #fafafa;
  font-size: 38rpx;
}

.evaluate-form > .btn-primary {
  margin-top: 28rpx;
}
</style>
