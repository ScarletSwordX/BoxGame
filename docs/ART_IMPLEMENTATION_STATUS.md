# VA-v1.1 暖砂配色实施记录

日期：2026-09-27。规范：[当前关卡配色优化规范](../Art/RuleWorkshop_ArtSpec_v1.0.md)。

## 名词同色修订（2026-09-27）

名词词牌现与同名物件共用身份材质，六种原有名词及并行加入的 LAVA 均遵循同一映射；连接词、属性词与深墨正文保持原样。`nounTextMaterial` 和旧 Mat_TextNoun 仅保留兼容引用，不再决定名词词牌颜色。下文 33 项测试、截图及旧三类配色对比度均为本修订前的历史证据，不作为此次同色修订的视觉验收。重新核算后，ROBOT、ROCK、SPRING、WALL 的深墨文字对比低于 4.5:1，见规范 3.2，后续需优化阅读表现。

本次修订验证：PaletteRuntimeTests 5/5 通过（作业 `0229f24c21714a929bf33e4de9da13da`），覆盖名词与物件共享材质、变形／撤销、旧配置回退；Unity 无编译错误。未重拍关卡截图。

## 结果与范围

已将配色接入当前游戏并生成材质资产；Unity 编译及定向 PlayMode 33/33 通过。六个运行阶段已保存并检查 720p / 1080p 游戏画面。整体配色已应用，但长词、遮挡和原 HUD 的可读性仍有遗留问题，不标为全项视觉通过。

本轮未修改 Core、关卡 JSON、模型、字体、字号、输入或镜头取景逻辑。工作期间另有任务更新 L02 地图与项目文档，保留这些修改；本记录以证据目录中的地图快照说明截图和回放版本。

## 实际接入

- `VisualConfig` 继续承载表现配置，新增云、弹簧、墙、结构及三类词牌材质引用，以及背景色和正文色。保留旧材质引用和旧配置回退；未新增关卡字段或第二套规则接口。
- `WorldView` 按当前 Subject 选择六种身份材质，名词复用同名物件材质，连接词／属性词按 Tokens 分类选择词牌材质，覆盖 IS、AND、BOUNCY。类别不随成句状态变化；词牌未知值使用灰色回退。
- 名词变形及撤销沿用实际状态更新材质，STOP / PUSH / YOU 不更改主体身份色、不改变透明度或尺寸。
- 现有地形盒位于整图最低地形层的使用暖砂地面色；盒的 min.y 高于该层则使用淡紫结构色。只按既有盒赋色，不拆分几何、不读关卡 ID。扩图单元采用所属目标地形盒的相同分类；固定玻璃优先沿用原材质。
- 游戏相机使用暖砂纯色背景，固定第 1 视角和取景计算保持原样；实际旧画面是天空盒，不能仅修改未生效的背景字段。
- TextMesh 沿用原字形、0.12 characterSize、32 fontSize 与位置。GUI/Text Shader 在 Linear 工程直接消费顶点色，因此仅在写入 TextMesh.color 时执行一次 sRGB→linear 转换，配置本身保存 sRGB。修正前灰色文字的试拍不作为最终证据。
- 词牌主体关闭接收阴影，避免类别底色在台阶阴影下过暗；地形及普通物件仍使用原灯光和阴影。没有调整灯光、后处理或项目色彩空间。
- 保留玩家圆形剖切、固定玻璃和弹跳顶点反馈。YOU / WIN 配置字段目前没有独立可见标记入口；未新增圆环、图标或功能色覆盖。结构暗面／装饰色没有独立现有槽位，本轮未强制使用。

## 修改与资源

源码：`Runtime/Configuration/VisualConfig.cs`、`Runtime/Presentation/WorldView.cs`、`Runtime/Presentation/CameraSlotsController.cs`、`Editor/PrototypeSetup/PrototypeSetup.cs`（均位于 `Assets/_RulePyramid/`）。新增 `Tests/PlayMode/PaletteRuntimeTests.cs` 及 meta。

更新 `Config/VisualConfig.asset` 和既有 Mat_Terrain、Mat_Red、Mat_Blue、Mat_Pink、Mat_Text。新增共享材质 Mat_Structure、Mat_Cloud、Mat_Spring、Mat_Wall、Mat_TextNoun、Mat_TextOperator、Mat_TextProperty 及 meta。材质色与规范 HEX 一致；沿用 Standard Shader 的其他参数。Mat_PinkHollow 保持原文件内容。

菜单 `Tools/规则工坊/应用暖砂配色` 可以显式重置为规范色板。普通“创建或修复原型”在 paletteVersion=1 后保留已连接材质的人工调色；未迁移配置仅首次应用默认配色。反复修复配置的检查确认：材质数量不增长、人工颜色保留、玻璃不被重写。

已序列化材质资产和配置，正常打开 Prototype 场景直接 Play 即可生效，无须先运行菜单。未保存新的场景或改变构建列表。

## 验证证据

环境：Unity 2022.3.51f1、Windows Editor、Built-in、Linear、NVIDIA GeForce GTX 1080、Ultra、2×抗锯齿。相机、分辨率、地图哈希和状态指纹记录于各组 `captures.json`。

