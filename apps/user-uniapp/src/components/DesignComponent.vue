<template>
  <view
    v-if="shouldRender"
    class="design-block"
    :style="{ minHeight: component.height ? `${component.height}rpx` : undefined }"
  >
    <view v-if="component.type === 'banner'" class="banner card">
      <swiper v-if="images.length" class="banner__swiper" circular autoplay :interval="4000" indicator-dots>
        <swiper-item v-for="(image, index) in images" :key="index">
          <image class="banner__image" :src="image" mode="aspectFill" @click="openBanner(index)" />
        </swiper-item>
      </swiper>
      <view v-else class="banner__empty">
        <text class="banner__eyebrow">{{ platform.current?.mallName }}</text>
        <text class="banner__title">{{ component.props?.title || '发现今日好物' }}</text>
        <view class="banner__cta">
          <text>探索本季精选</text>
          <AppIcon type="arrow-right" :size="14" color="#163047" />
        </view>
        <text class="banner__serial">NEW SEASON / 2026</text>
      </view>
    </view>

    <button v-else-if="component.type === 'searchBar'" class="search-pill" @click="search">
      <AppIcon class="search-pill__icon" type="search" :size="18" color="#8a8a8f" />
      <text>{{ component.props?.placeholder || '搜索商品' }}</text>
    </button>

    <view v-else-if="component.type === 'title'" class="section-head">
      <text class="section-title">{{ component.props?.title || '精选推荐' }}</text>
      <text v-if="component.props?.subtitle" class="section-more">{{ component.props.subtitle }}</text>
    </view>

    <view v-else-if="component.type === 'notice'" class="notice card">
      <view class="notice__tag">
        <AppIcon type="notification" :size="15" color="#0071e3" />
        <text>公告</text>
      </view>
      <swiper class="notice__swiper" vertical autoplay circular :interval="3500">
        <swiper-item v-for="item in displayNotices" :key="item.id || item.content">
          <text class="notice__text">{{ item.content }}</text>
        </swiper-item>
      </swiper>
    </view>

    <view v-else-if="component.type === 'kingKong' || component.type === 'categoryNav' || component.type === 'serviceGrid'" class="kingkong card">
      <button
        v-for="(item, index) in items"
        :key="index"
        class="kingkong__item"
        @click="jump(item)"
      >
        <view class="kingkong__icon">
          <image v-if="item.icon" class="kingkong__image" :src="item.icon" mode="aspectFit" />
          <AppIcon v-else :type="resolveEntryIcon(item, index)" :size="22" :color="entryColor(index)" />
        </view>
        <text class="kingkong__label">{{ item.name || item.title || `入口${index + 1}` }}</text>
      </button>
    </view>

    <view v-else-if="component.type === 'productGrid' || component.type === 'productScroll'">
      <view class="section-head">
        <text class="section-title">{{ component.props?.title || '为你推荐' }}</text>
        <button v-if="component.props?.showMore !== false" class="section-more section-more--icon" @click="openList">
          <text>查看更多</text>
          <AppIcon type="right" :size="13" color="#8a8a8f" />
        </button>
      </view>
      <scroll-view v-if="component.type === 'productScroll'" scroll-x class="product-scroll" :show-scrollbar="false">
        <view class="product-scroll__track">
          <view v-for="item in displayProducts" :key="item.productId" class="product-scroll__item">
            <ProductCard :product="item" />
          </view>
        </view>
      </scroll-view>
      <view v-else class="product-grid" :class="`product-grid--${columns}`">
        <ProductCard v-for="item in displayProducts" :key="item.productId" :product="item" />
      </view>
    </view>

    <view v-else-if="component.type === 'couponZone'" class="coupon-zone">
      <view class="section-head">
        <text class="section-title">{{ component.props?.title || '领券中心' }}</text>
        <button class="section-more section-more--icon" @click="go('/pages/coupon/center')">
          <text>更多</text>
          <AppIcon type="right" :size="13" color="#8a8a8f" />
        </button>
      </view>
      <scroll-view scroll-x class="coupon-zone__scroll">
        <view class="coupon-zone__track">
          <view v-for="coupon in displayCoupons" :key="coupon.activityId" class="coupon-mini">
            <AppIcon class="coupon-mini__icon" type="gift-filled" :size="17" color="#e87935" />
            <text class="coupon-mini__value">
              {{ coupon.couponType === 2 ? `${coupon.discountRate}折` : money(coupon.discountAmount) }}
            </text>
            <text class="coupon-mini__rule">{{ coupon.thresholdAmount > 0 ? `满${coupon.thresholdAmount}可用` : '无门槛' }}</text>
          </view>
        </view>
      </scroll-view>
    </view>

    <view v-else-if="component.type === 'activityZone'" class="activity-zone">
      <view class="section-head">
        <text class="section-title">{{ component.props?.title || '限时活动' }}</text>
      </view>
      <button v-for="item in displayActivities" :key="item.activityId" class="activity-card card" @click="openActivity(item)">
        <image v-if="item.coverImage" class="activity-card__image" :src="item.coverImage" mode="aspectFill" />
        <view v-else class="activity-card__image activity-card__image--empty">活动</view>
        <view class="activity-card__body">
          <text class="activity-card__title">{{ item.activityName }}</text>
          <text class="activity-card__desc">{{ item.description || '限时优惠进行中' }}</text>
          <text class="activity-card__time">{{ shortTime(item.startTime) }} - {{ shortTime(item.endTime) }}</text>
        </view>
      </button>
    </view>

    <view v-else-if="component.type === 'seckillZone'" class="feature-band card" @click="go('/pages/seckill/index')">
      <view class="feature-band__lead">
        <view class="feature-band__icon">
          <AppIcon type="fire-filled" :size="26" color="#ff7a00" />
        </view>
        <view>
        <text class="feature-band__eyebrow">限时抢购</text>
        <text class="feature-band__title">好价正在发生</text>
        </view>
      </view>
      <view class="feature-band__arrow">
        <text>去看看</text>
        <AppIcon type="right" :size="13" color="#8a8a8f" />
      </view>
    </view>

    <view v-else-if="component.type === 'shopList'">
      <view class="section-head">
        <text class="section-title">{{ component.props?.title || '精选店铺' }}</text>
        <button class="section-more section-more--icon" @click="go('/pages/shop/list')">
          <text>全部</text>
          <AppIcon type="right" :size="13" color="#8a8a8f" />
        </button>
      </view>
      <button v-for="shop in displayShops" :key="shop.merchantId" class="shop-row card" @click="openShop(shop.merchantId)">
        <image v-if="shop.logo" class="shop-row__logo" :src="shop.logo" mode="aspectFill" />
        <view v-else class="shop-row__logo shop-row__logo--text">{{ shop.merchantName?.slice(0, 1) }}</view>
        <view class="shop-row__body">
          <text class="shop-row__name">{{ shop.merchantName }}</text>
          <text class="shop-row__desc">{{ shop.description || '精选好店' }}</text>
        </view>
        <view class="shop-row__score">
          <AppIcon type="star-filled" :size="13" color="#ff9f0a" />
          <text>{{ Number(shop.evaluationScore || 0).toFixed(1) }}</text>
        </view>
      </button>
    </view>

    <view v-else-if="component.type === 'shopActivity'">
      <view class="section-head"><text class="section-title">{{ component.props?.title || '店铺活动' }}</text></view>
      <button v-for="item in displayActivities" :key="item.activityId" class="activity-card card" @click="openActivity(item)">
        <image v-if="item.coverImage" class="activity-card__image" :src="item.coverImage" mode="aspectFill" />
        <view class="activity-card__body">
          <text class="activity-card__title">{{ item.activityName }}</text>
          <text class="activity-card__desc">{{ item.description || '本店优惠活动' }}</text>
          <text class="activity-card__time">{{ shortTime(item.startTime) }} - {{ shortTime(item.endTime) }}</text>
        </view>
      </button>
    </view>

    <view v-else-if="component.type === 'shopCategory'">
      <view class="section-head"><text class="section-title">{{ component.props?.title || '店铺分类' }}</text></view>
      <scroll-view scroll-x class="shop-categories" :show-scrollbar="false">
        <view class="shop-categories__track">
          <button
            v-for="category in categories"
            :key="category.categoryId"
            @click="jump({ linkType: 'category', linkValue: category.categoryId })"
          >{{ category.categoryName }}</button>
        </view>
      </scroll-view>
    </view>

    <view v-else-if="component.type === 'shopEvaluate'" class="shop-evaluate card">
      <view class="section-head">
        <text class="section-title">{{ component.props?.title || '店铺评价' }}</text>
        <button v-if="component.props?.showAll !== false" class="section-more section-more--icon" @click="openMerchantReviews">
          <text>全部评价</text>
          <AppIcon type="right" :size="13" color="#8a8a8f" />
        </button>
      </view>
      <view v-for="review in displayReviews" :key="review.evaluateId" class="shop-evaluate__row">
        <text>{{ review.customerName || '匿名用户' }} · {{ '★'.repeat(review.starScore) }}</text>
        <text>{{ review.content }}</text>
      </view>
    </view>

    <button v-else-if="component.type === 'imageText'" class="image-text card" @click="jump(component.props)">
      <image v-if="component.props?.image" class="image-text__image" :src="component.props.image" mode="aspectFill" />
      <view class="image-text__body">
        <text class="image-text__title">{{ component.props?.title || '图文广告' }}</text>
        <text class="image-text__desc">{{ component.props?.description || component.props?.subtitle }}</text>
      </view>
    </button>

    <view v-else-if="component.type === 'divider'" class="divider" />
    <view v-else-if="component.type === 'spacer'" class="spacer" />
    <button v-else-if="component.type === 'cartFloat'" class="design-cart-float" aria-label="购物车" @click="go('/pages/cart/index')">
      <AppIcon type="cart-filled" :size="22" color="#ffffff" />
    </button>
  </view>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import type { DesignComponentConfig, ProductItem } from '@/core/types';
