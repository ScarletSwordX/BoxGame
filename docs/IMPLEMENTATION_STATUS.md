# Implementation Status — Rule Workshop RW-v0.8

阶段状态：已编写 / Python 设计模型 / Unity C# 测试 / Game View 试玩 / 编辑器 / 构建。

| 阶段 | 已编写 | Python | C# 测试 | 试玩 | 编辑器 | 构建 |
|---|---|---|---|---|---|---|
| A 模拟核（变形／换控／推爬） | 是 | PASS | 已编写待跑 | — | — | — |
| B Runtime + L01–L07 | 是 | PASS | 已编写待跑 | 待本机 Play | 菜单／HUD 已改 | — |
| C L08–L12 + 18 解 | 是 | PASS | 18 解用例已写 | 待本机 Play | 回放 A/B/C | — |
| D 审核与验收 | 是 | PASS | 互动审核用例已写 | 待本机 | 互动审核按钮 | 待本机 |

## 实际执行

- 规格：`docs/PROTOTYPE_SPEC.md` = RW-v0.8；`AGENTS.md` 已更新
- Core：`WorldModel` 移植 Pack `reference_check.py`；Transformation／Control／双方实体碰撞／Shift 推／登攀
- Content：L01–L12（JsonUtility 友好裁剪）+ `LevelCatalog` 十二关引用
- Runtime：Shift→`P*`；按 `subject` 选材质
- Editor：`Tools/规则工坊/…`；解法回放；互动审核
- Tests：`RuleWorkshopCoreTests`（schema／推爬／变形／换控／十八解／L02 审核／编辑隔离）
- Python：`reference_check.py` **PASS**（12 关／18 见证／80 局部／11 无互动穷尽）

## 未在本机关闭的验收项

当前会话 Unity MCP（`127.0.0.1:8080`）不可用，且 Hub 未检出 2022.3.51f1 可执行文件，因此下列项需在打开本工程的编辑器中完成：

1. `Tools/规则工坊/Create or Repair Prototype`
2. EditMode 跑 `RulePyramid.Tests.EditMode`
3. Play Mode 打开 Prototype，Bootstrap 回放或人手试玩
4. Windows 开发构建冒烟
