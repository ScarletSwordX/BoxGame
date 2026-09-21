# Rule Pyramid 原型

Unity 2022.3 原型，玩法合同：`docs/PROTOTYPE_SPEC.md`（RP-v0.5 / schemaVersion 5）。

## 运行

1. 菜单 `Tools/RulePyramid/Create or Repair Prototype`（首次或修复场景/配置）
2. 打开 `Assets/_RulePyramid/Scenes/Prototype.unity`
3. Play。WASD/方向键移动（相对镜头），Space 跳或顶点恢复下降，Q/E 转镜头，Z 撤销，R 重开。

关卡 JSON：`Assets/_RulePyramid/Content/Levels/L01.json` … `L06.json`。

## 编辑器

`Tools/RulePyramid/Level Editor`：Y 切片绘制、校验、保存。试玩不写回草稿。

## 测试

Unity Test Runner → EditMode → `RulePyramid.Tests.EditMode`。覆盖六关参考解与 R01–R24。