import { money } from '@/core/format';
import { usePlatformStore } from '@/stores/platform';
import ProductCard from './ProductCard.vue';
import { request } from '@/core/http';
import { useSessionStore } from '@/stores/session';
import AppIcon from './AppIcon.vue';

const props = defineProps<{
  component: DesignComponentConfig;
  products?: ProductItem[];
  coupons?: any[];
  activities?: any[];
  notices?: any[];
  shops?: any[];
  categories?: any[];
  reviews?: any[];
  merchantId?: string;
}>();

const platform = usePlatformStore();
const columns = computed(() => Number(props.component.props?.columns || 2) === 3 ? 3 : 2);
const session = useSessionStore();
const loadedProducts = ref<ProductItem[] | null>(null);
const displayProducts = computed(() => loadedProducts.value ?? props.products ?? []);
const coupons = computed(() => props.coupons || []);
const displayCoupons = computed(() => {
  const ids = String(props.component.props?.activityIds || '')
    .split(',').map((item) => item.trim()).filter(Boolean);
  const rows = ids.length ? coupons.value.filter((item) => ids.includes(String(item.activityId))) : coupons.value;
  return rows.slice(0, Number(props.component.props?.limit || 6));
});
const activities = computed(() => props.activities || []);
const displayActivities = computed(() => {
  const ids = String(props.component.props?.activityIds || '')
    .split(',').map((item) => item.trim()).filter(Boolean);
  const rows = ids.length ? activities.value.filter((item) => ids.includes(String(item.activityId))) : activities.value;
  return rows.slice(0, Number(props.component.props?.limit || 6));
});
const notices = computed(() => props.notices?.length ? props.notices : [{ content: '欢迎来到本商城，祝您购物愉快' }]);
const displayNotices = computed(() => {
  const ids = String(props.component.props?.noticeIds || '')
    .split(',').map((item) => item.trim()).filter(Boolean);
  if (!ids.length) return notices.value;
  return notices.value.filter((item) => ids.includes(String(item.id)));
});
const shops = computed(() => props.shops || []);
const displayShops = computed(() => shops.value.slice(0, Number(props.component.props?.limit || 6)));
const categories = computed(() => props.categories || []);
const reviews = computed(() => props.reviews || []);
const displayReviews = computed(() => reviews.value.slice(0, Number(props.component.props?.limit || 3)));
const images = computed(() => {
  const list = props.component.props?.items;
  const raw = props.component.props?.images
    || props.component.props?.banners
    || (Array.isArray(list) ? list.map((item: any) => item.image) : []);
  return Array.isArray(raw)
    ? raw.map((item: any) => typeof item === 'string' ? item : item.image).filter(Boolean)
    : [];
});
const items = computed(() => {
  if (props.component.type === 'categoryNav' && categories.value.length) {
    return categories.value.slice(0, Number(props.component.props?.limit || 8)).map((item: any) => ({
      name: item.categoryName,
      icon: item.icon,
      id: item.id,
      linkType: 'category',
      linkValue: item.id,
    }));
  }
  const raw = props.component.props?.items
    || props.component.props?.categories
    || props.component.props?.services
    || [];
  return Array.isArray(raw) ? raw : [];
});

