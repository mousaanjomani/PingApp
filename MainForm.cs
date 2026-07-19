using System.Media;
using System.Net.NetworkInformation;

namespace PingApp;

public class MainForm : Form
{
    private readonly TextBox _ipTextBox;
    private readonly NumericUpDown _intervalUpDown;
    private readonly Button _startStopButton;
    private readonly Label _statusLabel;
    private readonly CheckBox _alertOnDisconnectCheckBox;
    private readonly ListBox _logListBox;
    private readonly System.Windows.Forms.Timer _pingTimer;

    private bool _running;
    private bool? _lastReachable; // null = هنوز نتیجه‌ای نداریم
    private bool _pingInProgress;

    public MainForm()
    {
        Text = "PingApp — هشدار اتصال";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(420, 360);
        StartPosition = FormStartPosition.CenterScreen;

        var ipLabel = new Label
        {
            Text = "آدرس IP یا هاست:",
            Location = new Point(300, 18),
            AutoSize = true,
        };

        _ipTextBox = new TextBox
        {
            Text = "8.8.8.8",
            Location = new Point(20, 15),
            Width = 260,
            RightToLeft = RightToLeft.No,
            TextAlign = HorizontalAlignment.Left,
        };

        var intervalLabel = new Label
        {
            Text = "فاصله بررسی (ثانیه):",
            Location = new Point(285, 53),
            AutoSize = true,
        };

        _intervalUpDown = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 3600,
            Value = 2,
            Location = new Point(200, 50),
            Width = 80,
        };

        _alertOnDisconnectCheckBox = new CheckBox
        {
            Text = "هنگام قطع ارتباط هم هشدار بده",
            Location = new Point(180, 85),
            AutoSize = true,
        };

        _startStopButton = new Button
        {
            Text = "شروع",
            Location = new Point(20, 48),
            Size = new Size(100, 60),
        };
        _startStopButton.Click += OnStartStopClicked;

        _statusLabel = new Label
        {
            Text = "آماده",
            Location = new Point(20, 120),
            Size = new Size(380, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.Gainsboro,
        };

        _logListBox = new ListBox
        {
            Location = new Point(20, 160),
            Size = new Size(380, 180),
            IntegralHeight = false,
        };

        Controls.AddRange(new Control[]
        {
            ipLabel, _ipTextBox, intervalLabel, _intervalUpDown,
            _alertOnDisconnectCheckBox, _startStopButton, _statusLabel, _logListBox,
        });

        _pingTimer = new System.Windows.Forms.Timer();
        _pingTimer.Tick += async (_, _) => await CheckConnectionAsync();
    }

    private void OnStartStopClicked(object? sender, EventArgs e)
    {
        if (_running)
        {
            StopMonitoring();
            return;
        }

        var target = _ipTextBox.Text.Trim();
        if (target.Length == 0)
        {
            MessageBox.Show(this, "لطفاً یک آدرس IP یا نام هاست وارد کنید.", "خطا",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _running = true;
        _lastReachable = null;
        _startStopButton.Text = "توقف";
        _ipTextBox.Enabled = false;
        _intervalUpDown.Enabled = false;
        SetStatus("در حال بررسی...", Color.Khaki);
        Log($"شروع پایش {target}");

        _pingTimer.Interval = (int)_intervalUpDown.Value * 1000;
        _pingTimer.Start();
        _ = CheckConnectionAsync(); // بررسی فوری بدون انتظار برای اولین تیک تایمر
    }

    private void StopMonitoring()
    {
        _running = false;
        _pingTimer.Stop();
        _startStopButton.Text = "شروع";
        _ipTextBox.Enabled = true;
        _intervalUpDown.Enabled = true;
        SetStatus("متوقف شد", Color.Gainsboro);
        Log("پایش متوقف شد");
    }

    private async Task CheckConnectionAsync()
    {
        if (!_running || _pingInProgress)
            return;

        _pingInProgress = true;
        var target = _ipTextBox.Text.Trim();
        bool reachable;
        long roundtrip = 0;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, 3000);
            reachable = reply.Status == IPStatus.Success;
            roundtrip = reply.RoundtripTime;
        }
        catch
        {
            reachable = false;
        }
        finally
        {
            _pingInProgress = false;
        }

        if (!_running)
            return;

        if (reachable)
        {
            SetStatus($"متصل ✓ ({roundtrip} ms)", Color.LightGreen);
            if (_lastReachable != true)
            {
                Log($"ارتباط با {target} برقرار شد ({roundtrip} ms)");
                PlayConnectedAlert();
            }
        }
        else
        {
            SetStatus("قطع ✗", Color.LightCoral);
            if (_lastReachable == true)
            {
                Log($"ارتباط با {target} قطع شد");
                if (_alertOnDisconnectCheckBox.Checked)
                    PlayDisconnectedAlert();
            }
        }

        _lastReachable = reachable;
    }

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

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.BackColor = color;
    }

    private void Log(string message)
    {
        _logListBox.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (_logListBox.Items.Count > 200)
            _logListBox.Items.RemoveAt(_logListBox.Items.Count - 1);
    }
}
