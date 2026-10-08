## 2026-10-09 后续修订：恢复词牌字体、精简阶段完成界面

按用户反馈，世界词牌恢复修改前的 TextMesh 默认字体、字号与字距，撤回名词粗体、属性描边、运算符字距、自动字号适配及 TMP 配置；旧字体资源解除引用后保留，自动审批拦截了目录删除。词牌底色、模型、材质和 YOU 标签保持现有表现。

中间阶段完成面板仅包含 Stage completed 与 Next stage；移除关卡名称、阶段编号、进度、说明、Undo／Restart 和错误标签，完成时隐藏背景 HUD。下一阶段加载失败仍保留面板和可重试按钮，错误保留在 Console。整关及末关完成界面维持现状，Z／R 入口仍可撤销／重开。

本次未运行测试、截图或发布构建。以下 VA-v1.2 测试和截图均为本次修订前的历史证据，不能作为当前字体或简化面板的验收结果。

## VA-v1.2 阶段结算、模型与词牌字形（2026-10-09，修订前记录）

已接入当前游戏。中间阶段展示独立 Stage complete 界面，动作动画完成后等待玩家点击 Next stage；整关结束为 Level complete，末关为 Campaign complete。中间阶段不写入整关存档；结算允许撤销／重开，切换期间锁定输入，失败可重试，暂停及 UI 重建有自动测试覆盖。

七类物件使用 Blender 4.2.22 生成的低多边形网格。源文件位于 `Art/SourceModels/RuleWorkshop_Objects.blend`，生成脚本为 `Tools/generate_game_models.py`，纹理脚本为 `Tools/generate_game_textures.py`；FBX、游戏网格、材质和纹理位于 `Assets/_RulePyramid/Art/Objects/`。运行时保留实例根节点，仅按当前 Subject 替换网格／材质；旧配置继续回退到旧几何。弹簧的开口顶盘让固定俯视角能看见线圈，机器人斜面面部朝向观察侧。现有 YOU 标签上移，避免盖住面部。

世界词牌使用同一 Liberation Sans SDF 字体：名词粗体实心及细浅色轮廓、属性较宽描边、运算符常规字重与紧凑字距。三套共享材质、完整单行字面、按宽度自动适配；覆盖全部当前 token 和未知值回退。名词底板保留身份色；LAVA 词牌使用稳定底色，物件使用流动裂隙与发光。实际检查确认 SDF shader 直接消费顶点色，Linear 正文在输入边界转换一次。正交镜头中的文字统一朝向屏幕。

本轮只改游戏，未将模型或字体同步到关卡编辑器三维预览及独立试玩。未修改关卡 JSON、规则代码、schemaVersion、镜头姿态或取景逻辑；保留剖切、固定玻璃和顶点反馈。初始 VisualConfig 的剖切半径为 0，继续保持该配置；相关测试仍验证启用剖切时的表现。

### 验证与实际限制

- Unity 编译成功，Console 无非预期 error／warning；故意加载失效地图的用例产生一条已断言的预期错误日志。相关 PlayMode **47/47**（`84e4f5dd68bd44e398e7efe4680d0165`），EditMode **33/33**（`1dcda9afc8a845b688238b877119e393`）。[测试记录](ArtReview/VA-v1.2/tests.json)。
- PlayMode 覆盖阶段等待、动画先完成、重复点击、Undo／Restart、暂停、失效地图重试、组件／UI 重建、存档，以及七类模型、身份替换、字体类别、完整长词、共享材质、六处地图过场、玻璃／剖切与弹跳顶点反馈。旧配置的配色与 TextMesh 回退测试保持通过。
- 九张正式地图与 HEAD 内容及 Git 行尾归一化后字节一致；[地图哈希](ArtReview/VA-v1.2/maps.json)。未通过修改地图或放宽路线断言消除旧解法失败。
- [纯逻辑参考路线回放](ArtReview/VA-v1.2/reference-replay.txt)显示：L1P1、L1P2、L2P1 通过；L1P3 两条、L2P2、L2P3、L3 各阶段的当前参考路线存在拒绝或未获胜。Core 与地图均无本轮变更；这些既有路线问题仍需单独修复。较广测试中的对应失败不计入上述通过数。
- 九阶段 × 720p／1080p 共 18 张实际 Game View 初态截图，另有阶段／整关／末关完成面板及七对象展示，共 26 张。结算样张明确通过运行时合成完成状态呈现，不作为实际地图解法通过证据；新结算自动测试使用真实规则获胜。
- 长词完整单行，描边、压窄增高及屏幕对齐改善了可读性；大地图 720p 的字仍较小，局部遮挡仍遵守正常深度。未声称真人盲测或所有场景的最低字高验收通过。
- 未执行发布构建或全量回归。源码及导入文本按字节检查 UTF-8 无 BOM、CRLF。

### 入口与复现

正常打开 Prototype 场景直接 Play 即可使用，不需要修复菜单。`Tools/规则工坊/接入游戏造型` 补齐缺失引用并保留人工配置；普通原型修复也会调用同一初始化。更改 Blender 源脚本后重新导出 FBX，再使用 `Tools/规则工坊/重载导出的游戏模型` 更新已有网格而保留 GUID。纹理尺寸为身份色板 128×32、岩浆底色／发光各 256×256。

视觉证据入口：[七类对象与词牌](ArtReview/VA-v1.2/objects-and-words-1920x1080.png)、[阶段完成](ArtReview/VA-v1.2/stage-complete-1280x720.png)、[整关完成](ArtReview/VA-v1.2/level-complete-1280x720.png)、[末关完成](ArtReview/VA-v1.2/campaign-complete-1280x720.png)。

# VA-v1.1 暖砂配色实施记录

日期：2026-09-27。规范：[当前关卡配色优化规范](../Art/RuleWorkshop_ArtSpec_v1.0.md)。

## 环境光提亮（2026-09-27）

按用户要求，仅调整 Prototype 场景的环境光和主光：环境光由天空盒改为三色渐变，天空 RGB 为 (0.70, 0.73, 0.78)，水平补光为 (0.58, 0.58, 0.56)，地面反射补光为 (0.38, 0.35, 0.30)；环境强度为 1，主方向光强度由 1 提高到 1.1，保留原主光颜色、方向和软阴影。目标是提亮侧面和背光面，并保留形体层次。

已核对场景序列化差异与文本编码。按用户要求不截图、不进行画面验收，实际亮度及可读性由用户在游戏中验收；未运行与配置调整无关的 Unity 测试。

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