const shouldRender = computed(() => {
  switch (props.component.type) {
    case 'searchBar':
      return false;
    case 'banner':
    case 'title':
    case 'divider':
    case 'spacer':
      return true;
    case 'kingKong':
    case 'serviceGrid':
      return items.value.length > 0;
    case 'categoryNav':
      return categories.value.length > 0;
    case 'productGrid':
    case 'productScroll':
      return displayProducts.value.length > 0;
    case 'couponZone':
      return displayCoupons.value.length > 0;
    case 'activityZone':
      return displayActivities.value.length > 0;
    case 'shopList':
      return displayShops.value.length > 0;
    case 'shopActivity':
      return displayActivities.value.length > 0;
    case 'shopCategory':
      return categories.value.length > 0;
    case 'shopEvaluate':
      return displayReviews.value.length > 0;
    case 'notice':
      return displayNotices.value.length > 0 || Boolean(props.component.props?.text);
    case 'imageText':
      return Boolean(props.component.props?.image || props.component.props?.title);
    case 'seckillZone':
    case 'cartFloat':
      return true;
    default:
      return false;
  }
});

async function loadComponentProducts() {
  if (!['productGrid', 'productScroll'].includes(props.component.type)) return;
  await platform.initialize();
  session.restore();
  const settings = props.component.props || {};
  const manualIds = Array.isArray(settings.manualProductIds)
    ? settings.manualProductIds.map(String)
    : [];
  const result = await request<{ items: ProductItem[] }>('/gateway/shop/products/List', {
    method: 'POST',
    auth: false,
    data: {
      customerId: session.profile?.customerId || '0',
      platformId: platform.platformId,
      merchantId: props.merchantId || '0',
      categoryId: settings.source === 'category' ? String(settings.categoryId || '0') : '0',
      brandId: settings.source === 'brand' ? String(settings.brandId || '0') : '0',
      sortBy: Number(settings.sortBy || 0),
      page: 1,
      pageSize: Math.min(50, Number(settings.limit || 10)),
    },
    silent: true,
  }).catch(() => null);
  if (!result) return;
  const rows = result.items || [];
  loadedProducts.value = settings.source === 'manual' && manualIds.length
    ? rows.filter((item) => manualIds.includes(String(item.productId)))
    : rows;
}

