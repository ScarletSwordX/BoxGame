# BoxGame 代理约定

## 玩法合同

- 当前有效规格：`docs/PROTOTYPE_SPEC.md`（RP-v0.5）
- 实施状态：`docs/IMPLEMENTATION_STATUS.md`

## 必须加载的项目技能

修改本仓库文本源码或文档时，遵循：

- `.cursor/skills/文本编码约定/SKILL.md`

要点：**UTF-8 无 BOM** + **CRLF 行尾**；提交前按字节检查，不要用会吞掉 `\r` 的文本 API 下结论。

## 提交

- 格式：`feature:` / `chore:` / `fix:` + 中文简述
- 非必要部分不要使用英文
