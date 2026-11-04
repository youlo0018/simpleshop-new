export interface PlatformInfo {
  id?: string;
  platformId: string;
  platformName: string;
  mallName: string;
  platformCode: string;
  logo?: string;
  primaryColor?: string;
  tabColor?: string;
  backgroundColor?: string;
  shareTitle?: string;
  userAgreement?: string;
  privacyPolicy?: string;
  servicePhone?: string;
  icpNo?: string;
  mallIntro?: string;
}

export interface ProductItem {
  productId: string;
  merchantId?: string;
  spuName: string;
  subTitle: string;
  mainImage: string;
  brandName: string;
  categoryName: string;
  deliveryType: number;
  originalPrice: number;
  finalPrice: number;
  discountSource: string;
  discountSourceName: string;
  hasDiscount: boolean;
  sales: number;
  evaluationScore: number;
  evaluationCount: number;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface DesignComponentConfig {
  id?: string;
  type: string;
  span?: number;
  height?: number;
  props?: Record<string, any>;
}

export interface CustomerProfile {
  customerId: string;
  customerNo?: string;
  customerName: string;
  nickName: string;
  avatar: string;
  phone?: string;
  gender?: number;
  birthday?: string;
}
