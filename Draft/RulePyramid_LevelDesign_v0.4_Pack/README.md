# 规则金字塔 LD-v0.4 辅助包

**采用本包完整替换 v0.3，不混用旧目标碰撞、面邻接判胜或地面定向跳回放。**

核心基线：不同 YOU/WIN 同格即胜；六关使用可进入的空心目标；地面四向移动＋原地跳跃，仅在 JUMP 弹起后的顶点可侧移一次。

## 文件

| 文件 | 用途 |
|---|---|
| `RulePyramid_LevelDesign_v0.4.md` | 完整设计规格、六关坐标、引导、参考回放、框架与编辑器合同 |
| `six_levels_graybox.json` | 可复建的灰盒输入及版本/机制参数 |
| `validate_grayboxes.py` | Python 标准库确定性设计模型、参考回放与针对性检查 |
| `validation_results.json` | 本次实际生成的轨迹、事件、终态及检查结果 |
| `build_levels.py` | 可选作者脚本，恢复本版初始 JSON；会覆盖手动修改 |
| `audit_document.py` | 核对文档嵌入坐标、回放表与 JSON/实际报告 |
| `CHANGELOG_v0.4.md` | 相对 v0.3 的迁移重点 |

## 运行

解压到一个目录，用 Python 3.10 或以上运行，无第三方依赖：

```sh
python validate_grayboxes.py
python audit_document.py
```

检查器读取而不覆盖输入 JSON；成功后刷新 `validation_results.json`。六关初始稳定、初始未胜、末步同格获胜、整步撤销和相机不变性均会检查；另有 50 项语义/回归/限制转移子集检查。报告中的事件是设计模型的诊断记录，不是已经接入 Unity 的动画资源。

只有需要**恢复本版原始灰盒数据**时才运行：

```sh
python build_levels.py
```

不要在每次校验前无条件重建，否则会丢失作者对 JSON 的调整。若主动修改关卡，需相应更新参考路径、文档坐标/表格和针对性期望；文档审计报告不一致时不能继续把旧说明当作新设计。

## 给 Codex

先完整阅读 Markdown，再导入配套 JSON；Markdown 第 11 节可直接作为执行提示词。设计格式中的 `solidBoxes` 要展开成地形，`fixedRules` 要做成显式可读碑文。不得只改目标渲染而保留阻挡 Collider，也不得只改胜利公式却保留旧关卡的 PINK BLOCK。

程序选择轻量纯 C# 网格核心＋会话层＋Unity 表现/输入＋独立 Editor 适配；编辑器为 IMGUI 窗口＋SceneView 按 Y 层绘制。模拟与编辑预览共用规则、碰撞与胜利逻辑，草稿/试玩/磁盘隔离。

## 验证边界

这是设计辅助包，不是 Unity 项目或已打包游戏。Python 通过不代表 C# 编译、PlayMode、真实输入、镜头、动画、编辑器、保存导入或构建通过。五个绕解检查只遍历指定的限制转移子集，不证明所有解、最短解或唯一解。多 YOU、移动弹台、多下落体并发和边缘支撑不在已验证范围。
