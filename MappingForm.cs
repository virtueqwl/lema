using System.Data;

namespace GameInputTester;

public class MappingForm : Form
{
    public DataTable Mapping { get; } = new();

    private static readonly Keys[] UsKeyboardKeys = BuildUsKeyList();

    public MappingForm()
    {
        Text = "键位映射 — 冷却 / 抖动 / 加权 / 操作间隔";
        Width = 980; Height = 520;
        StartPosition = FormStartPosition.CenterScreen;

        Mapping.Columns.Add("Logical",                typeof(string));
        Mapping.Columns.Add("Physical",               typeof(Keys));
        Mapping.Columns.Add("CooldownMs",             typeof(int)); // 基础冷却
        Mapping.Columns.Add("JitterMs",               typeof(int)); // 0~+N 抖动
        Mapping.Columns.Add("AfterTriggerWaitMinMs",  typeof(int)); // 触发后等待 min
        Mapping.Columns.Add("AfterTriggerWaitMaxMs",  typeof(int)); // 触发后等待 max
        Mapping.Columns.Add("Weight",                 typeof(int)); // 加权（0=禁用）

        // 默认示例：RPG 技能循环（A=8s±1 + 触发后 1-2s, 权重 1）
        Mapping.Rows.Add("A", Keys.A, 8000, 1000, 1000, 2000, 1);
        Mapping.Rows.Add("B", Keys.S, 5000,  500, 1000, 1500, 1);
        Mapping.Rows.Add("C", Keys.D, 3000,  300, 1000, 1500, 1);
        Mapping.Rows.Add("D", Keys.F, 1000,  200, 1000, 1500, 1);

        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            DataSource = Mapping,
            AllowUserToAddRows = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        var physCol = new DataGridViewComboBoxColumn
        {
            Name = "Physical",
            DataPropertyName = "Physical",
            HeaderText = "物理键（US 104 键）",
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        physCol.Items.AddRange(UsKeyboardKeys.Cast<object>().ToArray());
        grid.Columns.Remove(grid.Columns["Physical"]);
        grid.Columns.Insert(1, physCol);

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            BackColor = SystemColors.Info,
            ForeColor = SystemColors.InfoText,
            Text = "  CooldownMs=冷却  JitterMs=0~+N 抖动  "
                 + "AfterTriggerWaitMin/MaxMs=发完后到下一个键的等待（区间随机）  "
                 + "Weight=加权（0=禁用）\n"
                 + "  规则：发完键 → 等 after 区间 → 选下一个 → 等该键冷却好 → 发",
            TextAlign = ContentAlignment.MiddleLeft
        };

        var ok = new Button { Text = "保存", Dock = DockStyle.Bottom, Height = 36 };
        ok.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        Controls.Add(grid);
        Controls.Add(ok);
        Controls.Add(hint);
    }

    private static Keys[] BuildUsKeyList()
    {
        var list = new List<Keys>();

        for (var c = 'A'; c <= 'Z'; c++)
            list.Add((Keys)Enum.Parse(typeof(Keys), c.ToString()));
        for (var c = '0'; c <= '9'; c++)
            list.Add((Keys)Enum.Parse(typeof(Keys), "D" + c));
        for (int i = 1; i <= 24; i++)
            list.Add((Keys)Enum.Parse(typeof(Keys), "F" + i));

        list.AddRange(new[] {
            Keys.LShiftKey, Keys.RShiftKey,
            Keys.LControlKey, Keys.RControlKey,
            Keys.LMenu, Keys.RMenu,
            Keys.CapsLock, Keys.NumLock, Keys.ScrollLock
        });
        list.AddRange(new[] {
            Keys.Up, Keys.Down, Keys.Left, Keys.Right,
            Keys.Home, Keys.End, Keys.Insert, Keys.Delete,
            Keys.PageUp, Keys.PageDown,
            Keys.Print, Keys.Pause, Keys.Escape,
            Keys.Back, Keys.Tab, Keys.Return, Keys.Space
        });
        list.AddRange(new[] {
            Keys.Oemtilde, Keys.OemMinus, Keys.Oemplus,
            Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemBackslash,
            Keys.OemSemicolon, Keys.OemQuotes,
            Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion,
        });
        for (var c = '0'; c <= '9'; c++)
            list.Add((Keys)Enum.Parse(typeof(Keys), "NumPad" + c));
        list.AddRange(new[] {
            Keys.Multiply, Keys.Add, Keys.Separator,
            Keys.Subtract, Keys.Decimal, Keys.Divide
        });

        return list.Distinct().OrderBy(k => k).ToArray();
    }
}
