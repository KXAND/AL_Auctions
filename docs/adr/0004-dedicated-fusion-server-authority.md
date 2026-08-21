---
status: accepted
---

# 使用 AuctionServer 承载联网对局 Authority

联网服务端由 `AuctionServer` 启动 Fusion Server Mode，负责可信连接、等待队列、对局创建、消息路由和资产保存；Fusion 仅作为传输实现。每场对局拥有一个全新的 `Authority`，由它保存唯一真值并负责包裹与线索生成、阶段计时、出价排序、AI 接管分界和结算。

`AuctionServer` 在逻辑上可以同时维护多场对局；采用一个或多个 Runner 不改变 `MatchId → Authority` 的边界。客户端只通过 `AuctionManager` 提交动作并接收定向生成的可见状态。相比玩家房主模式，这增加了服务端构建与启动成本，但避免了房主断线和客户端裁定问题。本地试玩不适用本决定，见 ADR-0006。
