# SimpleShop

SimpleShop 是一个多平台微服务电商系统，覆盖：

> 注册 → 浏览 → 加购 → 下单 → 模拟支付 → 发货 / 备货 → 签收 / 取货 → 评价 → 退款

系统按平台、商户、客户三层隔离。后端使用 .NET 10，管理后台使用 Vue 3 + Element Plus，
商城端使用 uni-app（Vue 3 + TypeScript），同时支持 H5 与微信小程序构建。

## 当前交付

| 模块 | 状态 |
|---|---|
| 后端 | 16 个 HTTP 服务 + ScheduledService，全部可启动 |
| 管理后台 | 平台 / 商户 / 商品 / 交易 / 营销 / 装修 / 搜索索引等页面 |
| 商城端 | 36 个路由页面，支持平台切换、账号密码登录、模拟支付 |
| 商品搜索 | Elasticsearch + IK；同义词在查询层展开，保存后即时生效 |
| 基础设施 | PostgreSQL、Redis、Consul、RabbitMQ、AgileConfig、ES、Kibana、Fluentd |

## 本地地址

| 服务 | 地址 |
|---|---|
| 管理后台 | `http://127.0.0.1:5173` |
| 商城 H5 | `http://127.0.0.1:5175` |
| 网关 | `http://127.0.0.1:5008` |
| AgileConfig | `http://127.0.0.1:5000/ui` |

演示环境：

| 项 | 值 |
|---|---|
| 平台编码 | `QHRQDB` |
| 后台超管 | `codexadmin` / `Admin123456` |
| 商城客户 | `youlo0018` / `Aa123456..` |

AgileConfig 与内部令牌凭据位于 `deploy/.env`，不进入版本库。

## 启动

Linux Mint / Ubuntu：

```bash
cd deploy/linux
chmod +x ./*.sh
./build.sh --fresh
./start-all.sh full
./status.sh
```

Windows 可使用 `scripts/*.ps1`。中间件、数据库初始化和低内存启动说明见
[`deploy/linux/README.md`](deploy/linux/README.md)。

前端单独构建：

```bash
cd apps/admin-vue
npm run build

cd ../user-uniapp
npm run type-check
npm run build:h5
npm run build:mp-weixin
```

当前仓库路径包含 `#` 时，Vite 生产构建需在无 `#` 的临时目录执行。

## 验证

```bash
dotnet test
tests/e2e/run.sh
pwsh ./tests/ui/run-all.ps1
```

最近验证结果：

- 单元测试 `404 / 404` 通过。
- ProductService 构建 `0 warning / 0 error`。
- 管理后台 `vue-tsc + vite build` 通过。
- 商城 H5 构建通过，36 个路由均可在 `390×844` 视口打开。
- 搜索同义词支持全局、平台、分类、品牌作用域及四种规则类型。

## 项目结构

| 路径 | 内容 |
|---|---|
| `src/` | .NET 微服务与 `Collaboration` 公共库 |
| `apps/admin-vue/` | 管理后台 |
| `apps/user-uniapp/` | 商城 H5 / 微信小程序 |
| `deploy/sql/` | PostgreSQL 建表与种子脚本 |
| `deploy/linux/` | Linux 构建与启停脚本 |
| `tests/e2e/` | API 回归 |
| `tests/ui/` | 管理后台 UI 回归 |
| `scripts/` | Windows 初始化、构建与演示数据脚本 |

## 文档

| 文档 | 用途 |
|---|---|
| [`BUSINESS.md`](BUSINESS.md) | 业务规则、权限、状态机、营销、积分、评价 |
| [`DATA_SPEC.md`](DATA_SPEC.md) | 数据模型、FreeSql、字段与建表约定 |
| [`CODING_STANDARD.md`](CODING_STANDARD.md) | 分层、校验、注释和已知实现陷阱 |
| [`DESIGN_SPEC.md`](DESIGN_SPEC.md) | 管理后台视觉 token 与硬约束 |
| [`FRONTEND_DESIGN.md`](FRONTEND_DESIGN.md) | 管理后台页面与交互 |
| [`TEST_CASES.md`](TEST_CASES.md) | 单元、API、UI、视觉与越权测试范围 |
| [`REVIEW.md`](REVIEW.md) | 关键链路风险和审计结果 |
| [`AI_HANDOFF.md`](AI_HANDOFF.md) | 当前状态、协作约束和交接入口 |
| [`docs/miniprogram-requirements.md`](docs/miniprogram-requirements.md) | 商城端逐页需求与业务决策 |
| [`docs/miniprogram-visual-spec.md`](docs/miniprogram-visual-spec.md) | 商城端视觉规范 |
| [`docs/search-synonyms.md`](docs/search-synonyms.md) | 商品搜索同义词设计与后台操作 |

## 关键约束

- 雪花 Id 前端按字符串处理，禁止 `Number(id)`。
- 金额统一使用两位小数和既有格式化函数。
- 表结构由 `deploy/sql/` 管理，不使用 FreeSql CodeFirst。
- `/internal/**` 必须携带内部令牌。
- ES 只负责召回；价格、上下架和库存以数据库为准。
- 新增网关接口必须同步权限路径，否则会 403 或绕过鉴权。
