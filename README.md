# 规则工坊原型（Rule Workshop）

Unity 2022.3 原型，玩法合同：`docs/PROTOTYPE_SPEC.md`（**RW-v0.9** / schemaVersion **9**）。

设计包参考：`RuleWorkshop_Prototype_Codex_v0.9_Pack/`（十二关 + `reference_check.py`）。

## 运行

1. 菜单 `Tools/规则工坊/Create or Repair Prototype`（首次或修复场景／十二关目录）
2. 打开 `Assets/_RulePyramid/Scenes/Prototype.unity`
3. Play。WASD / 方向键移动（相对镜头）；**按住 Shift + 方向** 推动；Space 原地跳或弹跳顶点恢复下降；Q/E 转镜头；Z 撤销；R 重开。

关卡 JSON：`Assets/_RulePyramid/Content/Levels/L01.json` … `L12.json`。

**v0.9 要点：** 所有显式规则只来自世界词牌（无 `fixedRules`）；词牌全部可推；源词区用透明隔墙隔离。

## 编辑器

`Tools/规则工坊/关卡编辑器`：Y 切片绘制、校验、隔离试玩、参考解回放 A/B/C、互动审核。试玩不写回草稿。

## 测试

Unity Test Runner → EditMode → `RulePyramid.Tests.EditMode`。

Python 设计模型：

```shell
python RuleWorkshop_Prototype_Codex_v0.9_Pack/reference_check.py
```
