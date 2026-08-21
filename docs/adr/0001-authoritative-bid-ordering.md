---
status: accepted
---

# 以 Authority 接受顺序裁定同价

多人对局中，每局 `Authority` 接受出价请求的顺序是唯一有效出价顺序，客户端本地点击时间和本地时间不参与裁定。联网请求由 `AuctionServer` 根据可信 Fusion 连接路由到对应 `Authority`；真人与 AI 都经 `Controller → AuctionManager → ActionRequest` 的统一入口提交。
