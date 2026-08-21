# 本地试玩模式

Status: ready-for-agent

## Problem Statement

竞拍玩法、数值和 UI 的日常验证目前依赖 Fusion 专用服务端与 Photon 网络。网络、代理或云会话故障会阻塞玩法开发，也无法在无互联网环境中演示完整竞拍。

## Solution

在 AuctionDemo 中提供显式的本地试玩入口。`GameManager` 为每局创建全新的进程内 `Authority`，以一个长期存活的本地玩家身份开始，由 AI 补齐其余 `P - 1` 个玩家席位，并通过本地 `AuctionManager` 完成分析、出价、揭示、结算和连续对局。它不创建 Fusion 或 Photon 网络对象。

结算展示约三秒后自动开始下一局；本地玩家资产在同一次试玩中跨局累积。提供重置本地试玩操作，以初始资产建立新的本地玩家身份。联网入口保留为清晰独立的操作，且不自动连接。

## User Stories

1. 作为开发者，我想在未启动服务端且断网时开始本地试玩，以便不受联网故障阻塞玩法验证。
2. 作为开发者，我想显式点击开始本地试玩，以便区分本地试玩与联网验收。
3. 作为本地玩家，我想在默认规则下与三个 AI 席位完成竞拍，以便验证完整多人规则。
4. 作为本地玩家，我想按现有规则选择私有线索和提交出价，以便本地试玩不产生另一套玩法。
5. 作为开发者，我想让 AI 在本地试玩中仍只依据所属席位视角行动，以便验证信息权限与正式对局一致。
6. 作为本地玩家，我想在结算后自动进入下一局，以便验证连续竞拍节奏。
7. 作为本地玩家，我想在同一次试玩中保留结算后的资产，以便验证经济循环。
8. 作为开发者，我想重置本地试玩并回到初始资产，以便快速重测数值情形。
9. 作为开发者，我想在本地试玩过程中不产生 Fusion、Photon 或服务端连接，以便明确隔离联网故障。
10. 作为开发者，我想继续使用独立的联网入口进行 Fusion 烟测，以便不削弱联网验收。
11. 作为桌面构建使用者，我想在不带服务端参数时离线运行本地试玩，以便演示玩法。
12. 作为开发者，我想让本地试玩与联网对局共享展示和动作语义，以便避免两套玩法表现漂移。

## Implementation Decisions

- `GameManager` 保存本地运行内资产，并为每局确定 `matchId`、固定参赛名单和可信初始资产，再创建全新的本地 `Authority`。
- 本地玩家和补位 AI 都使用 Controller，并通过同一个本地 `AuctionManager` 提交 `ActionRequest`；不将默认的三个 AI 硬编码为规则。
- 本地 GM 直接连接进程内 Authority 并推进其时间；结算后由 `GameManager` 幂等保存真人资产变化、销毁旧 Authority，再创建下一局。
- 本地与联网模式共用 `ActionRequest`、`AuthorityResult`、`AuthorityState`、`AuthorityGameEvent` 和 `VisibleRecord` 契约。
- 联网入口保留在 AuctionDemo 中，但必须与本地试玩入口显式区分，且场景加载时不得自动连接。
- 本地试玩支持 Unity Editor Play Mode 与普通桌面构建；服务端启动参数不进入本地试玩。

## Testing Decisions

- 本地试玩的高层测试通过 `VisibleRecord`、真人动作和资产结果验证完整行为，不断言 Fusion 回调或 GameManager 内部字段。
- 在无服务端和无网络运行器的条件下验证一名本地玩家与 `P - 1` 个 AI 能完成对局。
- 验证本地玩家资产跨局累积，重置试玩后恢复初始资产。
- 验证本地试玩和联网对局使用同一 Authority 语义；既有 Authority 行为测试继续通过。
- Fusion 烟测继续作为独立验收，不承担规则边界测试。

## Out of Scope

- 局域网多人、同机多真人、房间发现或新的网络传输实现。
- 移除 Fusion 包、改变 `AuctionServer → MatchId → Authority` 的联网边界。
- 长期账号、资产持久化和本地试玩存档。

## Further Notes

- 本地试玩不是局域网模式；局域网能力需要单独的传输与发现设计。
- 本功能遵循 ADR-0004 和 ADR-0006：联网由 `AuctionServer` 通过 Fusion 路由到单局 Authority，本地试玩直接使用进程内 Authority。
