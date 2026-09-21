# Implementation Status — Rule Pyramid RP-v0.5

阶段状态标记：已编写 / Python 设计模型通过（包内既有） / Unity C# 测试 / Game View 试玩 / 构建。

| 阶段 | 已编写 | Python 模型 | C# 测试 | 试玩 | 构建 |
|---|---|---|---|---|---|
| A 骨架 | 是 | 通过 | 通过 | L01 场景可加载 | 未做独立构建 |
| B 移动闭环 | 是 | 通过 | 通过（R01/R14/R16/R17） | L01 Play Mode 加载 Grounded (0,1,1) | 未做 |
| C 规则/推字 | 是 | 通过 | 通过（R08/R09/L02） | 未逐关人手 | 未做 |
| D 跳跃弹跳 | 是 | 通过 | 通过（R10–R15/R21/R22/L04/L06） | 未逐关人手 | 未做 |
| E 结算完整 | 是 | 通过 | 通过（R02–R07/R12/R18/R23/R24/L03/L05） | 未逐关人手 | 未做 |
| F HUD/镜头 | 是 | n/a | PlayMode 烟测通过 | HUD 已进 Prototype 场景 | 未做 |
| G 编辑器 | 是 | n/a | 草稿隔离测试通过 | 窗口菜单可用 | 未做 |

## 实际执行

- EditMode：`RulePyramid.Tests.EditMode` **27/27 Passed**（R01–R24 + 六关 referenceSolution 回放与 Undo + JSON 往返 + 编辑试玩隔离）
- PlayMode：`RulePyramid.Tests.PlayMode` **1/1 Passed**
- Play Mode：打开 `Assets/_RulePyramid/Scenes/Prototype.unity`，L01 会话 `Grounded you=(0,1,1) won=False entities=2`
- 初始化菜单：`Tools/RulePyramid/Create or Repair Prototype` 已执行并写日志成功

## 未验证

- 六关 Game View 完整人手走关、遮挡可读性、独立 Player 构建
- 编辑器 SceneView 点击落点与四槽镜头手感未做完整 UX 验收
