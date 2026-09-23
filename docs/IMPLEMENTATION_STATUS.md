# Implementation Status — Rule Workshop RW-v0.9

阶段状态：已编写 / Python 设计模型 / Unity C# 测试 / Game View 试玩 / 编辑器 / 构建。

| 阶段 | 已编写 | Python | C# 测试 | 试玩 | 编辑器 | 构建 |
|---|---|---|---|---|---|---|
| 规则来源空间化（去 fixedRules） | 是 | PASS | 已编写 | 待本机 | CreateEmpty 世界词 | — |
| 十二关 v0.9 数据 | 是 | PASS | 18 解用例 | 待本机 | 回放 A/B/C | — |
| 透明隔墙表现 | 是 | n/a | — | 待本机 | — | — |

## 实际执行

- 规格：`docs/PROTOTYPE_SPEC.md` = RW-v0.9；`AGENTS.md` 已指向 v0.9 Pack
- Core：schema 9；禁止 fixedRules 注入；校验要求世界 Text、全员 unanchored；options 含 `ruleSourceMode` / `textMobilityMode`
- Content：L01–L12 已替换为 v0.9 裁剪 JSON
- Runtime：透明隔墙按 `appearance=TransparentGlass` 半透明显示
- Editor／Tests：默认草稿与 Fixture 仅用世界词牌
- Python：`RuleWorkshop_Prototype_Codex_v0.9_Pack/reference_check.py` **PASS**（18 路径／34 专项／12 无互动穷尽）

## 未在本机关闭的验收项

1. EditMode 跑 `RulePyramid.Tests.EditMode`
2. L01 关闭 HUD 可见六块词牌；推 WIN 改变规则
3. Play Mode／构建冒烟
