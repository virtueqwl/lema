using System.Data;
using System.Text.Json;
using static GameInputTester.WinApi;

namespace GameInputTester;

public class MainForm : Form
{
    [STAThread]
    static void Main()
    {
        Application.Run(new MainForm());
    }

    private readonly List<RecordedKey> _buffer = new();
    private readonly object _mapLock = new();  // 保护 _mapping DataTable 并发访问
    private Recorder _rec = null!;
    private ScriptPlayer? _scriptPlayer;
    private CooldownPlayer? _cooldownPlayer;
    private CancellationTokenSource? _playCts;
    private Task? _playTask;
    private DataTable _mapping = new();
    private RichTextBox _log = null!;
    private ListBox _configList = null!;
    private ComboBox _modeCombo = null!;
    private NumericUpDown _roundsBox = null!;
    private CheckBox _resetCooldownBox = null!;
    private long _lastRecordStopMs;
    private string? _activeConfigFile;

    private static readonly string ConfigsDir = Path.Combine(AppContext.BaseDirectory, "configs");
    private const string LastConfigFile = "last_config.txt";

    public MainForm()
    {
        Text = "GameInputTester";
        Width = 1080; Height = 640;
        ShowInTaskbar = true;

        // ===== 顶部菜单 =====
        var menu = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("文件");
        var miImport   = new ToolStripMenuItem("导入配置...");
        var miSave     = new ToolStripMenuItem("保存为配置...");
        var miOpenDir  = new ToolStripMenuItem("打开配置目录");
        var miSep      = new ToolStripSeparator();
        var miExit     = new ToolStripMenuItem("退出");
        miImport.Click  += (_, _) => ImportFromDialog();
        miSave.Click    += (_, _) => SaveScriptAsConfig();
        miOpenDir.Click += (_, _) => OpenConfigDir();
        miExit.Click    += (_, _) => Application.Exit();
        fileMenu.DropDownItems.AddRange(new ToolStripItem[] {
            miImport, miSave, miOpenDir, miSep, miExit
        });
        menu.Items.Add(fileMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);

        // ===== 模式 + 轮数 条 =====
        var modePanel = new FlowLayoutPanel {
            Dock = DockStyle.Top, Height = 32,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(6, 4, 6, 4)
        };
        var lblMode = new Label { Text = "模式:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 4, 0) };
        _modeCombo = new ComboBox {
            Width = 220, DropDownStyle = ComboBoxStyle.DropDownList
        };
        _modeCombo.Items.AddRange(new[] {
            "冷却随机（加权，按配置）",
            "脚本回放（旧：按录制顺序）"
        });
        _modeCombo.SelectedIndex = 0;   // 默认冷却随机
        var lblRounds = new Label { Text = "轮数 (0=无限):", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(20, 6, 4, 0) };
        _roundsBox = new NumericUpDown { Width = 80, Minimum = 0, Maximum = 9999, Value = 5 };
        _resetCooldownBox = new CheckBox {
            Text = "每轮重置冷却（独立测试）",
            AutoSize = true,
            Checked = false,
            Margin = new Padding(20, 6, 4, 0)
        };
        _resetCooldownBox.CheckedChanged += (_, _) => {
            Log($"冷却模式: {(_resetCooldownBox.Checked ? "每轮重置" : "跨轮累计")}");
        };
        modePanel.Controls.AddRange(new Control[] {
            lblMode, _modeCombo, lblRounds, _roundsBox, _resetCooldownBox
        });
        Controls.Add(modePanel);

        // ===== SplitContainer =====
        var split = new SplitContainer {
            Dock = DockStyle.Fill,
            SplitterDistance = 220,
            FixedPanel = FixedPanel.Panel1
        };

        // 左侧：配置列表
        var leftPanel = new Panel { Dock = DockStyle.Fill };
        var lblConfigs  = new Label { Text = "配置文件 (configs/)", Dock = DockStyle.Top, Height = 24, BackColor = SystemColors.ControlLight, TextAlign = ContentAlignment.MiddleLeft };
        var btnRefresh  = new Button { Text = "刷新列表", Dock = DockStyle.Top, Height = 28 };
        var btnOpenDir2 = new Button { Text = "打开目录", Dock = DockStyle.Top, Height = 28 };
        var btnDelete   = new Button { Text = "删除当前", Dock = DockStyle.Top, Height = 28 };
        _configList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        btnRefresh.Click += (_, _) => LoadConfigList();
        btnOpenDir2.Click += (_, _) => OpenConfigDir();
        btnDelete.Click  += (_, _) => DeleteActiveConfig();
        _configList.DoubleClick += (_, _) => LoadSelectedConfig();
        leftPanel.Controls.Add(_configList);
        leftPanel.Controls.Add(btnDelete);
        leftPanel.Controls.Add(btnOpenDir2);
        leftPanel.Controls.Add(btnRefresh);
        leftPanel.Controls.Add(lblConfigs);

        // 右侧：日志 + 控制按钮
        var rightPanel = new Panel { Dock = DockStyle.Fill };
        _log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 10) };
        var btnPanel = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 60, ColumnCount = 5 };
        for (int i = 0; i < 5; i++) btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        var btnMapping = new Button { Text = "键位映射 (F4)",     Dock = DockStyle.Fill };
        var btnRecord  = new Button { Text = "录制 (F5 切换)",    Dock = DockStyle.Fill };
        var btnPlay    = new Button { Text = "回放 (F6)",         Dock = DockStyle.Fill };
        var btnStop    = new Button { Text = "停止 (F7)",         Dock = DockStyle.Fill };
        var btnSave    = new Button { Text = "保存配置 (Ctrl+S)", Dock = DockStyle.Fill };
        btnMapping.Click += (_, _) => OpenMapping();
        btnRecord.Click  += (_, _) => ToggleRecord();
        btnPlay.Click    += (_, _) => StartPlay();
        btnStop.Click    += (_, _) => StopPlay();
        btnSave.Click    += (_, _) => SaveScriptAsConfig();
        btnPanel.Controls.Add(btnMapping, 0, 0);
        btnPanel.Controls.Add(btnRecord,  1, 0);
        btnPanel.Controls.Add(btnPlay,    2, 0);
        btnPanel.Controls.Add(btnStop,    3, 0);
        btnPanel.Controls.Add(btnSave,    4, 0);
        rightPanel.Controls.Add(_log);
        rightPanel.Controls.Add(btnPanel);

        split.Panel1.Controls.Add(leftPanel);
        split.Panel2.Controls.Add(rightPanel);
        Controls.Add(split);

        KeyPreview = true;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) SaveScriptAsConfig(); };

        // 全局热键
        RegisterHotKey(IntPtr.Zero, 1, 0, (uint)Keys.F4);
        RegisterHotKey(IntPtr.Zero, 2, 0, (uint)Keys.F5);
        RegisterHotKey(IntPtr.Zero, 3, 0, (uint)Keys.F6);
        RegisterHotKey(IntPtr.Zero, 4, 0, (uint)Keys.F7);

        _mapping = LoadMappingOrDefault();
        _rec = new Recorder {
            WatchKeys = _mapping.AsEnumerable().Select(r => (Keys)r["Physical"]).ToHashSet(),
            // LogicalNameOf 可能在钩子线程被调用 → 必须锁
            LogicalNameOf = k => {
                lock (_mapLock) {
                    return _mapping.AsEnumerable()
                        .Where(r => (Keys)r["Physical"] == k)
                        .Select(r => (string)r["Logical"])
                        .FirstOrDefault() ?? k.ToString();
                }
            },
            // OnEvent 派发到 UI 线程，钩子线程立即返回
            OnEvent = evt => {
                try { BeginInvoke(() => OnRecorded(evt)); } catch { /* form 已销毁 */ }
            }
        };

        Shown += (_, _) => { LoadConfigList(); TryRestoreLastConfig(); };
        // 同步 handler：FormClosing 不支持 await（WinForms 不会等异步 lambda 跑完）
        FormClosing += (_, _) => {
            // 1. 取消回放
            _playCts?.Cancel();
            // 2. 同步等待回放 Task 结束（最多 1 秒）
            //    避免 SendInput 在 runtime 卸载后执行导致进程崩溃
            if (_playTask != null) {
                try { _playTask.Wait(1000); } catch { }
            }
            // 3. 释放钩子（必须在 Form 销毁前）
            try { _rec.Dispose(); } catch { }
            // 4. 写 last_config.txt
            if (_activeConfigFile != null)
                try {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, LastConfigFile), _activeConfigFile);
                } catch { }
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case 1: OpenMapping(); break;
                case 2: ToggleRecord(); break;
                case 3: StartPlay(); break;
                case 4: StopPlay(); break;
            }
        }
        base.WndProc(ref m);
    }

    // ===== 录制 =====

    private void OnRecorded(RecordedKey rk)
    {
        // 现在 OnRecorded 只在 UI 线程被调用（钩子已 BeginInvoke 派发）
        // 但 _buffer 可能在其他线程被读（SaveScriptAsConfig）→ 简化：用 ConcurrentBag 或锁
        // 实际：只有 UI 线程访问 _buffer，所以这里不需要额外锁
        if (IsDisposed || Disposing) return;
        try
        {
            _buffer.Add(rk);
            _log.AppendText($"[{rk.TimestampMs,6}ms] {rk.LogicalKey} {rk.Event}\n");
        }
        catch (ObjectDisposedException) { /* 关窗瞬间 */ }
    }

    private void ToggleRecord()
    {
        if (_rec.WatchKeys.Count == 0) { Log("⚠ 先在映射里加键"); return; }
        if (_buffer.Count == 0 || _lastRecordStopMs == 0
            || Environment.TickCount64 - _lastRecordStopMs > 2000)
        {
            _buffer.Clear();
            _rec.Start();
            _lastRecordStopMs = 0;
            Log("● REC start");
        }
        else
        {
            _rec.Stop();
            _lastRecordStopMs = Environment.TickCount64;
            Log($"■ REC done ({_buffer.Count} events)");
        }
    }

    // ===== 回放（两种模式） =====

    private void StartPlay()
    {
        // 启动前先停掉旧的回放（避免并发执行）
        if (_playTask != null && !_playTask.IsCompleted) {
            _playCts?.Cancel();
            try { _playTask.Wait(500); } catch { }
        }
        if (_modeCombo.SelectedIndex == 1) PlayScript();
        else PlayCooldown();
    }

    private void PlayCooldown()
    {
        List<DataRow> rows;
        lock (_mapLock) {
            rows = _mapping.AsEnumerable().ToList();
        }
        var slots = rows
            .Where(r => (int)r["Weight"] > 0)
            .Select(r => new CooldownPlayer.Slot(
                (string)r["Logical"],
                (Keys)r["Physical"],
                (int)r["CooldownMs"],
                (int)r["JitterMs"],
                (int)r["AfterTriggerWaitMinMs"],
                (int)r["AfterTriggerWaitMaxMs"],
                (int)r["Weight"]))
            .ToList();
        if (slots.Count == 0) { Log("⚠ 没有 Weight > 0 的键"); return; }

        // 释放旧 CTS 避免 handle 泄漏
        try { _playCts?.Dispose(); } catch { }
        _cooldownPlayer = new CooldownPlayer {
            Slots = slots,
            Rounds = (int)_roundsBox.Value,
            ResetLastTriggerPerRound = _resetCooldownBox.Checked
        };
        _playCts = new CancellationTokenSource();
        var progress = new Progress<string>(s => Log(s));
        var token = _playCts.Token;
        _playTask = Task.Run(() => _cooldownPlayer.RunAsync(token, progress));
        Log($"▶ COOLDOWN | {slots.Count} 键 | {(_roundsBox.Value == 0 ? "∞" : _roundsBox.Value.ToString())} 轮 | 冷却: {(_resetCooldownBox.Checked ? "每轮重置" : "跨轮累计")}");
    }

    private void PlayScript()
    {
        var steps = _buffer.Where(e => e.Event == RecordEvent.Down)
            .Select(e => ToScriptStep(e.PhysicalKey, e.LogicalKey))
            .ToList();
        if (steps.Count == 0) { Log("⚠ 没有可回放的动作"); return; }

        // 释放旧 CTS 避免 handle 泄漏
        try { _playCts?.Dispose(); } catch { }
        _scriptPlayer = new ScriptPlayer { Steps = steps };
        _playCts = new CancellationTokenSource();
        var progress = new Progress<string>(s => Log(s));
        var token = _playCts.Token;
        _playTask = Task.Run(() => _scriptPlayer.RunAsync(token, progress));
        Log($"▶ SCRIPT | {steps.Count} steps");
    }

    private void StopPlay()
    {
        _playCts?.Cancel();
        Log("■ STOP");
    }

    private ScriptPlayer.Step ToScriptStep(Keys physical, string logical)
    {
        DataRow? row;
        lock (_mapLock) {
            row = _mapping.AsEnumerable().FirstOrDefault(r => (Keys)r["Physical"] == physical);
        }
        if (row == null) return new ScriptPlayer.Step(logical, physical, 800, 1200);
        // 脚本模式：把 CooldownMs 当 WaitMin，CooldownMs + JitterMs 当 WaitMax
        int cd = (int)row["CooldownMs"];
        int j  = (int)row["JitterMs"];
        return new ScriptPlayer.Step(
            (string)row["Logical"],
            (Keys)row["Physical"],
            cd,
            cd + j);
    }

    // ===== 映射管理 =====

    private void OpenMapping()
    {
        using var f = new MappingForm();
        if (f.ShowDialog() == DialogResult.OK)
        {
            lock (_mapLock) {
                _mapping = f.Mapping.Copy();
            }
            PersistMapping();
            _rec.WatchKeys = _mapping.AsEnumerable().Select(r => (Keys)r["Physical"]).ToHashSet();
            Log("✓ 映射已更新");
        }
    }

    private DataTable LoadMappingOrDefault()
    {
        var t = new DataTable();
        t.Columns.Add("Logical",               typeof(string));
        t.Columns.Add("Physical",              typeof(Keys));
        t.Columns.Add("CooldownMs",            typeof(int));
        t.Columns.Add("JitterMs",              typeof(int));
        t.Columns.Add("AfterTriggerWaitMinMs", typeof(int));
        t.Columns.Add("AfterTriggerWaitMaxMs", typeof(int));
        t.Columns.Add("Weight",                typeof(int));
        if (File.Exists("settings.json"))
        {
            foreach (var item in JsonSerializer.Deserialize<List<MappingItem>>(File.ReadAllText("settings.json"))!)
                t.Rows.Add(
                    item.Logical,
                    Enum.Parse<Keys>(item.Physical),
                    item.CooldownMs,
                    item.JitterMs,
                    item.AfterTriggerWaitMinMs,
                    item.AfterTriggerWaitMaxMs,
                    item.Weight <= 0 ? 1 : item.Weight);
        }
        else
        {
            // 默认示例：A=8s±1 + 触发后 1-2s
            t.Rows.Add("A", Keys.A, 8000, 1000, 1000, 2000, 1);
            t.Rows.Add("B", Keys.S, 5000,  500, 1000, 1500, 1);
            t.Rows.Add("C", Keys.D, 3000,  300, 1000, 1500, 1);
            t.Rows.Add("D", Keys.F, 1000,  200, 1000, 1500, 1);
        }
        return t;
    }

    private void PersistMapping()
    {
        // 拷贝数据时也加锁（虽然调用方是 UI 线程，但保持一致性）
        List<MappingItem> items;
        lock (_mapLock) {
            items = _mapping.AsEnumerable().Select(r => new MappingItem {
                Logical                = (string)r["Logical"],
                Physical               = ((Keys)r["Physical"]).ToString(),
                CooldownMs             = (int)r["CooldownMs"],
                JitterMs               = (int)r["JitterMs"],
                AfterTriggerWaitMinMs  = (int)r["AfterTriggerWaitMinMs"],
                AfterTriggerWaitMaxMs  = (int)r["AfterTriggerWaitMaxMs"],
                Weight                 = (int)r["Weight"]
            }).ToList();
        }
        File.WriteAllText("settings.json",
            JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ===== 配置文件目录 =====

    private void EnsureConfigsDir()
    {
        if (!Directory.Exists(ConfigsDir)) Directory.CreateDirectory(ConfigsDir);
    }

    private void LoadConfigList()
    {
        EnsureConfigsDir();
        _configList.Items.Clear();
        foreach (var f in Directory.GetFiles(ConfigsDir, "*.json").OrderBy(p => p))
            _configList.Items.Add(Path.GetFileName(f));
        Log($"已加载 {_configList.Items.Count} 个配置");
    }

    private void TryRestoreLastConfig()
    {
        var last = Path.Combine(AppContext.BaseDirectory, LastConfigFile);
        if (!File.Exists(last)) return;
        var name = File.ReadAllText(last).Trim();
        var idx = _configList.Items.IndexOf(name);
        if (idx >= 0) { _configList.SelectedIndex = idx; LoadSelectedConfig(); }
    }

    private void LoadSelectedConfig()
    {
        if (_configList.SelectedItem is not string name) return;
        var path = Path.Combine(ConfigsDir, name);
        ImportFromPath(path);
        _activeConfigFile = name;
        Text = $"GameInputTester — {name}";
        Log($"▶ 切换到配置: {name}");
    }

    private void OpenConfigDir()
    {
        EnsureConfigsDir();
        // UseShellExecute=true 让 Windows 自己处理路径引号（避免 Program Files 等含空格路径被截断）
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
            FileName = ConfigsDir,
            UseShellExecute = true
        });
    }

    private void DeleteActiveConfig()
    {
        if (_activeConfigFile == null) { Log("⚠ 未选中配置"); return; }
        var path = Path.Combine(ConfigsDir, _activeConfigFile);
        if (MessageBox.Show($"删除配置 {_activeConfigFile}？", "确认",
            MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        File.Delete(path);
        Log($"🗑 已删除 {_activeConfigFile}");
        _activeConfigFile = null;
        Text = "GameInputTester";
        LoadConfigList();
    }

    // ===== 导入 / 保存 =====

    private void ImportFromDialog()
    {
        using var ofd = new OpenFileDialog {
            Title = "导入配置",
            Filter = "JSON (*.json)|*.json|所有文件 (*.*)|*.*",
            InitialDirectory = Directory.Exists(ConfigsDir) ? ConfigsDir : AppContext.BaseDirectory
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;
        ImportFromPath(ofd.FileName);
    }

    private void ImportFromPath(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<ImportedStep>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items == null || items.Count == 0) { Log("⚠ 文件为空"); return; }

            bool isScript = items[0].LogicalKey != null;
            if (isScript)
            {
                _buffer.Clear();
                long t = 0;
                foreach (var it in items)
                {
                    if (!Enum.TryParse<Keys>(it.Physical, out var pk)) continue;
                    _buffer.Add(new RecordedKey(
                        it.LogicalKey ?? pk.ToString(), pk, RecordEvent.Down, t));
                    t += 200;
                }
                Log($"📥 已导入脚本: {Path.GetFileName(path)} ({items.Count} 步)");
            }
            else
            {
                // #7 修复：建新 DataTable 替换，避免 Clear() + 重建列时 DataGridView 闪空
                var newMap = new DataTable();
                newMap.Columns.Add("Logical",               typeof(string));
                newMap.Columns.Add("Physical",              typeof(Keys));
                newMap.Columns.Add("CooldownMs",            typeof(int));
                newMap.Columns.Add("JitterMs",              typeof(int));
                newMap.Columns.Add("AfterTriggerWaitMinMs", typeof(int));
                newMap.Columns.Add("AfterTriggerWaitMaxMs", typeof(int));
                newMap.Columns.Add("Weight",                typeof(int));
                foreach (var it in items)
                {
                    if (!Enum.TryParse<Keys>(it.Physical, out var pk)) continue;
                    int cd = it.CooldownMs;
                    int jt = it.JitterMs;
                    int w0 = it.AfterTriggerWaitMinMs;   // 兼容老字段
                    int w1 = it.AfterTriggerWaitMaxMs;
                    int w  = it.Weight <= 0 ? 1 : it.Weight;
                    // 兼容老 WaitMinMs/WaitMaxMs 字段
                    if (cd == 0 && jt == 0 && w0 == 0 && w1 == 0
                        && (it.WaitMinMs > 0 || it.WaitMaxMs > 0))
                    {
                        cd = it.WaitMinMs;
                        jt = Math.Max(0, it.WaitMaxMs - it.WaitMinMs);
                        w0 = it.WaitMinMs;
                        w1 = it.WaitMaxMs;
                    }
                    newMap.Rows.Add(
                        it.Logical ?? pk.ToString(),
                        pk, cd, jt, w0, w1, w);
                }
                lock (_mapLock) {
                    _mapping = newMap;
                }
                PersistMapping();
                _rec.WatchKeys = _mapping.AsEnumerable().Select(r => (Keys)r["Physical"]).ToHashSet();
                Log($"📥 已导入映射: {Path.GetFileName(path)} ({items.Count} 行)");
            }
        }
        catch (JsonException jx) { Log($"❌ JSON 解析失败: {jx.Message}"); }
        catch (Exception ex)     { Log($"❌ 导入失败: {ex.Message}"); }
    }

    private void SaveScriptAsConfig()
    {
        EnsureConfigsDir();
        using var sfd = new SaveFileDialog {
            Title = "保存为配置",
            Filter = "JSON (*.json)|*.json",
            InitialDirectory = ConfigsDir,
            FileName = $"script_{DateTime.Now:yyyyMMdd_HHmmss}.json"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        // #9 修复：导出全字段（CooldownMs/JitterMs/AfterWait/Weight），不再丢 after / weight
        var steps = _buffer.Where(e => e.Event == RecordEvent.Down)
            .Select(e => {
                DataRow? row;
                lock (_mapLock) {
                    row = _mapping.AsEnumerable().FirstOrDefault(r => (Keys)r["Physical"] == e.PhysicalKey);
                }
                int cd = (int)(row?["CooldownMs"] ?? 1000);
                int jt = (int)(row?["JitterMs"] ?? 0);
                int aMin = (int)(row?["AfterTriggerWaitMinMs"] ?? 0);
                int aMax = (int)(row?["AfterTriggerWaitMaxMs"] ?? 0);
                int w    = (int)(row?["Weight"] ?? 1);
                return new {
                    LogicalKey              = e.LogicalKey,
                    Physical                 = e.PhysicalKey.ToString(),
                    CooldownMs               = cd,
                    JitterMs                 = jt,
                    AfterTriggerWaitMinMs    = aMin,
                    AfterTriggerWaitMaxMs    = aMax,
                    Weight                   = w
                };
            }).ToList();
        File.WriteAllText(sfd.FileName,
            JsonSerializer.Serialize(steps, new JsonSerializerOptions { WriteIndented = true }));
        Log($"💾 已保存脚本配置: {Path.GetFileName(sfd.FileName)}");
        LoadConfigList();
        var name = Path.GetFileName(sfd.FileName);
        var idx = _configList.Items.IndexOf(name);
        if (idx >= 0) { _configList.SelectedIndex = idx; _activeConfigFile = name; }
    }

    private void Log(string s)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            _log.AppendText($"[{Environment.TickCount64 % 100000}ms] {s}\n");
            // 限制日志行数，避免长时间运行内存爆炸
            if (_log.Lines.Length > 5000)
            {
                _log.SuspendLayout();
                var keep = string.Join("\n", _log.Lines.Skip(1000));
                _log.Text = keep + "\n";
                _log.SelectionStart = _log.Text.Length;
                _log.ScrollToCaret();
                _log.ResumeLayout();
            }
        }
        catch (ObjectDisposedException) { /* 关窗瞬间 */ }
    }

    // settings.json / 键位映射 配置 DTO
    private record MappingItem(
        string Logical, string Physical,
        int CooldownMs, int JitterMs,
        int AfterTriggerWaitMinMs, int AfterTriggerWaitMaxMs,
        int Weight);

    // 通用导入 DTO（兼容老脚本 + 新映射）
    private class ImportedStep
    {
        // 新映射字段
        public string? Logical                { get; set; }
        public string? Physical               { get; set; }
        public int     CooldownMs             { get; set; }
        public int     JitterMs               { get; set; }
        public int     AfterTriggerWaitMinMs  { get; set; }
        public int     AfterTriggerWaitMaxMs  { get; set; }
        public int     Weight                 { get; set; }

        // 老脚本/老映射字段
        public int     WaitMinMs              { get; set; }
        public int     WaitMaxMs              { get; set; }
        public string? LogicalKey             { get; set; }
    }
}
