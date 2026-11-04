#!/usr/bin/env python3
"""Import public DummyJSON demo products into SimpleShop for local testing.

The source is DummyJSON's public demo commerce API. Product titles, prices,
stock, ratings, descriptions and images are fetched from the source. Merchant
names and category labels are localized demo labels around those products.

The script is idempotent:
- categories are matched by their stable demo category code;
- merchants are matched by exact name;
- products are matched by exact SPU name + merchant.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from decimal import Decimal, ROUND_HALF_UP
from typing import Any


SOURCE_URL = (
    "https://dummyjson.com/products"
    "?limit=100&skip=0"
    "&select=id,title,description,category,price,discountPercentage,"
    "rating,stock,brand,thumbnail,images"
)

TITLE_MAP = {
    1: "Essence 羽感浓密睫毛膏",
    2: "Glamour Beauty 镜面九色眼影盘",
    3: "Velvet Touch 柔焦定妆散粉",
    4: "Chic Cosmetics 丝绒正红唇膏",
    5: "Nail Couture 亮泽车厘子指甲油",
    6: "Calvin Klein CK One 中性淡香水",
    7: "Chanel Coco Noir 黑可可女士香水",
    8: "Dior J'adore 真我淡香精",
    9: "Dolce & Gabbana 浅蓝女士淡香水",
    10: "Gucci Bloom 花悦绽放香水",
    11: "Annibale Colombo 意式软包双人床",
    12: "Annibale Colombo 意式真皮三人沙发",
    13: "Furniture Co. 非洲樱桃木床头柜",
    14: "Knoll Executive 会议椅",
    15: "Bath Trends 木质浴室柜镜组合",
    43: "原木秋千装饰",
    44: "家庭树相框",
    45: "现代房屋摆件绿植",
    46: "极简陶瓷花盆",
    47: "奶油风台灯",
    78: "Apple MacBook Pro 14 深空灰",
    79: "Asus Zenbook Pro 双屏笔记本",
    80: "Huawei MateBook X Pro 轻薄本",
    81: "Lenovo Yoga 920 触控笔记本",
    82: "Dell XPS 13 9300 笔记本",
    88: "Nike Air Jordan 1 红黑",
    89: "Nike 棒球钉鞋",
    90: "Puma Future Rider 复古休闲鞋",
    91: "Off White 红白运动鞋",
    92: "Off White 撞色运动鞋",
    93: "Fashion Timepieces 棕色皮带腕表",
    94: "Longines 名匠系列腕表",
    95: "Rolex Cellini Date 黑盘",
    96: "Rolex Cellini Moonphase",
    97: "Rolex Datejust 经典腕表",
    98: "Rolex Submariner 潜航者",
    99: "Amazon Echo Plus 智能音箱",
    100: "Apple AirPods 无线耳机",
}


@dataclass(frozen=True)
class MerchantSpec:
    name: str
    contact: str
    phone: str
    description: str
    service_guarantee: str
    delivery_note: str
    logo_product_id: int
    source_categories: tuple[str, ...]
    category_map: dict[str, str]


CATEGORY_SPECS = [
    ("美妆个护", "DEMO_CAT_BEAUTY", 0),
    ("彩妆香氛", "DEMO_CAT_BEAUTY_GROUP", "DEMO_CAT_BEAUTY"),
    ("彩妆", "DEMO_CAT_MAKEUP", "DEMO_CAT_BEAUTY_GROUP"),
    ("香水", "DEMO_CAT_FRAGRANCE", "DEMO_CAT_BEAUTY_GROUP"),
    ("家居生活", "DEMO_CAT_HOME", 0),
    ("家具装饰", "DEMO_CAT_HOME_GROUP", "DEMO_CAT_HOME"),
    ("家具", "DEMO_CAT_FURNITURE", "DEMO_CAT_HOME_GROUP"),
    ("家居饰品", "DEMO_CAT_DECOR", "DEMO_CAT_HOME_GROUP"),
    ("数码办公", "DEMO_CAT_DIGITAL", 0),
    ("电脑数码", "DEMO_CAT_DIGITAL_GROUP", "DEMO_CAT_DIGITAL"),
    ("笔记本电脑", "DEMO_CAT_LAPTOP", "DEMO_CAT_DIGITAL_GROUP"),
    ("智能设备", "DEMO_CAT_SMART", "DEMO_CAT_DIGITAL_GROUP"),
    ("品质配饰", "DEMO_CAT_ACCESSORY", 0),
    ("腕表鞋履", "DEMO_CAT_ACCESSORY_GROUP", "DEMO_CAT_ACCESSORY"),
    ("男士腕表", "DEMO_CAT_WATCH", "DEMO_CAT_ACCESSORY_GROUP"),
    ("男士鞋履", "DEMO_CAT_SHOES", "DEMO_CAT_ACCESSORY_GROUP"),
]

MERCHANTS = [
    MerchantSpec(
        name="澄光美研馆",
        contact="林知夏",
        phone="13800001001",
        description="专注彩妆、香氛与日常护理的精选集合店。",
        service_guarantee="正品保障 · 7 天无理由 · 快速退款",
        delivery_note="现货 48 小时内发出，支持全国配送",
        logo_product_id=1,
        source_categories=("beauty", "fragrances"),
        category_map={
            "beauty": "DEMO_CAT_MAKEUP",
            "fragrances": "DEMO_CAT_FRAGRANCE",
        },
    ),
    MerchantSpec(
        name="栖居生活选物",
        contact="周予安",
        phone="13800001002",
        description="把舒适、耐用和好设计带进日常家居。",
        service_guarantee="品质验收 · 送装咨询 · 售后无忧",
        delivery_note="大件商品预计 3-7 天送达，偏远地区以物流为准",
        logo_product_id=12,
        source_categories=("furniture", "home-decoration"),
        category_map={
            "furniture": "DEMO_CAT_FURNITURE",
            "home-decoration": "DEMO_CAT_DECOR",
        },
    ),
    MerchantSpec(
        name="极客与风度",
        contact="陈屿",
        phone="13800001003",
        description="数码装备、腕表与鞋履，兼顾性能和风格。",
        service_guarantee="官方渠道 · 成色保障 · 7 天售后",
        delivery_note="现货商品 24 小时内发出，节假日顺延",
        logo_product_id=78,
        source_categories=(
            "laptops",
            "mobile-accessories",
            "mens-watches",
            "mens-shoes",
        ),
        category_map={
            "laptops": "DEMO_CAT_LAPTOP",
            "mobile-accessories": "DEMO_CAT_SMART",
            "mens-watches": "DEMO_CAT_WATCH",
            "mens-shoes": "DEMO_CAT_SHOES",
        },
    ),
]


class ApiError(RuntimeError):
    """A readable API failure."""


class Client:
    """Small JSON client for the local gateway."""

    def __init__(self, base_url: str, token: str = "") -> None:
        self.base_url = base_url.rstrip("/")
        self.token = token

    def request(
        self,
        method: str,
        path: str,
        body: Any | None = None,
        query: dict[str, Any] | None = None,
        authenticated: bool = True,
    ) -> Any:
        url = f"{self.base_url}{path}"
        if query:
            clean = {k: v for k, v in query.items() if v is not None and v != ""}
            url += "?" + urllib.parse.urlencode(clean)

        headers = {"User-Agent": "SimpleShop-Demo-Importer/1.0"}
        payload = None
        if authenticated and self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        if body is not None:
            payload = json.dumps(body, ensure_ascii=False).encode("utf-8")
            headers["Content-Type"] = "application/json"

        request = urllib.request.Request(
            url, data=payload, headers=headers, method=method.upper()
        )
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                raw = response.read().decode("utf-8")
        except urllib.error.HTTPError as error:
            raw = error.read().decode("utf-8", errors="replace")
            raise ApiError(f"{method} {url} -> HTTP {error.code}: {raw}") from error
        except urllib.error.URLError as error:
            raise ApiError(f"{method} {url} -> {error}") from error

        try:
            envelope = json.loads(raw)
        except json.JSONDecodeError as error:
            raise ApiError(f"{method} {url} -> 非 JSON 响应: {raw[:300]}") from error

        if not envelope.get("success"):
            raise ApiError(
                f"{method} {url} -> {envelope.get('message')} "
                f"{json.dumps(envelope.get('errors') or {}, ensure_ascii=False)}"
            )
        return envelope.get("data")


def source_products() -> list[dict[str, Any]]:
    request = urllib.request.Request(
        SOURCE_URL, headers={"User-Agent": "SimpleShop-Demo-Importer/1.0"}
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        data = json.load(response)
    return [item for item in data["products"] if item["id"] in TITLE_MAP]


def login(base_url: str, user: str, password: str) -> str:
    body = urllib.parse.urlencode(
        {
            "grant_type": "password",
            "client_id": "admin-app",
            "username": user,
            "password": password,
        }
    ).encode()
    request = urllib.request.Request(
        f"{base_url.rstrip('/')}/gateway/auth/token",
        data=body,
        headers={"Content-Type": "application/x-www-form-urlencoded"},
        method="POST",
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)["access_token"]


def flatten_categories(nodes: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    for node in nodes:
        rows.append(node)
        rows.extend(flatten_categories(node.get("children") or []))
    return rows


def ensure_categories(client: Client, platform_id: int) -> dict[str, int]:
    tree = client.request("GET", "/gateway/categories/Tree", query={"includeDisabled": "true"})
    rows = flatten_categories(tree or [])
    by_code = {
        str(row.get("categoryCode")): int(row["id"])
        for row in rows
        if row.get("categoryCode")
    }
    created = 0
    for name, code, parent in CATEGORY_SPECS:
        if code in by_code:
            continue
        parent_id = 0 if parent == 0 else by_code[str(parent)]
        category_id = client.request(
            "POST",
            "/gateway/categories/Create",
            {
                "parentId": parent_id,
                "categoryName": name,
                "platformId": platform_id,
                "categoryCode": code,
                "sortOrder": created,
                "status": 1,
            },
        )
        by_code[code] = int(category_id)
        created += 1
    print(f"[categories] created={created}, total={len(by_code)}")
    return by_code


def find_merchant(client: Client, name: str) -> dict[str, Any] | None:
    page = client.request(
        "POST",
        "/gateway/merchants/List",
        {"keyword": name, "page": 1, "pageSize": 100},
    )
    for row in page.get("items") or []:
        if row.get("name") == name or row.get("merchantName") == name:
            return row
    return None


def ensure_merchant(
    client: Client, spec: MerchantSpec, platform_id: int, logo: str
) -> int:
    row = find_merchant(client, spec.name)
    if row is None:
        merchant_id = int(
            client.request(
                "POST",
                "/gateway/merchants/Create",
                {
                    "merchantName": spec.name,
                    "platformId": platform_id,
                    "contactName": spec.contact,
                    "contactPhone": spec.phone,
                    "logo": logo,
                    "description": spec.description,
                    "serviceGuarantee": spec.service_guarantee,
                    "deliveryNote": spec.delivery_note,
                },
            )
        )
        row = find_merchant(client, spec.name)
        if row is None:
            raise ApiError(f"创建商户后仍查不到：{spec.name}")
    else:
        merchant_id = int(row.get("id") or row.get("merchantId"))

    audit_status = int(row.get("auditStatus") or 0)
    status = int(row.get("status") or 0)
    if audit_status == 90:
        client.request(
            "POST", "/gateway/merchants/Resubmit", {"merchantId": merchant_id}
        )
        audit_status = 10
    if audit_status == 10:
        client.request(
            "POST",
            "/gateway/merchants/Audit",
            {
                "merchantId": merchant_id,
                "auditStatus": 20,
                "auditRemark": "公开演示数据，审核通过",
            },
        )
    if status != 1:
        client.request(
            "POST",
            "/gateway/merchants/ChangeStatus",
            {"merchantId": merchant_id, "status": 1},
        )
    print(f"[merchant] {spec.name} id={merchant_id}")
    return merchant_id


def find_product(client: Client, name: str, merchant_id: int) -> dict[str, Any] | None:
    rows = client.request(
        "GET",
        "/gateway/products/List",
        query={
            "keyword": name,
            "merchantId": merchant_id,
            "page": 1,
            "pageSize": 100,
        },
    )
    for row in rows or []:
        if row.get("spuName") == name:
            return row
    return None


def round_money(value: float) -> float:
    return float(Decimal(str(value)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP))


def ensure_product(
    client: Client,
    product: dict[str, Any],
    merchant_id: int,
    category_id: int,
    platform_id: int,
    sort_order: int,
) -> int:
    source_id = int(product["id"])
    name = TITLE_MAP[source_id]
    row = find_product(client, name, merchant_id)
    if row is not None:
        product_id = int(row.get("id") or row.get("productId"))
    else:
        price = round_money(float(product["price"]))
        discount = float(product.get("discountPercentage") or 0)
        divisor = max(0.01, 1 - discount / 100)
        original_price = max(price, round_money(price / divisor))
        images = list(product.get("images") or [])
        if product.get("thumbnail") and product["thumbnail"] not in images:
            images.insert(0, product["thumbnail"])
        images = images[:6]
        detail_images = list(product.get("images") or images)[:9]
        brand = str(product.get("brand") or "精选")
        product_id = int(
            client.request(
                "POST",
                "/gateway/products/Save",
                {
                    "productId": 0,
                    "spuName": name,
                    "subTitle": f"{brand} · 公开演示精选",
                    "categoryId": category_id,
                    "deliveryType": 1,
                    "mainImage": product.get("thumbnail") or (images[0] if images else ""),
                    "platformId": platform_id,
                    "merchantId": merchant_id,
                    "brandId": 0,
                    "images": json.dumps(images, ensure_ascii=False),
                    "detailImages": json.dumps(detail_images, ensure_ascii=False),
                    "originalPrice": original_price,
                    "description": (
                        f"{name}。该商品来自公开演示商品数据源，"
                        "可用于本地商城的下单、支付、退款等完整链路测试。"
                    ),
                    "status": 2,
                    "sortOrder": sort_order,
                    "remark": f"dummyjson:{source_id}",
                    "specs": [{"specName": "规格", "specValues": ["标准款"]}],
                    "skus": [
                        {
                            "skuCode": f"DJ-{source_id}",
                            "specValues": ["标准款"],
                            "price": price,
                            "originalPrice": original_price,
                            "stock": max(1, int(product.get("stock") or 1)),
                            "image": product.get("thumbnail") or "",
                            "status": 1,
                        }
                    ],
                },
            )
        )

    detail = client.request(
        "GET", "/gateway/products/Detail", query={"productId": product_id}
    )
    audit_status = int(detail.get("auditStatus") or 0)
    status = int(detail.get("status") or 0)
    if audit_status == 30:
        client.request(
            "POST", "/gateway/products/SubmitAudit", {"productId": product_id}
        )
        audit_status = 10
    if audit_status != 20:
        client.request(
            "POST",
            "/gateway/products/Audit",
            {"productId": product_id, "auditStatus": 20, "reason": "公开演示数据"},
        )
    if status != 1:
        client.request(
            "POST",
            "/gateway/products/ChangeListing",
            {"productId": product_id, "status": 1},
        )
    return product_id


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--base-url", default="http://127.0.0.1:5008")
    parser.add_argument("--platform-code", default="QHRQDB")
    parser.add_argument(
        "--admin-user", default=os.environ.get("SIMPLESHOP_ADMIN_USER", "codexadmin")
    )
    parser.add_argument(
        "--admin-password",
        default=os.environ.get("SIMPLESHOP_ADMIN_PASSWORD", "Admin123456"),
    )
    args = parser.parse_args()

    products = source_products()
    products_by_id = {int(item["id"]): item for item in products}
    token = login(args.base_url, args.admin_user, args.admin_password)
    client = Client(args.base_url, token)

    platform = client.request(
        "GET",
        "/gateway/platforms/PublicDetail",
        query={"platformCode": args.platform_code},
        authenticated=False,
    )
    platform_id = int(platform.get("id") or platform.get("platformId"))

    categories = ensure_categories(client, platform_id)
    total_products = 0
    for merchant_index, merchant in enumerate(MERCHANTS):
        logo_product = products_by_id[merchant.logo_product_id]
        logo = logo_product.get("thumbnail") or ""
        merchant_id = ensure_merchant(client, merchant, platform_id, logo)
        merchant_products = [
            item
            for item in products
            if item.get("category") in merchant.source_categories
        ]
        merchant_products.sort(key=lambda item: int(item["id"]))
        for product_index, product in enumerate(merchant_products):
            category_code = merchant.category_map[str(product["category"])]
            product_id = ensure_product(
                client,
                product,
                merchant_id,
                categories[category_code],
                platform_id,
                merchant_index * 100 + product_index,
            )
            total_products += 1
            print(
                f"[product] {TITLE_MAP[int(product['id'])]} "
                f"id={product_id} price={product['price']}"
            )

    print(
        json.dumps(
            {
                "ok": True,
                "platformCode": args.platform_code,
                "platformId": platform_id,
                "merchants": len(MERCHANTS),
                "products": total_products,
                "source": "https://dummyjson.com/products",
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except ApiError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        raise SystemExit(1)
