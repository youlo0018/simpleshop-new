<template>
  <view class="address-form page-shell">
    <AppHeader :title="addressId ? '编辑地址' : '新增地址'" back />
    <view class="form card">
      <label><text>收货人</text><input v-model="form.consigneeName" placeholder="请输入收货人" /></label>
      <label><text>手机号</text><input v-model="form.consigneePhone" maxlength="20" placeholder="请输入手机号" /></label>
      <label><text>所在地区</text><input v-model="form.regionPath" placeholder="省 / 市 / 区" @click="chooseRegion" /></label>
      <label><text>详细地址</text><textarea v-model="form.detailAddress" maxlength="255" placeholder="街道、门牌号等" /></label>
      <label><text>标签</text><input v-model="form.label" maxlength="16" placeholder="家 / 公司 / 自定义" /></label>
      <view class="default-row"><text>设为默认地址</text><switch :checked="form.isDefault" color="#0071e3" @change="setDefault" /></view>
    </view>
    <button class="btn-primary" :disabled="saving" @click="save">保存</button>
  </view>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue';
import { onLoad } from '@dcloudio/uni-app';
import AppHeader from '@/components/AppHeader.vue';
import { request } from '@/core/http';
import { useSessionStore } from '@/stores/session';

const session = useSessionStore();
const addressId = ref('');
const saving = ref(false);
const form = reactive({
  consigneeName: '',
  consigneePhone: '',
  provinceCode: '',
  cityCode: '',
  districtCode: '',
  regionPath: '',
  detailAddress: '',
  label: '',
  isDefault: false,
});

async function load() {
  session.restore();
  if (!addressId.value) {
    form.consigneeName = session.profile?.nickName || '';
    return;
  }
  const result = await request<any>('/gateway/customers/addresses/List', {
    method: 'POST',
    data: { customerId: session.profile?.customerId, page: 1, pageSize: 100 },
  });
  const item = (result.items || []).find((row: any) => String(row.addressId) === addressId.value);
  if (item) Object.assign(form, {
    consigneeName: item.consigneeName,
    consigneePhone: item.consigneePhone,
    provinceCode: item.provinceCode,
    cityCode: item.cityCode,
    districtCode: item.districtCode,
    regionPath: item.regionPath,
    detailAddress: item.detailAddress,
    label: item.label,
    isDefault: item.isDefault,
  });
}

function chooseRegion() {
  uni.showToast({ title: '请按“省/市/区”填写地区', icon: 'none' });
}

function setDefault(event: any) {
  form.isDefault = Boolean(event.detail.value);
}

async function save() {
  if (!form.consigneeName || !form.consigneePhone || !form.regionPath || !form.detailAddress) {
    uni.showToast({ title: '请完整填写地址', icon: 'none' });
    return;
  }
  saving.value = true;
  try {
    const path = form.regionPath.split('/').map((item) => item.trim());
    const body = {
      ...form,
      customerId: session.profile!.customerId,
      provinceCode: path[0] || form.regionPath,
      cityCode: path[1] || form.regionPath,
      districtCode: path[2] || form.regionPath,
    };
    if (addressId.value) {
      await request('/gateway/customers/addresses/Update', {
        method: 'POST',
        data: { ...body, addressId: addressId.value },
      });
    } else {
      await request('/gateway/customers/addresses/Create', { method: 'POST', data: body });
    }
    uni.showToast({ title: '已保存', icon: 'success' });
    setTimeout(() => uni.navigateBack(), 350);
  } finally {
    saving.value = false;
  }
}

onLoad((query) => {
  addressId.value = String(query?.id || '');
  load();
});
</script>

<style scoped lang="scss">
@use '@/styles/tokens.scss' as *;

.form {
  padding: 24rpx;
  margin: 20rpx 0 28rpx;
}

.form label {
  display: block;
  padding: 12rpx 0 22rpx;
}

.form label > text {
  display: block;
  margin-bottom: 10rpx;
  color: $muted;
  font-size: 23rpx;
}

.form input,
.form textarea {
  width: 100%;
  padding: 0 18rpx;
  border-radius: 12rpx;
  background: #f5f6f8;
  font-size: 26rpx;
}

.form input {
  height: 82rpx;
}

.form textarea {
  height: 150rpx;
  padding-top: 18rpx;
}

.default-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 80rpx;
  border-top: 1rpx solid $line;
  font-size: 26rpx;
}
</style>
