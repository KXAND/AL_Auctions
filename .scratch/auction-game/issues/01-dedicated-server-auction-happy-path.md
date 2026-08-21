# AuctionServer 的可完成竞拍主路径

Status: ready-for-agent

## Parent

`.scratch/auction-game/PRD.md`

## What to build

交付一条可从客户端进入、由 `AuctionServer` 通过 Fusion 路由到单局 `Authority` 裁定、并回到客户端展示结果的最小竞拍主路径。

每局 `Authority` 是此切片唯一的规则裁定方。它必须接收受控的时间推进和统一 `ActionRequest`，生成按目标玩家过滤的 `VisibleRecord` 与成交结果。`AuctionServer` 只负责可信连接、对局创建和定向路由。首个端到端场景可使用最小的配置化演示包裹与线索内容，但不能在客户端裁定对局，也不能把包裹真值发送给客户端。

客户端至少能建立连接、取得运行内初始资产、看到默认网格与阶段状态、选择一条私有线索、提交合法整数出价，并看到一次成功成交后的余额变化。该路径应同时建立行为测试入口，能在不启动 Unity 场景或 Fusion 运行时的条件下验证同一规则流程。

## Acceptance criteria

- [ ] `AuctionServer` 使用 Fusion Server Mode 接收可信请求，每局独立 `Authority` 是唯一规则与真值拥有者；客户端仅通过 GM 提交动作并消费自身 `VisibleRecord`。
- [ ] 可配置的对局人数默认值为四，空席位的控制者由临时实现填充，使最小对局可启动。
- [ ] 玩家连接后获得固定初始资产；客户端能显示可用资产、默认网格和当前阶段。
- [ ] 分析阶段能显示公共线索与至少一个候选私有线索；选中后仅选择席位立即获得结果。
- [ ] 出价阶段能提交不超过可用资产的非负整数；`Authority` 完成一次有效出价成交并将 `Y - Xw` 反映到赢家资产。
- [ ] 行为测试只通过对局核心的输入输出验证主路径，并证明客户端视角不包含包裹真值或其他席位的私有结果。
- [ ] 可在本地启动服务端和至少一个客户端，完成一次可观察的端到端竞拍。

## Blocked by

None - can start immediately.

## Comments