| 检查 | 结果／证据 |
|---|---|
| 编译及 Console | 编译成功，最后检查无 error / warning |
| 定向 PlayMode | 33/33，通过；作业 `0f1956a6c7654258aaba7854a4c3778c`，[测试记录](ArtReview/VA-v1.1/palette-tests.json) |
| 新增配色用例 | 5 项：身份／词牌／正文、真实推动变形与 Undo、旧字段回退、固定镜头背景、结构及扩图 |
| 既有相关回归 | WorldViewRuntimeTests、StageExpansionViewTests、BounceApexFeedbackTests，包含真实剖切像素断言和顶点反馈 |
| 色值与映射 | 六身份、全部 15 个规则词已覆盖；词牌输入对比度 5.89:1 / 11.10:1 / 7.84:1 |
| 同状态颜色对照 | 12 组，逐项核对地图哈希、状态指纹、相机位置／旋转／正交尺寸一致 |
| Game View | 六阶段 × 两个分辨率共 12 张含 HUD 截图；PNG 尺寸按文件字节确认 |
| 完整关卡回放 | 未全通过，见下方独立记录；不据此修改配色任务范围外的布局或断言 |
| 构建、全量 EditMode、性能 | 未执行；不声称构建或性能验收通过 |
| 文本编码 | 本轮修改文件按字节检查 UTF-8 无 BOM、CRLF；差异空白检查通过 |

首次较广测试作业 `32a8ba47cd01456685ac983fff17fe3f` 含两项失败：[原始记录](ArtReview/VA-v1.1/initial-broader-tests.json)。一项为新增测试使用浮点精确相等，而 TextMesh 顶点色实际按 Color32 量化；改为每通道 1/255 容差后通过。另一项为 StagedCampaignRuntimeTests 的参考路线 E 被拒绝，未修改或放宽该断言。

随后使用证据地图快照，仅创建 GameSession、不创建渲染器独立复现：L2P1 第 3 步 E、L2P2 第 17 步 E 被拒绝；L01 各路线及 L2P3 参考路线通过。[纯逻辑回放记录](ArtReview/VA-v1.1/reference-replay.json)。这些结果对应快照版本，不代表其他任务后续修改后的状态。

### 截图组织与对照

- `ArtReview/VA-v1.1/before/`：修改代码前实际捕获的原画面；由于地图并行更新，不能直接拿其中 L02 画面声称与最终布局完全一致。
- `ArtReview/VA-v1.1/sources/`：对照时使用的六张运行地图快照，仅作证据，不进入运行目录。
- `ArtReview/VA-v1.1/before-current-maps/`：在同一快照上重建旧材质颜色、天空盒与词牌阴影的对照。使用临时配置及材质副本，未回写游戏配置；这是重建旧配色对照，不冒充改动前原始截图。
- `ArtReview/VA-v1.1/after/`：相同快照、机位与初态下的新配色。以上三组为运行时 Camera RenderTexture，**不包含叠加 HUD**。
- `ArtReview/VA-v1.1/game-view/`：六阶段 720p / 1080p 的实际 Game View，包含 HUD；另保留 L2P1 参考路线被拒绝时画面。
- `ArtReview/VA-v1.1/supplement/`：历史未接入 L04 的云／玻璃检查，以及既有 BounceApexFeedbackTests 的真实顶点测试夹具；不把测试夹具当作当前关卡通过证据。

| 示例 | 同地图旧配色 | 当前配色与 HUD |
|---|---|---|
| L01 P2 | [旧配色](ArtReview/VA-v1.1/before-current-maps/L1P2-1280x720.png) | [720p](ArtReview/VA-v1.1/game-view/L1P2-1280x720.png) / [1080p](ArtReview/VA-v1.1/game-view/L1P2-1920x1080.png) |
| L02 P2 | [旧配色](ArtReview/VA-v1.1/before-current-maps/L2P2-1280x720.png) | [720p](ArtReview/VA-v1.1/game-view/L2P2-1280x720.png) / [1080p](ArtReview/VA-v1.1/game-view/L2P2-1920x1080.png) |
| 补充 | 历史地图／测试夹具 | [云与玻璃](ArtReview/VA-v1.1/supplement/historical-L04-cloud-glass-1920x1080.png) / [顶点反馈](ArtReview/VA-v1.1/supplement/test-fixture-bounce-apex-1920x1080.png) |

## 阅读结果与遗留问题

- 配色应用通过：暖砂地面、淡紫结构、物件身份色和三类词牌色均已呈现。文字已从误偏灰恢复为深墨色，阴影中的词牌类别不再过暗。并未执行逐像素全场文字对比度审计或真人盲测。
- L02 在 720p 的 SPRING / BOUNCY 等长词仍偏小；L2P3 的局部 BOUNCY 标签受到邻近物件遮挡。保留字号和空间布局，不把颜色改善等同于所有文字已可读。
- L2P3 换控区词牌集中，缺乏额外空间反馈；当前颜色仅区分类别，不能独立解释句子归属。后续考虑标签／规则反馈专项。
- 原 HUD 是浅色字加透明灰底，换成浅背景后对比偏弱；720p 底部面板也覆盖部分场景底座。本轮未改 HUD，可后续单独调整文字与背板。
- 当前模型仍以方块为主，身份颜色可辅助区分，但不宣称无颜色条件下均可辨认。缺少的 YOU / WIN 独立标记留待后续。

## 调整与回退

日常调色修改 VisualConfig 引用的共享材质及 backgroundColor / wordInk；运行时不会每帧从规范 JSON 覆写颜色。再次选择“应用暖砂配色”会明确恢复本版默认色，普通修复入口保留人工色。

需要回退时，只回退本轮四个源码文件的配色差异、VisualConfig 的新增配色字段与上述材质色／引用，并保留既有剖切、目录、关卡及其他任务改动。不要对当前脏工作区整文件还原。新增材质在没有引用时方可另行清理。
