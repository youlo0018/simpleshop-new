# PLAN — 阶段状态

> 本文件只保留阶段完成状态和仍被代码或脚本引用的决策。
> 历史实现流水使用 `git log` 追溯。

## 1. 阶段状态

| 阶段 | 范围 | 状态 |
|---|---|---|
| S0 | 基础设施、数据库、解决方案与公共库 | 完成 |
| S1 | Gateway、Auth、User、Permission、Customer、Tool | 完成 |
| S2 | Product、Cart、Inventory、ES + IK 搜索 | 完成 |
| S3 | 满减、满折、满赠、券、到手价与报表 | 完成 |
| S4 | Order、Payment、Scheduled 交易闭环 | 完成 |
| S5 | 平台、商户、审核、地区与装修 | 完成 |
| S6 | 积分与评价 | 完成 |
| S7 | 限时抢购与防超卖 | 完成 |
| S8 | Vue 3 管理后台 | 完成 |
| S9 | uni-app 商城端 | 完成 36 个路由页面 |
| S10 | 单元、API、UI、视觉与越权测试 | 持续维护 |

## 2. 保留决策

### 2.3 验收命令

```bash
dotnet test
tests/e2e/run.sh
pwsh ./tests/ui/run-all.ps1
```

Linux 使用 [`deploy/linux/README.md`](deploy/linux/README.md) 中的构建和启停脚本。

### 2.6 配置源决策

AgileConfig 不可达时，保留本地 `appsettings` / 环境变量作为过渡配置源。
这只改变配置来源，不提供业务默认值，缺少必需配置仍应 fail-fast。

## 3. 维护规则

1. 新增工作直接更新对应规格文档，不在本文件追加流水。
2. 修改交易、库存、积分、营销、秒杀、支付或退款时，同步检查 `REVIEW.md`。
3. 新增字段同步 `DATA_SPEC.md` 和 SQL 建表脚本。
4. 新增页面同步 `FRONTEND_DESIGN.md` 和 `DESIGN_SPEC.md`。
5. 新增测试同步 `TEST_CASES.md`。
6. 当前整体状态与已知边界见 `AI_HANDOFF.md`。
