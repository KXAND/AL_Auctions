---
status: accepted
---

# AI 仅基于所属席位视角决策

尽管 AI 在服务端运行，它只能通过无 UI 的 `AuctionManager` 读取所属玩家席位的 `VisibleRecord`，不能读取包裹真实组成、总价值 `Y` 或其他席位的私有线索结果。`Authority` 真值仅用于规则裁定和结算，以保证 AI 与真人处于相同信息边界。
