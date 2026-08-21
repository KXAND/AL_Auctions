---
status: accepted
---

# 使用进程内权威进行本地试玩

本地试玩由 `GameManager` 在同一进程内为每局创建独立 `Authority`，以一个本地玩家身份和 AI 补齐其余玩家席位完成完整对局。真人与 AI Controller 通过同一个本地 `AuctionManager` 提交动作，复用联网对局的规则、可见状态和 AI 语义，但不承担多人连接或局域网职责；联网传输由 `AuctionServer` 与 Fusion 负责。
