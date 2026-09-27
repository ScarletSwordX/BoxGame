# 规则工坊（Rule Workshop）

基于 Unity 2022.3.51f1 的空间规则解谜原型。玩家推动世界中的词牌改写规则；当前玩法合同为 [RW-v0.9 / schemaVersion 9](docs/PROTOTYPE_SPEC.md)。规则、关卡设计与实现进展分别以规格、设计卡和[实施状态](docs/IMPLEMENTATION_STATUS.md)为准。

## 快速开始

1. 用 Unity 2022.3.51f1 打开本仓库。首次打开或需要修复场景、关卡目录时，运行菜单 `Tools/规则工坊/创建或修复原型`。
2. 打开 [Prototype 场景](Assets/_RulePyramid/Scenes/Prototype.unity)，进入 Play Mode。
3. 主界面的 **Play** 从首关开始；有可继续的存档时才显示 **Continue**，它从最后通关关卡的下一关第一阶段开始。

| 操作 | 按键 |
|---|---|
| 相对镜头移动、推动物件与词牌 | WASD / 方向键 |
| 原地跳跃；弹跳顶点恢复下降 | Space |
| 撤销当前阶段的一步 | Z |
| 重开当前阶段 | R |
| 暂停或继续 | Esc |

方向移动会自动尝试推动可推的物件或词牌；推不动则停下。无需 Shift，也不能用方向键翻越。暂停菜单也可通过 HUD 的 Pause 按钮打开。

## 当前正式关卡

[正式目录清单](Assets/_RulePyramid/Content/Levels/catalog.json)目前包含 L01、L02、L03 三关，每关 P1—P3 三张独立地图，位于 `Assets/_RulePyramid/Content/LevelDrafts/`。P1/P2 获胜后自动进入下一阶段的固定初态；P3 获胜后完成整关；若目录中还有下一关，可点击 Next 进入。阶段切换不继承玩家残局、撤销记录或提示状态。

完成一关的最后阶段时，游戏在本机保存最远通关进度。重玩较早关卡不会倒退存档；全部关卡通关后因没有下一关，Continue 会隐藏。Play 始终从首关开始。

显式规则只由地图中的可移动词牌组成，HUD 仅展示解析结果。当前实现支持多个 YOU、LAVA/HOT/MELT、DEFEAT、名词变形，以及同一存活对象同时具有 YOU 和 WIN 时获胜；具体判定以[玩法合同](docs/PROTOTYPE_SPEC.md)的最新修订为准。正式目录接入不等于每张地图都已完成盲测与定稿，见[实施状态](docs/IMPLEMENTATION_STATUS.md)。

旧版 L01—L12 单图关卡仍在 `Assets/_RulePyramid/Content/Levels/`，旧设计包在 `Legacy/`；两者都不从当前游戏入口加载，也不作为现行九张阶段地图的验证基线。

## 关卡编辑与制作

`Tools/规则工坊/关卡编辑器` 提供地图编辑、校验、独立试玩与参考解回放。每个游玩阶段使用独立 JSON；阶段数量由关卡设计决定，不固定为三阶段。编辑器试玩只操作当前地图，不写回草稿。

新增或重做关卡时，先读[关卡设计工作流](docs/LEVEL_DESIGN_WORKFLOW.md)，按[设计卡模板](docs/templates/LEVEL_DESIGN_CARD.md)在 `docs/level-design/` 逐工序记录。操作细节见[编辑器指南](docs/LEVEL_EDITOR_GUIDE.md)。

`Assets/_RulePyramid/Content/Levels/catalog.json` 是正式关卡与阶段顺序的来源；改动目录后运行 `Tools/规则工坊/同步正式关卡目录`，更新构建使用的 `Assets/_RulePyramid/Config/LevelCatalog.asset`。

## 验证

在 Unity Test Runner 中分别运行 EditMode 与 PlayMode 测试。当前测试结果、已知限制和未完成的设计验证记录在[实施状态](docs/IMPLEMENTATION_STATUS.md)。`Legacy/RuleWorkshop_Prototype_Codex_v0.9_Pack/reference_check.py` 属于旧设计包，不代表当前九张阶段地图的运行时验收。
