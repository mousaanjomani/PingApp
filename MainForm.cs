using System.Media;
using System.Net.NetworkInformation;
using System.Text.Json;

namespace PingApp;

public class MainForm : Form
{
    private const int MaxTargets = 10;

    private sealed class TargetState
    {
        public string Alias = "";
        public string Host = "";
        public bool? LastReachable; // null = هنوز نتیجه‌ای نداریم
        public bool PingInProgress;
    }

    private sealed record TargetConfig(string Alias, string Host);

    private readonly List<TargetState> _targets = new();
    private readonly DataGridView _grid;
    private readonly TextBox _aliasTextBox;
    private readonly TextBox _hostTextBox;
    private readonly Button _addButton;
    private readonly Button _removeButton;
    private readonly Button _startStopButton;
    private readonly NumericUpDown _intervalUpDown;
    private readonly CheckBox _alertOnDisconnectCheckBox;
    private readonly Label _summaryLabel;
    private readonly ListBox _logListBox;
    private readonly System.Windows.Forms.Timer _pingTimer;
    private readonly NotifyIcon _trayIcon;

    private bool _running;

    private static string ConfigPath =>
        Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "targets.json");

    public MainForm()
    {
        Text = "PingApp — هشدار اتصال";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(600, 500);
        StartPosition = FormStartPosition.CenterScreen;

        // ردیف افزودن مقصد
        var aliasLabel = new Label { Text = "اسم مستعار:", Location = new Point(8, 16), AutoSize = true };
        _aliasTextBox = new TextBox { Location = new Point(84, 12), Width = 120 };

        var hostLabel = new Label { Text = "IP یا هاست:", Location = new Point(216, 16), AutoSize = true };
        _hostTextBox = new TextBox
        {
            Location = new Point(292, 12),
            Width = 160,
            RightToLeft = RightToLeft.No,
            TextAlign = HorizontalAlignment.Left,
        };

        _addButton = new Button { Text = "افزودن +", Location = new Point(464, 10), Size = new Size(124, 28) };
        _addButton.Click += OnAddClicked;

        // جدول مقصدها
        _grid = new DataGridView
        {
            Location = new Point(12, 48),
            Size = new Size(576, 230),
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            ReadOnly = true,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "اسم مستعار", FillWeight = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "آدرس", FillWeight = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "وضعیت", FillWeight = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "پاسخ (ms)", FillWeight = 55 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "آخرین تغییر", FillWeight = 85 });

        // ردیف کنترل‌ها
        _startStopButton = new Button { Text = "شروع", Location = new Point(12, 288), Size = new Size(110, 40) };
        _startStopButton.Click += OnStartStopClicked;

        _removeButton = new Button { Text = "حذف ردیف انتخابی", Location = new Point(134, 293), Size = new Size(130, 30) };
        _removeButton.Click += OnRemoveClicked;

        var intervalLabel = new Label { Text = "فاصله بررسی (ثانیه):", Location = new Point(278, 300), AutoSize = true };
        _intervalUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 3600,
            Value = 2,
            Location = new Point(400, 296),
            Width = 60,
        };

        _alertOnDisconnectCheckBox = new CheckBox
        {
            Text = "هشدار قطع ارتباط",
            Location = new Point(472, 298),
            AutoSize = true,
        };

        _summaryLabel = new Label
        {
            Text = "آماده",
            Location = new Point(12, 336),
            Size = new Size(576, 28),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Gainsboro,
        };

        _logListBox = new ListBox
        {
            Location = new Point(12, 372),
            Size = new Size(576, 116),
            IntegralHeight = false,
        };

        Controls.AddRange(new Control[]
        {
            aliasLabel, _aliasTextBox, hostLabel, _hostTextBox, _addButton,
            _grid, _startStopButton, _removeButton, intervalLabel, _intervalUpDown,
            _alertOnDisconnectCheckBox, _summaryLabel, _logListBox,
        });

        var trayMenu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        trayMenu.Items.Add("نمایش پنجره", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("خروج", null, (_, _) => Close());

        _trayIcon = new NotifyIcon
        {
            Text = "PingApp — هشدار اتصال",
            Icon = SystemIcons.Application,
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        _pingTimer = new System.Windows.Forms.Timer();
        _pingTimer.Tick += (_, _) => CheckAllTargets();

        LoadTargets();
        if (_targets.Count == 0)
            AddTarget("اینترنت", "8.8.8.8");
    }

    // ---------- مدیریت لیست مقصدها ----------

    private void OnAddClicked(object? sender, EventArgs e)
    {
        var host = _hostTextBox.Text.Trim();
        if (host.Length == 0)
        {
            MessageBox.Show(this, "لطفاً یک آدرس IP یا نام هاست وارد کنید.", "خطا",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_targets.Count >= MaxTargets)
        {
            MessageBox.Show(this, $"حداکثر {MaxTargets} مقصد می‌توانید اضافه کنید.", "محدودیت",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var alias = _aliasTextBox.Text.Trim();
        if (alias.Length == 0)
            alias = host;

        AddTarget(alias, host);
        SaveTargets();
        _aliasTextBox.Clear();
        _hostTextBox.Clear();
        _aliasTextBox.Focus();
    }

    private void AddTarget(string alias, string host)
    {
        _targets.Add(new TargetState { Alias = alias, Host = host });
        _grid.Rows.Add(alias, host, "—", "", "");
        _grid.Rows[^1].Cells[2].Style.BackColor = Color.Gainsboro;
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow is null)
            return;

        var index = _grid.CurrentRow.Index;
        Log($"مقصد «{_targets[index].Alias}» حذف شد");
        _targets.RemoveAt(index);
        _grid.Rows.RemoveAt(index);
        SaveTargets();
    }

    private void LoadTargets()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return;
            var configs = JsonSerializer.Deserialize<List<TargetConfig>>(File.ReadAllText(ConfigPath));
            if (configs is null)
                return;
            foreach (var c in configs.Take(MaxTargets))
                if (!string.IsNullOrWhiteSpace(c.Host))
                    AddTarget(string.IsNullOrWhiteSpace(c.Alias) ? c.Host : c.Alias, c.Host);
        }
        catch
        {
            // فایل تنظیمات خراب — با لیست خالی شروع می‌کنیم
        }
    }

    private void SaveTargets()
    {
        try
        {
            var configs = _targets.Select(t => new TargetConfig(t.Alias, t.Host)).ToList();
            File.WriteAllText(ConfigPath,
                JsonSerializer.Serialize(configs, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // مسیر غیرقابل نوشتن — ذخیره‌سازی بی‌صدا رد می‌شود
        }
    }

    // ---------- شروع/توقف پایش ----------

    private void OnStartStopClicked(object? sender, EventArgs e)
    {
        if (_running)
        {
            StopMonitoring();
            return;
        }

        if (_targets.Count == 0)
        {
            MessageBox.Show(this, "ابتدا حداقل یک مقصد اضافه کنید.", "خطا",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _running = true;
        foreach (var t in _targets)
            t.LastReachable = null;
        _startStopButton.Text = "توقف";
        SetAddRemoveEnabled(false);
        SetSummary("در حال بررسی...", Color.Khaki);
        Log($"شروع پایش {_targets.Count} مقصد");

        _pingTimer.Interval = (int)_intervalUpDown.Value * 1000;
        _pingTimer.Start();
        CheckAllTargets(); // بررسی فوری بدون انتظار برای اولین تیک تایمر
    }

    private void StopMonitoring()
    {
        _running = false;
        _pingTimer.Stop();
        _startStopButton.Text = "شروع";
        SetAddRemoveEnabled(true);
        SetSummary("متوقف شد", Color.Gainsboro);
        SetTrayText("PingApp — هشدار اتصال");
        Log("پایش متوقف شد");
    }

    private void SetAddRemoveEnabled(bool enabled)
    {
        _addButton.Enabled = enabled;
        _removeButton.Enabled = enabled;
        _aliasTextBox.Enabled = enabled;
        _hostTextBox.Enabled = enabled;
        _intervalUpDown.Enabled = enabled;
    }

    // ---------- پینگ و به‌روزرسانی وضعیت ----------

    private void CheckAllTargets()
    {
        if (!_running)
            return;
        for (var i = 0; i < _targets.Count; i++)
            _ = CheckTargetAsync(_targets[i], i);
    }

    private async Task CheckTargetAsync(TargetState target, int rowIndex)
    {
        if (target.PingInProgress)
            return;

        target.PingInProgress = true;
        bool reachable;
        long roundtrip = 0;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target.Host, 3000);
            reachable = reply.Status == IPStatus.Success;
            roundtrip = reply.RoundtripTime;
        }
        catch
        {
            reachable = false;
        }
        finally
        {
            target.PingInProgress = false;
        }

        // ممکن است در این فاصله پایش متوقف یا ردیف حذف شده باشد
        if (!_running || !_targets.Contains(target))
            return;
        rowIndex = _targets.IndexOf(target);

        var row = _grid.Rows[rowIndex];
        if (reachable)
        {
            row.Cells[2].Value = "متصل ✓";
            row.Cells[2].Style.BackColor = Color.LightGreen;
            row.Cells[3].Value = roundtrip.ToString();
            if (target.LastReachable != true)
            {
                row.Cells[4].Value = DateTime.Now.ToString("HH:mm:ss");
                Log($"«{target.Alias}» ({target.Host}) متصل شد ({roundtrip} ms)");
                _trayIcon.ShowBalloonTip(4000, "اتصال برقرار شد ✓",
                    $"{target.Alias} ({target.Host}) — {roundtrip} ms", ToolTipIcon.Info);
                PlayConnectedAlert();
            }
        }
        else
        {
            row.Cells[2].Value = "قطع ✗";
            row.Cells[2].Style.BackColor = Color.LightCoral;
            row.Cells[3].Value = "";
            if (target.LastReachable == true)
            {
                row.Cells[4].Value = DateTime.Now.ToString("HH:mm:ss");
                Log($"«{target.Alias}» ({target.Host}) قطع شد");
                _trayIcon.ShowBalloonTip(4000, "اتصال قطع شد ✗",
                    $"{target.Alias} ({target.Host})", ToolTipIcon.Warning);
                if (_alertOnDisconnectCheckBox.Checked)
                    PlayDisconnectedAlert();
            }
        }

        target.LastReachable = reachable;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var up = _targets.Count(t => t.LastReachable == true);
        var total = _targets.Count;
        if (up == total)
            SetSummary($"همه متصل ✓ ({up}/{total})", Color.LightGreen);
        else if (up == 0 && _targets.All(t => t.LastReachable is not null))
            SetSummary($"همه قطع ✗ (0/{total})", Color.LightCoral);
        else
            SetSummary($"متصل: {up} از {total}", Color.Khaki);
        SetTrayText($"PingApp — متصل {up}/{total}");
    }

    // ---------- Tray ----------

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
            _trayIcon.ShowBalloonTip(2000, "PingApp",
                "برنامه در کنار ساعت در حال اجراست. برای بازگشت دوبار کلیک کنید.",
                ToolTipIcon.Info);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveTargets();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnFormClosed(e);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    // ---------- صدا و نمایش ----------

    private static void PlayConnectedAlert()
    {
        // سه بوق بالارونده تا از صداهای عادی ویندوز قابل تشخیص باشد
        Task.Run(() =>
        {
            try
            {
                Console.Beep(800, 200);
                Console.Beep(1000, 200);
                Console.Beep(1300, 350);
            }
            catch
            {
                SystemSounds.Exclamation.Play();
            }
        });
    }

    private static void PlayDisconnectedAlert()
    {
        Task.Run(() =>
        {
            try
            {
                Console.Beep(600, 250);
                Console.Beep(400, 400);
            }
            catch
            {
                SystemSounds.Hand.Play();
            }
        });
    }

    private void SetSummary(string text, Color color)
    {
        _summaryLabel.Text = text;
        _summaryLabel.BackColor = color;
    }

    private void SetTrayText(string text)
    {
        // متن آیکون کنار ساعت حداکثر ۶۳ کاراکتر می‌پذیرد
        _trayIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void Log(string message)
    {
        _logListBox.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_logListBox.Items.Count > 200)
            _logListBox.Items.RemoveAt(_logListBox.Items.Count - 1);
    }
}
