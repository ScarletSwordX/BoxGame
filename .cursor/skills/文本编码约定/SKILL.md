---
name: 文本编码约定
description: 本仓库文本文件必须使用 UTF-8（无 BOM）与 CRLF 行尾。修改或新增 .cs/.json/.uxml/.uss/.asmdef/.md 等文本资源前后应检查编码与行尾；发现 LF-only、混合行尾、非 UTF-8 或带 BOM 时先规范化再提交。
---

# 文本编码约定

## 必须遵守

| 项 | 要求 |
|---|---|
| 字符编码 | **UTF-8，无 BOM** |
| 行尾 | **CRLF**（`\r\n`） |
| 适用范围 | `.cs` `.json` `.uxml` `.uss` `.asmdef` `.md` `.txt` `.yml` `.yaml` `.shader` `.hlsl` `.meta` 等文本；二进制资源除外 |

仓库根目录 `.gitattributes` 已声明 `eol=crlf`，提交与检出应以该约定为准。

## 何时检查

- 新增或批量改写文本文件之后、提交之前
- 从外部复制/粘贴、解压、或跨系统同步文件之后
- Agent 完成一批代码/文档改动准备提交时

## 检查方法（PowerShell / Python）

按**字节**判断，不要用会把 `\r\n` 归一成 `\n` 的文本读入 API 做行尾结论。

```python
from pathlib import Path
p = Path("某文件.cs")
b = p.read_bytes()
assert not b.startswith(b"\xef\xbb\xbf"), "禁止 UTF-8 BOM"
b.decode("utf-8")  # 必须能严格解码
crlf = b.count(b"\r\n")
lf = b.count(b"\n") - crlf
assert lf == 0, "存在裸 LF，应规范为 CRLF"
```

## 发现问题时

1. 先把内容规范为 UTF-8（去掉 BOM）+ CRLF
2. 再跑相关测试 / 编译确认无破坏
3. 单独或并入对应功能提交；纯约定修复可用 `chore: 统一文本为 UTF-8 无BOM与CRLF`

## 提交说明格式

本仓库提交信息优先：

```text
feature: 中文简述
chore: 中文简述
fix: 中文简述
```

非必要部分不要使用英文（专有名词、路径、版本号除外）。
