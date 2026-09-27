# Rule Workshop v0.8 — Entity transformation prototype specification

此包是完整设计规格、十二关初始数据、参考路径与 Python 检查器，不是已经完成的 Unity 工程。

## 实施入口

把 `RuleWorkshop_Prototype_Codex_v0.8.md` 作为本轮 `docs/PROTOTYPE_SPEC.md`。
以 `schemaVersion=8 / mechanicsVersion=RW-v0.8` 为唯一规则版本，不混用 v0.7 的名词变形禁止条款。
保留 Shift 推物、普通登上一格、原地跳、三格弹跳后一次侧移、默认重力、四视角。

本轮增加 `SUBJECT IS SUBJECT`，如 `ROBOT IS FLAG`：当前物种就地改变，ID 与位置保留，按目标物种重算属性。
拆句不自动还原；Undo 恢复整条指令。每个实例每条有效指令最多变一次，批次同步。
单目标变形是 P0。多 YOU、受控 HOVER/FLY、一变多、混合名词/属性右侧暂不支持，须明确诊断。

L08-B 与 L12-B 展示 ROCK→FLAG；L12-C 先换控 SPRING，再 ROBOT→FLAG。
第 9 关保留原“赋 WIN 再换控”的纯属性解法，以区分性质变化与身份变化。

## 复现

```shell
python reference_check.py
```

需要 Python 3.10 或以上；仅用标准库。不要以 `python -O` 执行：检查器使用 assert 作为测试断言。
脚本会重写各关 referenceSolutions 的结果元数据以及 validation_results.json。
本包的 manifest.json 是打包时文件摘要，重跑或编辑后需重新生成；旧摘要不是新内容的验证证据。

## 限制

验证是有限格子模型，不替代 C# / Unity 编译、实际输入、编辑器、动画、镜头、玩家测试或构建。
它只涵盖当前有底板的关卡，不涵盖坠入底部虚空的死亡/删除。
多个真正不同的参考解证明“至少存在这些方法”，不是唯一解、最短解或全部解法枚举。

`Model` 是低层测试状态构造器；生产 WorldFactory 还须校验没有初始待变形。
正式十二关已在检查入口校验这一点，单测可故意构造待变形状态来验证算子。

部分旧词牌 ID 保留了 w_win / w_flag 等历史名称；显示内容必须读取 token，而不能根据 ID 推断。
