using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace FilenameTranslator;

public sealed class MainForm : Form
{
    private readonly BindingList<FileItem> _items = new();
    private readonly List<RenameRecord> _undoRecords = new();

    private readonly DataGridView _grid = new();
    private readonly TextBox _templateBox = new();
    private readonly ComboBox _languageBox = new();
    private readonly CheckBox _recursiveBox = new();
    private readonly Label _status = new();
    private readonly WebView2 _web = new();
    private readonly ComboBox _themeBox = new();
    private readonly Button _renameButton = new();

    private readonly Dictionary<string, (Color Accent, Color Bg, Color Panel, Color Text)> _themes = new()
    {
        ["紫色"] = (Color.FromArgb(108, 92, 231), Color.FromArgb(247, 247, 252), Color.White, Color.FromArgb(35,35,40)),
        ["蓝色"] = (Color.FromArgb(37, 99, 235), Color.FromArgb(246, 249, 255), Color.White, Color.FromArgb(35,35,40)),
        ["绿色"] = (Color.FromArgb(22, 163, 74), Color.FromArgb(246, 252, 248), Color.White, Color.FromArgb(35,35,40)),
        ["橙色"] = (Color.FromArgb(234, 88, 12), Color.FromArgb(255, 249, 245), Color.White, Color.FromArgb(35,35,40)),
        ["深色"] = (Color.FromArgb(139, 92, 246), Color.FromArgb(24, 24, 28), Color.FromArgb(39, 39, 42), Color.White),
    };

    public MainForm()
    {
        Text = "文件名翻译重命名工具 v1.0";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1500;
        Height = 920;
        MinimumSize = new Size(1100, 700);

        BuildUi();
        ApplyTheme("紫色");

        Shown += async (_, _) => await InitWebAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 730,
            Panel1MinSize = 560,
            Panel2MinSize = 360,
            BorderStyle = BorderStyle.None
        };
        root.Controls.Add(split, 0, 0);

