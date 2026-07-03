using System.Text.Json;

namespace GameInputTester;

public class EditConfigForm : Form
{
    private readonly TextBox _editor;
    private readonly Label _status;
    private readonly string _filePath;
    private bool _dirty;

    public EditConfigForm(string filePath)
    {
        _filePath = filePath;
        Text = $"编辑配置 — {Path.GetFileName(filePath)}";
        Width = 800; Height = 600;
        StartPosition = FormStartPosition.CenterParent;

        _editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 11),
            AcceptsTab = true,
            AcceptsReturn = true
        };
        LoadFile();

        _editor.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.S) { Save(); e.SuppressKeyPress = true; }
        };
        _editor.TextChanged += (_, _) => { _dirty = true; UpdateStatus(); };

        var toolbar = new ToolStrip();
        var btnFormat   = new ToolStripButton("格式化 (Alt+F)");
        var btnValidate = new ToolStripButton("校验 JSON (Alt+V)");
        var btnSave     = new ToolStripButton("保存 (Ctrl+S)");
        var btnReload   = new ToolStripButton("重新加载");
        btnFormat.Click   += (_, _) => FormatJson();
        btnValidate.Click += (_, _) => ValidateJson();
        btnSave.Click     += (_, _) => Save();
        btnReload.Click   += (_, _) => { if (ConfirmDiscard()) LoadFile(); };
        toolbar.Items.AddRange(new ToolStripItem[] { btnFormat, btnValidate, btnSave, btnReload });

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            BackColor = SystemColors.ControlLight,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 0, 0)
        };
        UpdateStatus();

        var btnBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var btnOk     = new Button { Text = "保存并关闭", Width = 110, Height = 28 };
        var btnCancel = new Button { Text = "取消",       Width = 80,  Height = 28 };
        btnOk.Click     += (_, _) => { if (Save()) { DialogResult = DialogResult.OK; Close(); } };
        btnCancel.Click += (_, _) => { if (!ConfirmDiscard()) return; DialogResult = DialogResult.Cancel; Close(); };
        btnBar.Controls.AddRange(new Control[] { btnOk, btnCancel });

        Controls.Add(_editor);
        Controls.Add(toolbar);
        Controls.Add(_status);
        Controls.Add(btnBar);

        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.Alt && e.KeyCode == Keys.F) FormatJson();
            if (e.Alt && e.KeyCode == Keys.V) ValidateJson();
        };
    }

    private void LoadFile()
    {
        try
        {
            var raw = File.ReadAllText(_filePath);
            using var doc = JsonDocument.Parse(raw);
            _editor.Text = JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
            _dirty = false;
        }
        catch (Exception ex)
        {
            _editor.Text = $"// 加载失败: {ex.Message}\n\n" + File.ReadAllText(_filePath);
            _dirty = false;
        }
        UpdateStatus();
    }

    private void FormatJson()
    {
        try
        {
            using var doc = JsonDocument.Parse(_editor.Text);
            _editor.Text = JsonSerializer.Serialize(doc.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
            _dirty = true;
            UpdateStatus("已格式化");
        }
        catch (Exception ex) { UpdateStatus($"格式化失败: {ex.Message}", isError: true); }
    }

    private void ValidateJson()
    {
        try
        {
            using var doc = JsonDocument.Parse(_editor.Text);
            var count = doc.RootElement.GetArrayLength();
            UpdateStatus($"✓ JSON 有效，{count} 项", isError: false);
        }
        catch (Exception ex) { UpdateStatus($"❌ JSON 无效: {ex.Message}", isError: true); }
    }

    private bool Save()
    {
        try
        {
            using var doc = JsonDocument.Parse(_editor.Text);
            File.WriteAllText(_filePath, _editor.Text);
            _dirty = false;
            UpdateStatus($"💾 已保存 {DateTime.Now:HH:mm:ss}");
            return true;
        }
        catch (Exception ex) { UpdateStatus($"❌ 保存失败: {ex.Message}", isError: true); return false; }
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show("有未保存的修改，丢弃吗？", "确认",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        return r == DialogResult.Yes;
    }

    private void UpdateStatus(string? msg = null, bool isError = false)
    {
        _status.Text = msg ?? $"{(_dirty ? "●" : "○")}  {Path.GetFileName(_filePath)} | {_editor.Text.Length} 字符 | {_editor.Lines.Length} 行";
        _status.BackColor = isError ? Color.MistyRose : SystemColors.ControlLight;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_dirty && e.CloseReason == CloseReason.UserClosing)
        {
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
        }
        base.OnClosing(e);
    }
}