onMounted(loadComponentProducts);
watch(() => props.component.props, loadComponentProducts, { deep: true });

function go(url: string) {
  uni.navigateTo({ url });
}

function search() {
  go('/pages/search/index');
}

function openList() {
  uni.navigateTo({ url: '/pages/product/list' });
}

function openShop(id: string) {
  uni.navigateTo({ url: `/pages/shop/store?id=${id}` });
}

function openMerchantReviews() {
  if (!props.merchantId) return;
  uni.navigateTo({ url: `/pages/evaluate/list?merchantId=${props.merchantId}&title=${encodeURIComponent('店铺评价')}` });
}

function openActivity(item: any) {
  uni.navigateTo({ url: `/pages/activity/index?id=${item.activityId}` });
}

function openBanner(index: number) {
  const configured = props.component.props?.items;
  if (Array.isArray(configured) && configured[index]) jump(configured[index]);
}

function resolveEntryIcon(item: any, index: number) {
  const text = String(item.name || item.title || '').toLowerCase();
  if (text.includes('新品') || text.includes('推荐')) return 'fire';
  if (text.includes('券') || text.includes('赠')) return 'gift';
  if (text.includes('积分') || text.includes('会员')) return 'medal';
  if (text.includes('订单')) return 'list';
  if (text.includes('收藏')) return 'heart';
  if (text.includes('地址') || text.includes('门店')) return 'location';
  if (text.includes('数码') || text.includes('手机')) return 'phone';
  if (text.includes('美') || text.includes('妆')) return 'star';
  if (text.includes('食品') || text.includes('生鲜')) return 'shop';
  return ['home', 'shop', 'gift', 'heart', 'star'][index % 5];
}

function entryColor(index: number) {
  return ['#2f6bff', '#e87935', '#00a998', '#e94a65', '#7659d8'][index % 5];
}

function shortTime(value: string) {
  return value ? String(value).slice(0, 16).replace('T', ' ') : '';
}

