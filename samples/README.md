# 示例配置

3 个键位映射 + 1 个回放脚本。导入即用。

## 字段格式

`samples/*.json` 都是**键位映射**格式（`Logical` 字段），导入后走"冷却随机"模式。

| 字段 | 含义 |
|---|---|
| `CooldownMs` | 基础冷却 |
| `JitterMs` | 0~+N 抖动（只延后） |
| `AfterTriggerWaitMinMs` | 触发后到选下一个的最小等待 |
| `AfterTriggerWaitMaxMs` | 触发后到选下一个的最大等待 |
| `Weight` | 加权（0=禁用） |

回放脚本格式（`LogicalKey` 字段）走"脚本回放"模式，详见 `../README.md`。

## combat_weighted.json — RPG 技能循环（加权）

| 逻辑键 | 物理键 | 冷却 | 抖动 | 触发后等 | 权重 |
|---|---|---|---|---|---|
| Basic | Q | 3.0~3.3s | ±300ms | 0.8~1.5s | 5 |
| Skill1 | W | 5.0~5.5s | ±500ms | 1.0~2.0s | 3 |
| Skill2 | E | 8.0~9.0s | ±1s | 1.2~2.0s | 2 |
| Ult | R | 15~16.5s | ±1.5s | 1.0~1.8s | 1 |
| Dodge | Space | 1.0~1.2s | ±200ms | 0.5~1.0s | 4 |

**权重分布**：Basic 5/15 ≈ 33%，Dodge 4/15 ≈ 27%，Skill1 3/15 = 20%，Skill2 2/15 ≈ 13%，Ult 1/15 ≈ 7%。
**回放时序示例**（每轮保证每键出现 1 次，顺序随机）：

```
假设本轮抽到 A=Skill2, B=Skill1, C=Basic, D=Dodge, E=Ult
  t=0.0  发 Skill2 (E)
  t=1.5  发 Skill1 (W) [等 Skill2 触发后 1.2~2.0s + 冷却就绪]
  t=2.8  发 Basic  (Q)
  t=3.5  发 Dodge  (Space)
  t=8.5  发 Ult    (R) [要等 R 冷却好 15s±1.5s]
```

跨轮 `lastTrigger` 保留——Dodge (1s 冷却) 在下一轮可能"卡"在 cd 上。

## menu_navigate.json — 菜单导航

方向键 + Enter/Esc，每个键 cd=1.5~2s，触发后 0.4~0.6s。  
适合测试菜单选择响应、连续翻页、确认/取消流程。

## numpad_smoke.json — 数字键冒烟测试

0-9 顶行，每个键 cd=0.5s，触发后 0.1~0.2s。  
快速验证数字扫描码是否生效，回放 1 轮约 5 秒。

## mapping_numpad.json — 数字键键位映射（备用）

跟 `numpad_smoke.json` 类似，权重=1。

## 使用方法

1. 把样本从 `samples/` 拷到 `configs/`
2. 启动 GameInputTester.exe → 左侧"刷新列表" → 双击选中
3. 切到游戏窗口（前台）→ 按 **F6** → 启动冷却循环
4. 按 **F7** 停止
