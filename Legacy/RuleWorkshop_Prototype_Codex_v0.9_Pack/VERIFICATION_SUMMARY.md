# v0.9 实际验证摘要

此报告来自当前 reference_check.py 的实际执行；不是 Unity 工程验收。

- 12 关，18 条参考路径全部通过，5 个多策略关。
- 34 项当前专项检查通过。
- 共 153 个空间词块；主区 56 个；全部 anchored=false。
- 12 个无互动子图穷尽，总计 547 个状态、5470 次命令尝试，无无互动通关路径。
- 初态和每条参考路径的逐步规则均有真实 SourceTextIds。
- 完整撤销恢复初态；4 种实体列表重排后终局一致。
- 文档中的全部实体坐标和参考命令串已与 JSON 程序化核对。

## 每关词牌与搜索

| 关卡 | 世界 Text | 主区 Text | 参考解 | 无互动状态数 |
|---|---:|---:|---:|---:|
| L01 | 6 | 3 | 1 | 24 |
| L02 | 9 | 3 | 1 | 24 |
| L03 | 10 | 4 | 1 | 48 |
| L04 | 12 | 3 | 1 | 35 |
| L05 | 12 | 3 | 1 | 28 |
| L06 | 17 | 5 | 1 | 57 |
| L07 | 16 | 4 | 1 | 28 |
| L08 | 10 | 4 | 2 | 59 |
| L09 | 14 | 8 | 2 | 59 |
| L10 | 14 | 3 | 2 | 64 |
| L11 | 13 | 4 | 2 | 46 |
| L12 | 20 | 12 | 3 | 75 |

## 未验证

C# / Unity 编译、实际输入、字体与词牌视图、相机遮挡、透明隔墙外观、编辑器操作、构建、真人教学效果、全部解法枚举和最短解。
源词数据存在并不证明画面可读；请执行主规格的第一关硬验收。模型仅覆盖带底板关卡，不验证底部虚空或所有复杂物理边界。

## 本次专项检查

1. PASS — L01 schema, world sources, movable words and real support
2. PASS — L02 schema, world sources, movable words and real support
3. PASS — L03 schema, world sources, movable words and real support
4. PASS — L04 schema, world sources, movable words and real support
5. PASS — L05 schema, world sources, movable words and real support
6. PASS — L06 schema, world sources, movable words and real support
7. PASS — L07 schema, world sources, movable words and real support
8. PASS — L08 schema, world sources, movable words and real support
9. PASS — L09 schema, world sources, movable words and real support
10. PASS — L10 schema, world sources, movable words and real support
11. PASS — L11 schema, world sources, movable words and real support
12. PASS — L12 schema, world sources, movable words and real support
13. PASS — Non-spatial fixedRules injection is rejected, not combined or silently ignored
14. PASS — Deleting all world words removes all explicit properties and transformations
15. PASS — Displayed token and parsed token share data; stable ID w_win does not force WIN
16. PASS — Level 1 requires a real word push to activate FLAG WIN
17. PASS — Level 2 PUSH changes actual rock solidity and manual displacement permission
18. PASS — Replacing STOP with PUSH removes STOP; no hidden fallback sentence survives
19. PASS — Protected YOU source is ordinary movable text; reaching it in a test fixture allows breaking control
20. PASS — The source-area barrier is actual Terrain, not a cannot-push flag or UI exclusion zone
21. PASS — Main-puzzle noun and IS words are not anchored as attribute-only slots
22. PASS — Ordinary movement climbs a PUSH rock; push-intent moves it instead
23. PASS — STOP without PUSH blocks manual displacement but remains climbable
24. PASS — YOU and WIN on one identity never cause self-victory
25. PASS — World noun words perform ROCK to FLAG conversion and recompute target solidity
26. PASS — World IS transfer plus ROBOT IS FLAG preserves the distinct-body victory path
27. PASS — Same L11 initial data retains bounce and lift as different solutions
28. PASS — Four-view observation never changes rules or advances world time
29. PASS — Adding visible source words does not restore directional jumps
30. PASS — Ordinary text falls under world-default gravity and is not implicitly hovering
31. PASS — Removing the visible YOU word never silently leaves a robot controller active
32. PASS — Face adjacency to the hollow target still does not win
33. PASS — No-interaction search detects a deliberately authored traversal-only counterexample
34. PASS — Search limits are inconclusive, not no-bypass proofs