        split.Panel1.Controls.Add(BuildLeftPanel());
        split.Panel2.Controls.Add(BuildRightPanel());

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Text = "就绪";
        root.Controls.Add(_status, 0, 1);
    }

    private Control BuildLeftPanel()
    {
        var outer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(6),
        };
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 10, 0, 0),
        };
        top.Controls.Add(MakeButton("添加文件夹", AddFolder));
        top.Controls.Add(MakeButton("添加文件", AddFiles));
        top.Controls.Add(MakeButton("清空列表", (_, _) => { _items.Clear(); UpdateStatus(); }));
        _recursiveBox.Text = "包含子文件夹";
        _recursiveBox.Checked = true;
        _recursiveBox.AutoSize = true;
        _recursiveBox.Margin = new Padding(14, 10, 8, 0);
        top.Controls.Add(_recursiveBox);

        _themeBox.Width = 100;
        _themeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _themeBox.Items.AddRange(_themes.Keys.Cast<object>().ToArray());
        _themeBox.SelectedIndex = 0;
        _themeBox.SelectedIndexChanged += (_, _) => ApplyTheme(_themeBox.Text);
        top.Controls.Add(new Label { Text = "主题：", AutoSize = true, Margin = new Padding(14, 12, 0, 0) });
        top.Controls.Add(_themeBox);

        outer.Controls.Add(top, 0, 0);

        var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(0, 8, 0, 8) };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        settings.Controls.Add(new Label { Text = "目标语言", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _languageBox.Dock = DockStyle.Fill;
        _languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageBox.Items.AddRange(new object[] { "中文", "英文", "日文", "韩文", "泰文", "越南文", "自定义" });
        _languageBox.SelectedIndex = 0;
        settings.Controls.Add(_languageBox, 1, 0);

        settings.Controls.Add(new Label { Text = "命名模板", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0);
        _templateBox.Dock = DockStyle.Fill;
        _templateBox.Text = "【{译名}】{原名}";
        _templateBox.TextChanged += (_, _) => RefreshFinalNames();
        settings.Controls.Add(_templateBox, 3, 0);

        var hint = new Label
        {
            Text = "可用变量：{译名}  {原名}  {序号}    扩展名自动保留，不参与翻译。",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray
        };
        settings.SetColumnSpan(hint, 4);
        settings.Controls.Add(hint, 0, 1);
        outer.Controls.Add(settings, 0, 1);

        ConfigureGrid();
        outer.Controls.Add(_grid, 0, 2);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, 10, 0, 0) };
        actions.Controls.Add(MakeButton("复制给豆包", CopyPrompt));
        actions.Controls.Add(MakeButton("从剪贴板导入", PasteTranslations));
        actions.Controls.Add(MakeButton("清空译名", (_, _) => { foreach (var i in _items) i.Translation = ""; RefreshFinalNames(); }));
        actions.Controls.Add(MakeButton("导出 CSV", ExportCsv));
        actions.Controls.Add(MakeButton("打开豆包外部浏览器", (_, _) => OpenExternal("https://www.doubao.com/")));
        outer.Controls.Add(actions, 0, 3);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) };
        _renameButton.Text = "开始重命名";
        _renameButton.AutoSize = true;
        _renameButton.Height = 38;
        _renameButton.Padding = new Padding(14, 4, 14, 4);
        _renameButton.Click += RenameSelected;
        bottom.Controls.Add(_renameButton);
        bottom.Controls.Add(MakeButton("撤销上次重命名", UndoRename));
        bottom.Controls.Add(MakeButton("全选/反选", ToggleSelection));
        outer.Controls.Add(bottom, 0, 4);

        return outer;
    }

    private Control BuildRightPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(6) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        bar.Controls.Add(new Label { Text = "豆包网页版", AutoSize = true, Font = new Font("Microsoft YaHei UI", 11, FontStyle.Bold), Margin = new Padding(0, 10, 16, 0) });
        bar.Controls.Add(MakeButton("首页", (_, _) => NavigateDoubao()));
        bar.Controls.Add(MakeButton("刷新", (_, _) => _web.Reload()));
        bar.Controls.Add(MakeButton("后退", (_, _) => { if (_web.CanGoBack) _web.GoBack(); }));
        bar.Controls.Add(MakeButton("前进", (_, _) => { if (_web.CanGoForward) _web.GoForward(); }));

        panel.Controls.Add(bar, 0, 0);
        _web.Dock = DockStyle.Fill;
        panel.Controls.Add(_web, 0, 1);
        return panel;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = true;
        _grid.DataSource = _items;

        _grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", HeaderText = "选", Width = 42 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "OriginalName", HeaderText = "原始名称", Width = 220, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Translation", HeaderText = "翻译名称", Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "FinalName", HeaderText = "最终名称", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Status", HeaderText = "状态", Width = 90, ReadOnly = true });

        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].DataPropertyName == "Translation")
                RefreshFinalNames();
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
    }

    private Button MakeButton(string text, EventHandler click)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            Padding = new Padding(9, 3, 9, 3),
            Margin = new Padding(3, 3, 6, 3),
            FlatStyle = FlatStyle.Flat
        };
        b.FlatAppearance.BorderSize = 1;
        b.Click += click;
        return b;
    }

    private void AddFolder(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog { Description = "选择需要批量处理的文件夹", UseDescriptionForTitle = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var option = _recursiveBox.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(dlg.SelectedPath, "*", option))
                AddPath(file);
            RefreshFinalNames();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "扫描文件夹失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddFiles(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Multiselect = true, Title = "选择一个或多个文件", Filter = "所有文件|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var file in dlg.FileNames) AddPath(file);
        RefreshFinalNames();
    }

    private void AddPath(string fullPath)
    {
        if (_items.Any(x => string.Equals(x.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))) return;

        var name = Path.GetFileName(fullPath);
        _items.Add(new FileItem
        {
            FullPath = fullPath,
            DirectoryPath = Path.GetDirectoryName(fullPath) ?? "",
            OriginalName = name,
            BaseName = Path.GetFileNameWithoutExtension(fullPath),
            Extension = Path.GetExtension(fullPath),
            Status = "待翻译"
        });
        UpdateStatus();
    }

    private string BuildFinalName(FileItem item, int index)
    {
        var translated = string.IsNullOrWhiteSpace(item.Translation) ? item.BaseName : item.Translation.Trim();
        var body = _templateBox.Text
            .Replace("{译名}", translated)
            .Replace("{原名}", item.BaseName)
            .Replace("{序号}", (index + 1).ToString("D3"));

        foreach (var c in Path.GetInvalidFileNameChars())
            body = body.Replace(c, '_');

        body = body.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(body)) body = item.BaseName;
        return body + item.Extension;
    }

    private void RefreshFinalNames()
    {
        for (int i = 0; i < _items.Count; i++)
            _items[i].FinalName = BuildFinalName(_items[i], i);

        _grid.Refresh();
        UpdateStatus();
    }

    private void CopyPrompt(object? sender, EventArgs e)
    {
        if (_items.Count == 0) { MessageBox.Show(this, "请先添加文件。"); return; }

        var lang = _languageBox.Text;
        var sb = new StringBuilder();
        sb.AppendLine($"请把下面的文件名称翻译成{lang}。");
        sb.AppendLine("要求：");
        sb.AppendLine("1. 保留每行编号不变。");
        sb.AppendLine("2. 只翻译名称本身，不翻译扩展名。");
        sb.AppendLine("3. 每行严格输出：编号 | 翻译后的名称");
        sb.AppendLine("4. 不要解释，不要使用 Markdown 表格，不要增加其他文字。");
        sb.AppendLine();

        for (int i = 0; i < _items.Count; i++)
            sb.AppendLine($"{i + 1:D3} | {_items[i].BaseName}");

        Clipboard.SetText(sb.ToString());
        _status.Text = $"已复制 {_items.Count} 个名称及翻译要求，可直接粘贴到右侧豆包。";
    }

    private void PasteTranslations(object? sender, EventArgs e)
    {
        if (!Clipboard.ContainsText()) { MessageBox.Show(this, "剪贴板里没有文本。"); return; }
        var text = Clipboard.GetText();
        int matched = 0;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            var m = Regex.Match(line, @"^\s*(\d{1,6})\s*(?:\||[\.、:：\-])\s*(.+?)\s*$");
            if (!m.Success) continue;
            if (!int.TryParse(m.Groups[1].Value, out var n)) continue;
            if (n < 1 || n > _items.Count) continue;

            var translation = m.Groups[2].Value.Trim();
            translation = Regex.Replace(translation, @"\.(mp3|wav|flac|m4a|aac|ogg|wma|mp4|mov|mkv)$", "", RegexOptions.IgnoreCase);
            _items[n - 1].Translation = translation;
            _items[n - 1].Status = "已翻译";
            matched++;
        }

        RefreshFinalNames();
        _status.Text = $"已从剪贴板匹配 {matched}/{_items.Count} 条翻译结果。";
        if (matched == 0)
            MessageBox.Show(this, "没有识别到可匹配的编号格式。\n建议豆包输出：001 | 翻译名称", "未匹配");
    }

    private void RenameSelected(object? sender, EventArgs e)
    {
        _grid.EndEdit();
        RefreshFinalNames();

        var targets = _items.Where(x => x.Selected).ToList();
        if (targets.Count == 0) { MessageBox.Show(this, "没有选中需要重命名的文件。"); return; }

        var conflicts = new List<string>();
        foreach (var item in targets)
        {
            var newPath = Path.Combine(item.DirectoryPath, item.FinalName);
            if (!string.Equals(newPath, item.FullPath, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
                conflicts.Add(item.FinalName);
        }
        if (conflicts.Count > 0)
        {
            MessageBox.Show(this, "发现重名冲突，已停止执行：\n\n" + string.Join("\n", conflicts.Take(12)), "重名冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this, $"即将重命名 {targets.Count} 个文件。\n\n确定执行吗？", "确认重命名", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        _undoRecords.Clear();
        int ok = 0, fail = 0;

        foreach (var item in targets)
        {
            try
            {
                if (!File.Exists(item.FullPath)) { item.Status = "文件不存在"; fail++; continue; }

                var newPath = Path.Combine(item.DirectoryPath, item.FinalName);
                if (string.Equals(newPath, item.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    item.Status = "无需修改";
                    continue;
                }

                File.Move(item.FullPath, newPath);
                _undoRecords.Add(new RenameRecord { OldPath = item.FullPath, NewPath = newPath });

                item.FullPath = newPath;
                item.OriginalName = Path.GetFileName(newPath);
                item.BaseName = Path.GetFileNameWithoutExtension(newPath);
                item.Extension = Path.GetExtension(newPath);
                item.Status = "成功";
                ok++;
            }
            catch (Exception ex)
            {
                item.Status = "失败";
                fail++;
                Debug.WriteLine(ex);
            }
        }

        RefreshFinalNames();
        _status.Text = $"重命名完成：成功 {ok}，失败 {fail}。";
        MessageBox.Show(this, $"执行完成。\n成功：{ok}\n失败：{fail}", "完成");
    }

    private void UndoRename(object? sender, EventArgs e)
    {
        if (_undoRecords.Count == 0) { MessageBox.Show(this, "没有可撤销的上次重命名记录。"); return; }
        if (MessageBox.Show(this, $"将撤销 {_undoRecords.Count} 个文件的上次重命名，是否继续？", "撤销确认", MessageBoxButtons.YesNo) != DialogResult.Yes)
            return;

        int ok = 0, fail = 0;
        foreach (var record in _undoRecords.AsEnumerable().Reverse())
        {
            try
            {
                if (File.Exists(record.NewPath) && !File.Exists(record.OldPath))
                {
                    File.Move(record.NewPath, record.OldPath);
                    ok++;
                }
                else fail++;
            }
            catch { fail++; }
        }

        _undoRecords.Clear();
        _status.Text = $"撤销完成：成功 {ok}，失败 {fail}。建议重新添加文件刷新列表。";
        MessageBox.Show(this, $"撤销完成。\n成功：{ok}\n失败：{fail}", "撤销完成");
    }

    private void ToggleSelection(object? sender, EventArgs e)
    {
        foreach (var item in _items) item.Selected = !item.Selected;
        _grid.Refresh();
    }

    private void ExportCsv(object? sender, EventArgs e)
    {
        if (_items.Count == 0) return;
        using var dlg = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = "文件名翻译列表.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        using var sw = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
        sw.WriteLine("原始路径,原始名称,翻译名称,最终名称,状态");
        foreach (var i in _items)
            sw.WriteLine(string.Join(",", Csv(i.FullPath), Csv(i.OriginalName), Csv(i.Translation), Csv(i.FinalName), Csv(i.Status)));
        _status.Text = "CSV 已导出。";
    }

    private async Task InitWebAsync()
    {
        try
        {
            _status.Text = "正在初始化豆包网页...";
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FilenameTranslator", "WebView2");
            Directory.CreateDirectory(profile);

            var env = await CoreWebView2Environment.CreateAsync(null, profile);
            await _web.EnsureCoreWebView2Async(env);

            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                _web.CoreWebView2.Navigate(e.Uri);
            };
            NavigateDoubao();
            _status.Text = "豆包网页已加载。第一次使用请正常登录。";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text = "未检测到 Microsoft Edge WebView2 Runtime。左侧功能仍可使用。";
            MessageBox.Show(this,
                "这台电脑未检测到 Microsoft Edge WebView2 Runtime。\n\n左侧文件重命名功能仍然可用；要使用右侧内嵌豆包，请安装 Microsoft 官方 WebView2 Runtime。",
                "缺少 WebView2", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            _status.Text = "豆包网页初始化失败，左侧功能仍可使用。";
            MessageBox.Show(this, "豆包网页初始化失败：\n" + ex.Message, "网页错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void NavigateDoubao()
    {
        try
        {
            if (_web.CoreWebView2 != null)
                _web.CoreWebView2.Navigate("https://www.doubao.com/");
            else
                _web.Source = new Uri("https://www.doubao.com/");
        }
        catch { }
    }

    private void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private void UpdateStatus()
    {
        var selected = _items.Count(x => x.Selected);
        var translated = _items.Count(x => !string.IsNullOrWhiteSpace(x.Translation));
        _status.Text = $"文件 {_items.Count} 个｜选中 {selected} 个｜已有译名 {translated} 个";
    }

    private void ApplyTheme(string name)
    {
        if (!_themes.TryGetValue(name, out var t)) return;
        BackColor = t.Bg;
        ForeColor = t.Text;
        _status.ForeColor = t.Text;

        ApplyThemeRecursive(this, t);
        _renameButton.BackColor = t.Accent;
        _renameButton.ForeColor = Color.White;
        _renameButton.FlatAppearance.BorderColor = t.Accent;

        _grid.BackgroundColor = t.Panel;
        _grid.DefaultCellStyle.BackColor = t.Panel;
        _grid.DefaultCellStyle.ForeColor = t.Text;
        _grid.DefaultCellStyle.SelectionBackColor = t.Accent;
        _grid.DefaultCellStyle.SelectionForeColor = Color.White;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = t.Accent;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        _grid.EnableHeadersVisualStyles = false;
    }

    private void ApplyThemeRecursive(Control c, (Color Accent, Color Bg, Color Panel, Color Text) t)
    {
        if (c is Form || c is TableLayoutPanel || c is SplitContainer || c is FlowLayoutPanel)
            c.BackColor = t.Bg;
        else if (c is Button b)
        {
            b.BackColor = t.Panel;
            b.ForeColor = t.Text;
            b.FlatAppearance.BorderColor = Color.FromArgb(190, 190, 200);
        }
        else if (c is TextBox || c is ComboBox)
        {
            c.BackColor = t.Panel;
            c.ForeColor = t.Text;
        }
        else if (c is Label || c is CheckBox)
            c.ForeColor = t.Text;

        foreach (Control child in c.Controls)
            ApplyThemeRecursive(child, t);
    }
}
