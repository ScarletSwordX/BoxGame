# Rule Workshop v0.9 — 世界词牌为唯一规则来源

实施入口：将 `RuleWorkshop_Prototype_Codex_v0.9.md` 作为 `docs/PROTOTYPE_SPEC.md`。
本包完整替换 v0.8 规则来源和十二关内容，不是 Unity 已实现工程。

## 核心变化

所有显式规则只能从空间 Text 解析；JSON 无 fixedRules，运行时也没有这个后备来源。
基础控制、胜利、可推、悬停等都由实际名词／IS／属性词块组成。
重力、YOU 默认实体、文字默认可推和固定地形仍属已约定的隐式基础行为。

所有 Text 的 anchored=false。部分源词隔着实际透明 Terrain，不能在主区直接接近；它们不是装饰、UI 板或不可变规则。
主区名词和 IS 同样可移动。presentation/sourceGroups 只供构图与定位，不参与规则计算。
L01 六块字，推 WIN 激活目标；L02 九块字，先拼 PUSH，再搬石搭阶。没有前两关豁免。

## 内容

- 主规格：世界来源合同、运动与变形合同、编辑器迁移、12 关完整坐标与 18 条参考解。
- `levels/*.json`：schemaVersion=9 / mechanicsVersion=RW-v0.9 的实际关卡。
- `world_word_inventory.json`：各关词牌数量与源词组定位，仅作审计。
- `reference_check.py`：标准库有限 Python 参考模型。
- `validation_results.json`：本版实际运行结果，不代表 Unity/视觉验收。
- `VERIFICATION_SUMMARY.md`：检查结论与边界。
- `CHANGELOG.md`：版本变化。
- `manifest.json`：打包时的文件大小和 SHA-256；修改或重跑后需更新。

## 复现

```bash
python reference_check.py
```

不要使用 `-O`，脚本会拒绝关闭断言的运行方式。检查会更新回放预期和报告。
需要 Python 3.10 或以上。脚本不访问网络，不读取 Unity 工程，不安装依赖。

## Unity 首要验收

从 WorldState 遍历所有 Text 实际生成世界词牌，包括透明隔离区；不能只遍历 Object 或 mainAreaBounds。
打开 L01 必须有六个可读 TextView。关闭 HUD 后仍能在世界看见所有规则。
移动或测试删除 YOU/IS 必须改变控制权，撤销恢复；不能由 fixedRules、默认机器人或关卡脚本补回。
透明隔墙必须有可感知外观，不可用完全不可见的空气墙，也不可用不透明灰模把源词遮住。

本包只验证有限格子模型，未验证 C# 编译、真实 Shift 输入、四视角可读性、字体、透明渲染、编辑器交互、构建或真人体验。
