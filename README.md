# GameInputTester

针对**自研 Win PC 游戏**的键盘输入测试工具：录制 / 回放 / 键位映射 / 冷却随机 / 加权洗牌 / 多配置文件。

> ⚠️ 编译产物是 Windows 可执行文件。代码可在 macOS / Linux 编辑，编译请用 Windows 或 GitHub Actions。

## 目录

- [核心功能](#核心功能)
- [环境准备](#环境准备)
- [编译](#编译)
- [配置目录结构](#配置目录结构)
- [配置文件格式](#配置文件格式)
- [使用流程](#使用流程)
- [详细操作方法](#详细操作方法)
- [故障排查](#故障排查)
- [目录结构](#目录结构)

---

## 核心功能

### 1. 冷却随机模式（默认 / 推荐）
按键位映射表，**每轮**保证所有 `Weight > 0` 的键都被触发一次（顺序随机），**跨轮**保留 `lastTrigger`。
适用于 RPG 技能循环、MMO 战斗节奏、ACT 连段等场景。

**字段**：

| 字段 | 含义 | 单位 |
|---|---|---|
| `CooldownMs` | 基础冷却时间 | 毫秒 |
| `JitterMs` | 0~+N 抖动（**只延后不提前**） | 毫秒，0=无抖动 |
| `AfterTriggerWaitMinMs` | 该键发完后到选下一个键的最小等待 | 毫秒 |
| `AfterTriggerWaitMaxMs` | 该键发完后到选下一个键的最大等待 | 毫秒，< min 退化为 min |
| `Weight` | 加权（整数，0=禁用该键） | — |

**回放时序**：

```
发完键 → 等 AfterTriggerWaitMin~Max（区间随机）
  → 从"加权洗牌池"随机抽 1 个
  → 等该键冷却好 = lastTrigger[k] + CooldownMs + Jitter[0..+N]
  → 发键 + 记录 lastTrigger → 循环
```

每轮开始重新洗牌（`Fisher-Yates`），保证每轮覆盖全部键，跨轮 `lastTrigger` 保留让 CD 真正"过完"。

**"每轮重置冷却"开关**（模式条 CheckBox）：

- ☑ 勾选 = 每轮开始时所有键从就绪状态出发（独立测试用例）
- ☐ 不勾（默认）= 跨轮累计 CD（长时间压测，CD 真的"过完"才发）

### 2. 脚本回放模式（旧 / 保留兼容）
按录制顺序依次发键，键间隔使用 `WaitMin~WaitMax` 区间随机。导入老脚本（`LogicalKey` 字段）即可用。

### 3. 录制
低层键盘钩子 (`WH_KEYBOARD_LL`) 记录物理键 Down/Up，按 `Logical` 字段反查显示。

### 4. 多配置
`configs/` 目录存多个 `.json`，左侧列表切换，启动记忆上次选择。

### 5. 导入 / 导出
菜单"文件 → 导入配置..." / "保存为配置..."。自动识别脚本（`LogicalKey`）还是映射（`Logical`）。

### 6. 内置 JSON 编辑器
左侧配置列表右键 → "编辑..." → 应用内编辑 `.json`，带格式化和 JSON 校验。

## 全局热键

| 键 | 作用 |
|---|---|
| `F4` | 打开键位映射 |
| `F5` | 录制切换 |
| `F6` | 启动回放 |
| `F7` | 停止回放 |
| `Ctrl+S` | 保存为配置 |

---

## 环境准备

**Windows 10/11** 上需要装 **.NET 8 SDK**：

1. 下载：https://dotnet.microsoft.com/download/dotnet/8.0
2. 选 **SDK 8.0.x → win-x64** 安装（**勾上 "Add to PATH"**）
3. 验证（新开 cmd）：

```bash
dotnet --version
# 应输出 8.0.xxx

dotnet --list-runtimes
# 应看到 Microsoft.WindowsDesktop.App 8.0.x
```

如果 `WindowsDesktop.App` 不在列表里：单独装 **Desktop Runtime 8.0.x**（同一页面有链接）。

**Visual Studio 2022**（替代命令行，可选）：

1. 装 **VS 2022 Community**（免费）：https://visualstudio.microsoft.com/zh-hans/downloads/
2. 安装时勾选工作负载 **".NET 桌面开发"**
3. 双击 `lema\GameInputTester.csproj` 打开
4. `Ctrl+Shift+B` 编译 / `F5` 调试运行

**macOS / Linux 限制**：**不能编译 / 跑**。本项目 `net8.0-windows` + WinForms，只能在 Windows 上 build。macOS 上能做的：编辑 `.cs` / `mockup` / `README` / 提交 git。**没有 Windows 机器** → 用 GitHub Actions 的 `windows-latest` runner（`.github/workflows/build.yml`）自动 build。

## 编译

### 命令行

```bash
cd lema
dotnet restore            # 首次：拉 WinForms + Windows API 包
dotnet build -c Release   # 编译 → bin\Release\net8.0-windows\GameInputTester.exe
```

跑起来：

```bash
bin\Release\net8.0-windows\GameInputTester.exe
```

Windows Defender 首次可能弹"未识别的应用"警告 → "更多信息 → 仍要运行"。

### 单文件发布（不释放临时目录，~70 MB）

```bash
dotnet publish -c Release -r win-x64 \
  -p:PublishSingleFile=true \
  -p:SelfContained=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true
```

产物：`bin\Release\net8.0-windows\win-x64\publish\GameInputTester.exe`

`SelfContained=true` = 目标机器**不需要装 .NET**，直接拷过去就能跑。

### GitHub Actions（macOS 推荐）

push 代码到 GitHub，Actions 标签页会自动跑 build，下载 artifact 即可：

1. 仓库设置页：Settings → Actions → General → Workflow permissions 选 "Read and write permissions"
2. push 后看 https://github.com/\<owner\>/\<repo\>/actions
3. 最新 workflow run 底部 Artifacts → 下载 `GameInputTester-win-x64.zip`
4. 解压得到 `GameInputTester.exe`，双击运行

---

## 配置目录结构

```
lema/
├── GameInputTester.csproj
├── app.manifest              # asInvoker + PerMonitorV2 DPI
├── Program.cs                # MainForm（模式 UI + 录制 + 回放 + 导入/配置列表）
├── WinApi.cs                 # RegisterHotKey / SendInput / 扫描码表
├── Recorder.cs               # 低层键盘钩子
├── Player.cs                 # CooldownPlayer + ScriptPlayer
├── MappingForm.cs            # US 104 键下拉 + 7 列配置
├── EditConfigForm.cs         # 内置 JSON 编辑器（应用内编辑配置文件）
├── UI_mockup.html            # UI 设计参考（仅供查看，运行时不需要）
├── README.md
├── .gitignore
├── .github/workflows/build.yml
└── samples/
    ├── README.md
    ├── combat_weighted.json   RPG 技能循环（5 键，加权）
    ├── menu_navigate.json    菜单导航（方向键 + Enter/Esc）
    ├── numpad_smoke.json     数字键冒烟（0-9 短间隔）
    └── mapping_numpad.json   数字键键位映射
```

## 配置文件格式

### 键位映射（settings.json / configs/*.json）

```json
[
  {
    "Logical": "A",
    "Physical": "A",
    "CooldownMs": 8000,
    "JitterMs": 1000,
    "AfterTriggerWaitMinMs": 1000,
    "AfterTriggerWaitMaxMs": 2000,
    "Weight": 1
  }
]
```

### 回放脚本（兼容旧版）

```json
[
  { "LogicalKey": "A", "Physical": "A", "CooldownMs": 8000, "JitterMs": 1000, "AfterTriggerWaitMinMs": 1000, "AfterTriggerWaitMaxMs": 2000, "Weight": 1 }
]
```

`ImportFromPath` 自动按字段名识别：有 `LogicalKey` → 脚本，有 `Logical` → 映射。

## 使用流程

1. 启动 → 默认 4 个映射 (A/S/D/F) 出现，模式 = "冷却随机"，轮数 = 5
2. 按 **F4** → 调整键位 / 冷却 / 抖动 / 操作间隔 / 加权 → 保存
3. 把 `samples/*.json` 拷到 `configs/`，左侧"刷新列表" → 双击切换
4. 切到游戏窗口（前台）→ 按 **F6** → 启动冷却循环
5. 按 **F7** 停止

---

## 详细操作方法

### 1. 编辑键位映射（按 F4）

弹出 7 列 DataGridView 对话框，编辑规则：

- **编辑**：双击单元格，输入新值
- **新增**：滚到最后一行，输入即可
- **删除**：选中行 → 右键 → 删除
- **保存**：底部"保存"按钮
- **取消**：点 X 关闭窗口（自动放弃未保存修改）

### 2. 录制（按 F5）

- **第 1 次按 F5** → 开始录制，监听 `Logical → Physical` 映射里的所有键
- **第 2 次按 F5** → 停止录制，事件进入 `_buffer`
- **第 3 次按 F5** → 清空 buffer，重新开始

> 注意：录制的是 **Physical 键的 Down/Up 事件**，按你设置的所有白名单键都会捕获。录制期间后台仍接收键盘事件，不影响前台游戏。

### 3. 回放（按 F6）

**3.1 冷却随机**（默认）：见 [核心功能](#1-冷却随机模式默认--推荐)

**3.2 脚本回放**（旧）：按录制顺序依次发键，键间隔用 `CooldownMs` / `CooldownMs + JitterMs` 作 Min/Max 区间随机。**适用**：精确复现一段操作序列。

### 4. 停止（按 F7）

立刻取消正在进行的回放，buffer / 状态保留，下次按 F6 接着跑。

### 5. 多配置文件

左侧配置列表自动扫描 `configs/*.json`：

- **刷新列表** → 重新读目录
- **打开目录** → 弹资源管理器
- **删除当前** → 删激活配置
- **双击 / 回车** → 加载该配置
- **右键** → 弹出菜单：
  - **编辑...** → 打开内置 JSON 编辑器
  - **用记事本打开** → 外部编辑
  - **删除** → 同"删除当前"

启动时自动恢复**上次激活的配置**（记忆在 `last_config.txt`，`.gitignore` 排除不入仓）。

### 6. 内置 JSON 编辑器（右键配置 → 编辑...）

- **Ctrl+S** = 保存
- **Alt+F** = 格式化
- **Alt+V** = 校验 JSON
- **重新加载** = 撤销改动

**安全机制**：保存前自动 `JsonDocument.Parse` 校验，**无效 JSON 不写盘**；关闭时如有未保存改动，弹"丢弃吗？"确认；保存后如果是当前激活配置，自动 `ImportFromPath` 重新加载到 `_mapping`。

### 7. 导入 / 导出

- **菜单"文件 → 导入配置..."** → 选 `.json` → 自动识别脚本（`LogicalKey`）还是映射（`Logical`）
- **菜单"文件 → 保存为配置..."** → 选目录 + 命名 → 存到 `configs/script_yyyyMMdd_HHmmss.json`
- **Ctrl+S** → 同"保存为配置..."

### 8. 焦点要求

SendInput 把键发给**前台窗口**：
- 回放期间**游戏窗口必须在前台**（点开游戏窗口，不能最小化）
- GameInputTester 主窗口可以最小化到任务栏，但**焦点不能在另一个应用**
- 如果游戏和 GameInputTester 在**同一桌面**，来回切换焦点不会中断回放

---

## 故障排查

| 现象 | 原因 | 解决 |
|---|---|---|
| 按 F6 游戏没反应 | 焦点不在游戏 | 点开游戏窗口，让它在前台 |
| 按 F4 没弹映射窗口 | GameInputTester 没焦点 | 先点一下主窗口 |
| 录制时漏键 | 键没在白名单 | F4 加上这个键 |
| JSON 编辑器无法保存 | JSON 格式错 | 弹错误框提示哪里错，Alt+F 格式化后再试 |
| 编译报 `WindowsBase.dll not found` | 没装 Desktop Runtime | 重装 .NET 8 SDK 时勾上 Desktop |
| 双击 exe 弹"未识别的应用" | Windows Defender | "更多信息 → 仍要运行" |
| 回放比预期慢 1-2 秒 | 正常 — SendInput 是阻塞投递 | 调小 `CooldownMs` / `AfterWait` |
| 同一键被发多次 | 录制时手抖按了多次 | 录的时候松开再按下一个 |
| 启动报"SetWindowsHookEx 失败" | 反作弊 / AV 拦截钩子 | 关闭 AV 白名单，或换游戏（自己开发的应该没事） |
| 关闭程序偶发崩溃 | 已修：FormClosing 同步等回放结束 | 升级到最新代码 |
| GitHub Actions build 失败 | 网络/配置问题 | 看 Actions 日志 |

---

## 适用边界

- **目标**：自研 / 明确允许的 PC 游戏
- **支持**：US 104 键（字母/数字/符号/F1-F24/方向/编辑/修饰/小键盘）
- **不支持**：多媒体键、ACPI 键、厂商自定义键（罗技 G 键等走 HID 层）
- **焦点要求**：前台游戏窗口必须处于活动状态，SendInput 在失焦时投递无效

## 许可

仅供自研游戏开发测试使用，请勿用于绕过任何第三方游戏的反作弊系统。
