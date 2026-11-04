import { defineStore } from 'pinia';
import { request } from '@/core/http';
import type { CustomerProfile } from '@/core/types';
import {
  getProfile,
  getToken,
  setProfile,
  setToken,
} from '@/core/storage';
import { usePlatformStore } from './platform';

interface LoginResult {
  customerId: string;
  customerNo: string;
  token: string;
  customerName: string;
  nickName: string;
  avatar: string;
}

export const useSessionStore = defineStore('session', {
  state: () => ({
    token: '',
    profile: null as CustomerProfile | null,
  }),
  getters: {
    loggedIn: (state) => Boolean(state.token),
    displayName: (state) => state.profile?.nickName || state.profile?.customerName || '游客',
  },
  actions: {
    restore() {
      this.token = getToken();
      this.profile = getProfile();
    },
    async login(customerName: string, password: string) {
      const platform = usePlatformStore();
      const result = await request<LoginResult>('/gateway/customers/Login', {
        method: 'POST',
        auth: false,
        data: { customerName, password, platformCode: platform.platformCode },
      });
      this.token = result.token;
      this.profile = {
        customerId: result.customerId,
        customerNo: result.customerNo,
        customerName: result.customerName,
        nickName: result.nickName,
        avatar: result.avatar,
      };
      setToken(result.token, platform.platformCode);
      setProfile(this.profile, platform.platformCode);
    },
    async register(customerName: string, password: string, phone: string, nickName: string) {
      const platform = usePlatformStore();
      const result = await request<LoginResult>('/gateway/customers/Register', {
        method: 'POST',
        auth: false,
        data: {
          customerName,
          password,
          phone,
          nickName,
          platformCode: platform.platformCode,
        },
      });
      this.token = result.token;
      this.profile = {
        customerId: result.customerId,
        customerNo: result.customerNo,
        customerName: result.customerName,
        nickName: result.nickName,
        avatar: result.avatar,
      };
      setToken(result.token, platform.platformCode);
      setProfile(this.profile, platform.platformCode);
    },
    async refreshProfile() {
      if (!this.token || !this.profile) return;
      this.profile = await request<CustomerProfile>('/gateway/customers/Profile', {
        method: 'POST',
        data: { customerId: this.profile.customerId },
      });
      setProfile(this.profile);
    },
    async logout() {
      try {
        await request('/gateway/customers/Logout', { method: 'POST', data: {}, silent: true });
      } finally {
        this.token = '';
        this.profile = null;
        setToken('');
        setProfile(null);
      }
    },
  },
});