function jump(item: any) {
  const type = item?.linkType || item?.type;
  const target = item?.linkValue || item?.path || item?.url;
  if (type === 'category' && target) {
    const merchant = props.merchantId ? `&merchantId=${props.merchantId}` : '';
    go(`/pages/product/list?categoryId=${target}${merchant}`);
  }
  else if (type === 'activity' && target) go(`/pages/activity/index?id=${target}`);
  else if (type === 'mall') go('/pages/mall/index');
  else if (type === 'shop' && target) openShop(target);
  else if (target) go(String(target));
}
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.design-block {
  margin-bottom: 24rpx;
}

.banner {
  overflow: hidden;
  height: 320rpx;
  border-radius: 24rpx;
}

.banner__swiper,
.banner__image {
  width: 100%;
  height: 100%;
}

.banner__empty {
  position: relative;
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  width: 100%;
  height: 100%;
  padding: 32rpx;
  background:
    radial-gradient(circle at 78% 18%, rgba(255, 255, 255, 0.88), transparent 32%),
    linear-gradient(135deg, #dcecff 0%, #f5f8ff 52%, #d8f1eb 100%);
}

.banner__eyebrow {
  color: #51708a;
  font-size: 24rpx;
}

.banner__title {
  margin-top: 10rpx;
  color: #102235;
  font-size: 46rpx;
  font-weight: 600;
  letter-spacing: 0;
}

.banner__cta {
  display: flex;
  align-items: center;
  align-self: flex-start;
  gap: 8rpx;
  margin-top: 22rpx;
  padding: 12rpx 20rpx;
  border: 1rpx solid rgba(22, 48, 71, 0.12);
  border-radius: 999rpx;
  background: rgba(255, 255, 255, 0.56);
  color: #163047;
  font-size: 22rpx;
}

.banner__serial {
  position: absolute;
  right: 28rpx;
  bottom: 30rpx;
  color: rgba(22, 48, 71, 0.38);
  font-size: 18rpx;
  letter-spacing: 1rpx;
}

.search-pill {
  display: flex;
  align-items: center;
  width: 100%;
  height: 76rpx;
  padding: 0 24rpx;
  gap: 12rpx;
  border: 0;
  border-radius: 999rpx;
  background: #fff;
  box-shadow: $shadow;
  color: $muted;
  font-size: 26rpx;
  text-align: left;
}

.search-pill__icon {
  font-size: 34rpx;
}

.notice {
  display: flex;
  align-items: center;
  height: 82rpx;
  padding: 0 20rpx;
  gap: 16rpx;
}

.notice__tag {
  display: flex;
  align-items: center;
  gap: 4rpx;
  padding: 4rpx 10rpx;
  border-radius: 8rpx;
  background: #eef4ff;
  color: $brand;
  font-size: 22rpx;
}

.notice__swiper {
  flex: 1;
  height: 60rpx;
}

.notice__text {
  display: flex;
  align-items: center;
  height: 60rpx;
  overflow: hidden;
  color: #4d4d52;
  font-size: 24rpx;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.kingkong {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 26rpx 8rpx;
  padding: 24rpx 16rpx;
  border-radius: 20rpx;
}

.kingkong__item {
  display: flex;
  align-items: center;
  flex-direction: column;
  gap: 10rpx;
  min-width: 0;
  padding: 0;
  border: 0;
  background: transparent;
}

.kingkong__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 72rpx;
  height: 72rpx;
  border-radius: 22rpx;
  background: #f2f5f9;
  color: $brand;
  font-size: 28rpx;
  font-weight: 600;
}

.kingkong__image {
  width: 52rpx;
  height: 52rpx;
}

.kingkong__label {
  max-width: 100%;
  overflow: hidden;
  font-size: 23rpx;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.product-grid {
  display: grid;
  gap: 20rpx;
}

.product-grid--2 {
  grid-template-columns: repeat(2, minmax(0, 1fr));
}

.product-grid--3 {
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.product-scroll {
  width: 100%;
}

.product-scroll__track {
  display: flex;
  gap: 20rpx;
  padding: 2rpx 2rpx 20rpx;
}

.product-scroll__item {
  width: 280rpx;
  flex: 0 0 280rpx;
}

.coupon-zone__scroll {
  width: 100%;
}

.coupon-zone__track {
  display: flex;
  gap: 16rpx;
}

.coupon-mini {
  position: relative;
  display: flex;
  flex-direction: column;
  justify-content: center;
  width: 240rpx;
  min-height: 132rpx;
  flex: 0 0 240rpx;
  padding: 20rpx;
  border-radius: 16rpx;
  background: linear-gradient(135deg, #fff4f0, #fff);
}

.coupon-mini__icon {
  position: absolute;
  top: 14rpx;
  right: 14rpx;
}

.coupon-mini__value {
  color: $price;
  font-size: 38rpx;
  font-weight: 600;
}

.coupon-mini__rule {
  color: $muted;
  font-size: 22rpx;
}

.activity-card {
  display: flex;
  width: 100%;
  padding: 0;
  overflow: hidden;
  margin-bottom: 16rpx;
  border: 0;
  text-align: left;
}

.activity-card__image {
  width: 220rpx;
  height: 160rpx;
  flex: 0 0 220rpx;
}

.activity-card__image--empty {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f1f2f4;
  color: $tertiary;
}

.activity-card__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 8rpx;
  padding: 20rpx;
}

.activity-card__title {
  font-size: 30rpx;
  font-weight: 600;
}

.activity-card__desc,
.activity-card__time {
  color: $muted;
  font-size: 22rpx;
}

.feature-band {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 30rpx;
  background: linear-gradient(135deg, #fff7e8, #fff);
}

.feature-band__lead,
.feature-band__arrow {
  display: flex;
  align-items: center;
}

.feature-band__lead {
  gap: 18rpx;
}

.feature-band__icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 74rpx;
  height: 74rpx;
  border-radius: 22rpx;
  background: #fff0db;
}

.feature-band__eyebrow,
.feature-band__title {
  display: block;
}

.feature-band__eyebrow {
  color: $warning;
  font-size: 22rpx;
}

.feature-band__title {
  margin-top: 6rpx;
  font-size: 34rpx;
  font-weight: 600;
}

.feature-band__arrow {
  gap: 2rpx;
  color: $muted;
  font-size: 24rpx;
}

.shop-row {
  display: flex;
  align-items: center;
  width: 100%;
  gap: 18rpx;
  padding: 20rpx;
  margin-bottom: 16rpx;
  border: 0;
  text-align: left;
}

.shop-row__logo {
  width: 88rpx;
  height: 88rpx;
  flex: 0 0 88rpx;
  border-radius: 18rpx;
}

.shop-row__logo--text {
  display: flex;
  align-items: center;
  justify-content: center;
  background: #f1f2f4;
  color: $brand;
  font-size: 30rpx;
}

.shop-row__body {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 6rpx;
}

.shop-row__name {
  font-size: 28rpx;
  font-weight: 500;
}

.shop-row__desc,
.shop-row__score {
  color: $muted;
  font-size: 22rpx;
}

.shop-row__score {
  display: flex;
  align-items: center;
  gap: 4rpx;
}

.shop-categories {
  width: 100%;
}

.shop-categories__track {
  display: flex;
  gap: 12rpx;
}

.shop-categories__track button {
  height: 58rpx;
  flex: 0 0 auto;
  padding: 0 22rpx;
  border: 0;
  border-radius: 999rpx;
  background: #fff;
  color: $muted;
  font-size: 22rpx;
}

.shop-evaluate {
  padding: 0 24rpx 20rpx;
}

.shop-evaluate__row {
  display: flex;
  flex-direction: column;
  gap: 8rpx;
  padding: 16rpx 0;
  border-top: 1rpx solid $line;
  font-size: 24rpx;
}

.shop-evaluate__row text:first-child {
  color: $muted;
  font-size: 21rpx;
}

.image-text {
  display: flex;
  width: 100%;
  overflow: hidden;
  padding: 0;
  border: 0;
  text-align: left;
}

.image-text__image {
  width: 240rpx;
  height: 180rpx;
  flex: 0 0 240rpx;
}

.image-text__body {
  display: flex;
  flex-direction: column;
  gap: 10rpx;
  padding: 24rpx;
}

.image-text__title {
  font-size: 30rpx;
  font-weight: 600;
}

.image-text__desc {
  color: $muted;
  font-size: 23rpx;
}

.divider {
  height: 1rpx;
  margin: 12rpx 0;
  background: $line;
}

.spacer {
  height: 40rpx;
}

.design-cart-float {
  position: fixed;
  right: 26rpx;
  bottom: calc(126rpx + env(safe-area-inset-bottom));
  z-index: 40;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 88rpx;
  height: 88rpx;
  padding: 0;
  border: 0;
  border-radius: 999rpx;
  background: #171719;
  box-shadow: $shadow-float;
}
</style>
