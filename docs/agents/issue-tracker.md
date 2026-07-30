# Issue tracker: Local Markdown

本仓库的 PRD 与 issue 均以 Markdown 保存在 `.scratch/`。

## 约定

- 每个功能一个目录：`.scratch/<feature-slug>/`。
- PRD 位于 `.scratch/<feature-slug>/PRD.md`。
- 实施 issue 位于 `.scratch/<feature-slug>/issues/<NN>-<slug>.md`，从 `01` 编号。
- 每个 issue 顶部以 `Status:` 记录状态，状态字符串见 `triage-labels.md`。
- 后续讨论记录追加到文件末尾的 `## Comments`。

当技能要求“发布到 issue tracker”时，在相应功能目录创建 Markdown 文件。用户通常会直接提供路径或 issue 编号以供读取。

本项目不将外部 Pull Request 作为需求入口。
