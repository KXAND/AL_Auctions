# AGENTS.md

## Working rules

- 简明扼要地解决和回答问题，禁止随意拓展或顺手改动。
- 除非用户明确要求执行修改，否则只讨论实践方案。
- 除非用户明确要求重构，否则以最小且最佳的改动为标准。
- 项目处于开发阶段；放弃某个方案时，直接完全替换该模块，不考虑前向兼容。
- 在回复中使用项目文件的相对路径，而非绝对路径。
- Git 提交始终遵循 Conventional Commits。

## Agent skills

### Issue tracker

Issue、PRD 与实施任务以本地 Markdown 保存在 `.scratch/<feature-slug>/`；不将外部 PR 作为需求入口。详见 `docs/agents/issue-tracker.md`。

### Triage labels

使用默认五个状态：`needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human`、`wontfix`。详见 `docs/agents/triage-labels.md`。

### Domain docs

本仓库是 single-context：先读根 `CONTEXT.md`，再读相关 `docs/adr/`。详见 `docs/agents/domain.md`。
