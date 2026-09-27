# 规则工坊原型（Rule Workshop）

当前基础移动：普通方向统一移动与推动可推物件和词牌；推不动则停住，不能翻越，不需要 Shift。所有关卡与编辑器试玩共用这一规则。

Unity 2022.3 原型，玩法合同：`docs/PROTOTYPE_SPEC.md`（**RW-v0.9** / schemaVersion **9**）。

设计包参考：`RuleWorkshop_Prototype_Codex_v0.9_Pack/`（十二关 + `reference_check.py`）。

## 运行

1. 菜单 `Tools/规则工坊/Create or Repair Prototype`（首次或修复场景／十二关目录）
2. 打开 `Assets/_RulePyramid/Scenes/Prototype.unity`
3. Play。WASD / 方向键移动与推动（相对镜头）；Space 原地跳或弹跳顶点恢复下降；固定第 1 视角；Z 撤销；R 重开。

关卡 JSON：`Assets/_RulePyramid/Content/Levels/L01.json` … `L12.json`。

**v0.9 要点：** 所有显式规则只来自世界词牌（无 `fixedRules`）；词牌全部可推；源词区用透明隔墙隔离。

## 编辑器

关卡可按设计安排 P1—PN（N≥1）个游玩阶段，数量由各关需要决定。每阶段使用独立地图文件，例如 `L1P1.json`、`L1P2.json`；各文件分别保存地图边界、地形、物件、词牌、出生点和参考解。后阶段可保留地标并局部移动、增删地形，边界不必包含旧地图；设计卡记录变化原因。进入下一阶段时加载预设初态，不继承玩家残局。编辑器不再使用阶段区域笔刷或色块归属；旧 `stagePlan` 草稿仅供兼容读取、切换和导出当前阶段地图。游戏内胜利后自动推进与过渡另行实现，现有关卡未自动转换。

关卡制作遵循 [关卡设计工作流](docs/LEVEL_DESIGN_WORKFLOW.md)，逐工序填写 [设计卡](docs/templates/LEVEL_DESIGN_CARD.md)：定位与目的 → 空间结构与地图尺寸 → 地图原型搭建并输出 JSON → 教学方法与反馈 → 解题逻辑审查 → 解法验证 → 盲测 → 定稿。前两项完成后即可搭建可回修的原型；后续审查和验证须针对实际地图。Teach–Test–Twist 留到多关卡联合审核。操作说明见 [编辑器指南](docs/LEVEL_EDITOR_GUIDE.md)。

`Tools/规则工坊/关卡编辑器`：Y 切片绘制、校验、隔离试玩、参考解回放 A/B/C、互动审核。试玩不写回草稿。

## 测试

Unity Test Runner → EditMode → `RulePyramid.Tests.EditMode`。

Python 设计模型：

```shell
python RuleWorkshop_Prototype_Codex_v0.9_Pack/reference_check.py
```
