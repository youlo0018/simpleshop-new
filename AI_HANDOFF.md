# AI_HANDOFF — 当前状态与交接

> 新会话先读本文件，再按任务阅读对应规格。历史进度使用 `git log` 追溯，不在本文追加流水。

## 当前状态

截至 2026-11-04：

| 项 | 状态 |
|---|---|
| 后端 | 16 个 HTTP 服务 + ScheduledService，全部可启动 |
| 管理后台 | Vue 3 + TypeScript + Element Plus |
| 商城端 | uni-app，36 个路由页面，支持 H5 与微信小程序构建 |
| 基础设施 | PostgreSQL、Redis、Consul、RabbitMQ、AgileConfig、Elasticsearch、Kibana、Fluentd |
| 单元测试 | `404 / 404` 通过 |
| ProductService | 构建 `0 warning / 0 error` |
| 管理后台 | `vue-tsc + vite build` 通过 |
| 商城 H5 | 构建通过，36 个路由可在 `390×844` 打开 |

## 本地环境

| 服务 | 地址 |
|---|---|
| 管理后台 | `http://127.0.0.1:5173` |
| 商城 H5 | `http://127.0.0.1:5175` |
| 网关 | `http://127.0.0.1:5008` |
| AgileConfig | `http://127.0.0.1:5000/ui` |

| 账号 | 值 |
|---|---|
| 平台编码 | `QHRQDB` |
| 后台超管 | `codexadmin` / `Admin123456` |
| 商城客户 | `youlo0018` / `Aa123456..` |

Linux 启动：

```bash
cd deploy/linux
./build.sh --fresh
./start-all.sh full
./status.sh
```

常用验证：

```bash
dotnet test
tests/e2e/run.sh
pwsh ./tests/ui/run-all.ps1
```

## 文档入口

| 文档 | 何时读 |
|---|---|
| [`README.md`](README.md) | 环境、地址、账号、项目结构 |
| [`BUSINESS.md`](BUSINESS.md) | 业务规则、权限、状态机、营销、积分、评价 |
| [`DATA_SPEC.md`](DATA_SPEC.md) | 实体、仓储、字段、FreeSql 与建表约定 |
| [`CODING_STANDARD.md`](CODING_STANDARD.md) | 写代码前 |
| [`DESIGN_SPEC.md`](DESIGN_SPEC.md) | 管理后台视觉与交互硬约束 |
| [`FRONTEND_DESIGN.md`](FRONTEND_DESIGN.md) | 管理后台页面与按钮规则 |
| [`REVIEW.md`](REVIEW.md) | 交易、库存、支付、退款、秒杀等关键链路 |
| [`TEST_CASES.md`](TEST_CASES.md) | 回归范围与用例编号 |
| [`docs/miniprogram-requirements.md`](docs/miniprogram-requirements.md) | 商城端逐页需求 |
| [`docs/miniprogram-visual-spec.md`](docs/miniprogram-visual-spec.md) | 商城端视觉规范 |
| [`docs/search-synonyms.md`](docs/search-synonyms.md) | 搜索同义词设计 |

## 必须遵守

1. 不覆盖工作区中已有的未提交改动；提交前先看 `git status`。
2. 不主动 commit / push，除非用户明确要求。
3. 业务、数据、接口、页面、测试规则变更时，同批更新对应文档。
4. 接口、方法、DTO 和字段必须有 XML 注释。
5. 建表只改 `deploy/sql/`，脚本必须幂等可重跑。
6. 金额使用两位小数和显式舍入；雪花 Id 前端保持字符串。
7. 新增网关路径必须同步权限种子，内部调用必须带 `X-Internal-Token`。

## 关键架构约束

- 后台按平台 / 商户做租户隔离；C 端只返回公开可见数据。
- ES 只负责召回，价格、上下架、库存和订单状态以 PostgreSQL 为准。
- 搜索同义词在查询层展开，保存后即时生效，不重建索引。
- 交易关键链路遵循“下游先处理、本地状态最后改”，依靠唯一索引和条件更新保证幂等。
- 秒杀库存依赖 Redis 原子预扣；回补前必须确认货确实被划出。
- 虚拟商品手动发货且必须填写发货内容；签收后不可退款。
- 取货码由服务端生成和验签，前端只展示与传递。

## 当前边界与已知问题

- 未配置真实微信 AppId，未接入微信登录和微信支付。
- 支付使用模拟支付；短信验证码使用本地固定验证码。
- 商城 H5 首轮视觉巡检发现：分类页底部内容被 TabBar 遮挡、购物车合计栏与 TabBar
  有约 5px 重叠、店铺页浮动购物车会遮挡滚动中的商品内容。
- 当前演示客户没有可用的待支付订单，支付成功流程尚未用真实待支付单完整验证。
- 仓库路径包含 `#` 时，Vite 构建和 dev-server 有限制，生产构建使用无 `#` 临时目录。
- H5 与后台当前通过本地预览服务运行，临时目录不作为长期部署目录。

## 最近完成

- 商城端重构为 uni-app Vue3 + TypeScript，共 36 个路由页面。
- 客户账号按平台隔离，支持平台编码切换和分平台登录态。
- 补齐取货码、订单商品主图、退款进度、领券中心、积分中心、商户活动和评价等接口。
- 后台装修支持平台 TabBar、跳转配置和页面组件配置。
- 商品搜索同义词升级为查询时扩展，支持四种规则和四级作用域。
- 后台导航使用 SPA 站内切换，不打开新标签、不整页刷新。
- 后台全页面带肉眼不可见的数字水印，编码用户 Id、会话后缀和截图分钟；超级管理员可在“水印解析”页本地解码，失败时回退 OCR。

## 交接顺序

1. 运行 `git status`，识别未提交改动。
2. 按任务阅读上表文档，不依赖聊天记录补规则。
3. 后端改动先构建相关服务；前端改动先做类型检查和生产构建。
4. 修改关键链路后，运行对应单元测试或回归脚本。
5. 完成后更新当前文档，并记录仍未验证或有测试数据限制的部分。
