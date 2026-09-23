# 规则工坊原型（Rule Workshop）

Unity 2022.3 原型，玩法合同：`docs/PROTOTYPE_SPEC.md`（**RW-v0.8** / schemaVersion **8**）。

设计包参考：`RuleWorkshop_Prototype_Codex_v0.8_Pack/`（十二关 + `reference_check.py`）。

## 运行

1. 菜单 `Tools/规则工坊/Create or Repair Prototype`（首次或修复场景／十二关目录）
2. 打开 `Assets/_RulePyramid/Scenes/Prototype.unity`
3. Play。WASD / 方向键移动（相对镜头）；**按住 Shift + 方向** 推动；Space 原地跳或弹跳顶点恢复下降；Q/E 转镜头；Z 撤销；R 重开。

关卡 JSON：`Assets/_RulePyramid/Content/Levels/L01.json` … `L12.json`（共 12 关，18 条参考解）。

## 编辑器

`Tools/规则工坊/关卡编辑器`：Y 切片绘制、校验、隔离试玩、参考解回放 A/B/C、互动审核。试玩不写回草稿。

## 测试

Unity Test Runner → EditMode → `RulePyramid.Tests.EditMode`。覆盖 schema、推／爬、变形、换控、十八解回放与 L02 无互动审核。

Python 设计模型：

```shell
python RuleWorkshop_Prototype_Codex_v0.8_Pack/reference_check.py
```
