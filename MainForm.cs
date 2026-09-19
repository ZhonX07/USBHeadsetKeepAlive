using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using System.Drawing;

namespace UsbHeadsetKeepAlive;

internal sealed class MainForm : Form
{
    private const string StartupValueName = "USB Headset KeepAlive";
    private readonly ComboBox deviceBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button refreshButton = new() { Text = "刷新设备", AutoSize = true };
    private readonly Button toggleButton = new() { Text = "启动 KeepAlive", AutoSize = true };
    private readonly CheckBox startupCheck = new() { Text = "登录 Windows 时自动启动", AutoSize = true };
    private readonly CheckBox startMinimizedCheck = new() { Text = "自动启动时最小化到托盘", AutoSize = true, Checked = true };
    private readonly Label statusLabel = new() { AutoSize = true, Text = "已停止", ForeColor = Color.Firebrick };
    private readonly Label levelLabel = new() { AutoSize = true };
    private readonly TrackBar levelSlider = new()
    {
        Minimum = -100,
        Maximum = -50,
        Value = -80,
        TickFrequency = 10,
        SmallChange = 1,
        LargeChange = 5,
        AutoSize = false,
        Height = 40
    };
    private readonly NotifyIcon trayIcon;
    private readonly ToolStripMenuItem trayToggleItem = new("启动 KeepAlive");

    private MMDeviceEnumerator? enumerator;
    private WasapiOut? output;
    private KeepAliveWaveProvider? signal;
    private bool allowExit;
    private bool suppressStartupEvent;

    public MainForm()
    {
        Text = "USB 耳机 KeepAlive";
        ClientSize = new Size(560, 430);
        MinimumSize = new Size(590, 490);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(trayToggleItem);
        trayMenu.Items.Add("显示窗口", null, (_, _) => RestoreWindow());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => ExitApplication());
        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "USB 耳机 KeepAlive - 已停止",
            Visible = true,
            ContextMenuStrip = trayMenu
        };

        BuildUi();
        WireEvents();
        LoadDevices();
        LoadStartupSetting();

        string[] arguments = Environment.GetCommandLineArgs();
        bool autoStart = arguments.Any(a => a.Equals("--autostart", StringComparison.OrdinalIgnoreCase));
        bool startMinimized = arguments.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (autoStart)
        {
            Shown += (_, _) =>
            {
                StartKeepAlive();
                if (startMinimized)
                    HideToTray(showNotification: false);
            };
        }
    }

    private void BuildUi()
    {
        var title = new Label
        {
            Text = "USB 耳机 / DAC 保活",
            Font = new Font(Font.FontFamily, 16F, FontStyle.Bold),
            AutoSize = true
        };
        var description = new Label
        {
            Text = "持续输出几乎不可听的非零信号，防止设备因数字静音进入待机。",
            AutoSize = true,
            ForeColor = Color.DimGray
        };

        deviceBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        levelSlider.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        var deviceRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        deviceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deviceRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        deviceRow.Controls.Add(deviceBox, 0, 0);
        deviceRow.Controls.Add(refreshButton, 1, 0);

        var levelRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        levelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        levelRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        levelRow.Controls.Add(levelSlider, 0, 0);
        levelRow.Controls.Add(levelLabel, 1, 0);

        toggleButton.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
        toggleButton.Padding = new Padding(8, 4, 8, 4);
        var actionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 12, 0, 4)
        };
        actionRow.Controls.Add(toggleButton);
        actionRow.Controls.Add(new Label { Text = "状态：", AutoSize = true, Margin = new Padding(16, 8, 0, 0) });
        actionRow.Controls.Add(statusLabel);
        statusLabel.Margin = new Padding(3, 8, 0, 0);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(20),
            ColumnCount = 1,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(title);
        layout.Controls.Add(description);
        layout.Controls.Add(new Label { Text = "输出设备", AutoSize = true, Margin = new Padding(0, 15, 0, 3) });
        layout.Controls.Add(deviceRow);
        layout.Controls.Add(new Label { Text = "信号强度（先用默认值；若无效再逐步调高）", AutoSize = true, Margin = new Padding(0, 12, 0, 0) });
        layout.Controls.Add(levelRow);
        layout.Controls.Add(actionRow);
        layout.Controls.Add(startupCheck);
        layout.Controls.Add(startMinimizedCheck);
        layout.Controls.Add(new Label
        {
            Text = "关闭窗口时程序会继续在系统托盘运行。",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 10, 0, 0)
        });
        Controls.Add(layout);
        AutoScroll = true;
        UpdateLevelLabel();
    }

    private void WireEvents()
    {
        refreshButton.Click += (_, _) => LoadDevices();
        toggleButton.Click += (_, _) => ToggleKeepAlive();
        trayToggleItem.Click += (_, _) => ToggleKeepAlive();
        levelSlider.ValueChanged += (_, _) =>
        {
            UpdateLevelLabel();
            signal?.SetLevelDb(levelSlider.Value);
        };
        startupCheck.CheckedChanged += (_, _) =>
        {
            if (!suppressStartupEvent)
                SetStartup(startupCheck.Checked);
        };
        startMinimizedCheck.CheckedChanged += (_, _) =>
        {
            if (startupCheck.Checked && !suppressStartupEvent)
                SetStartup(enabled: true);
        };
        FormClosing += OnFormClosing;
        trayIcon.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                HideToTray(showNotification: true);
        };
    }

    private void LoadDevices()
    {
        string? previousId = (deviceBox.SelectedItem as AudioDeviceItem)?.Id;
        deviceBox.Items.Clear();
        enumerator?.Dispose();
        enumerator = new MMDeviceEnumerator();

        try
        {
            string? defaultDeviceId = null;
            try { defaultDeviceId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID; }
            catch { /* A system can temporarily have no default playback endpoint. */ }

            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in devices)
                deviceBox.Items.Add(new AudioDeviceItem(device.ID, device.FriendlyName));

            int previousIndex = deviceBox.Items.Cast<AudioDeviceItem>().ToList().FindIndex(x => x.Id == previousId);
            int defaultIndex = deviceBox.Items.Cast<AudioDeviceItem>().ToList().FindIndex(x => x.Id == defaultDeviceId);
            deviceBox.SelectedIndex = previousIndex >= 0
                ? previousIndex
                : (defaultIndex >= 0 ? defaultIndex : (deviceBox.Items.Count > 0 ? 0 : -1));
        }
        catch (Exception ex)
        {
            ShowError("无法读取音频输出设备。", ex);
        }

        UpdateControls();
    }

    private void ToggleKeepAlive()
    {
        if (output is null)
            StartKeepAlive();
        else
            StopKeepAlive();
    }

    private void StartKeepAlive()
    {
        if (output is not null)
            return;
        if (deviceBox.SelectedItem is not AudioDeviceItem selected || enumerator is null)
        {
            MessageBox.Show(this, "请先选择一个可用的音频输出设备。", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            MMDevice device = enumerator.GetDevice(selected.Id);
            signal = new KeepAliveWaveProvider();
            signal.SetLevelDb(levelSlider.Value);
            output = new WasapiOut(device, AudioClientShareMode.Shared, true, 200);
            output.PlaybackStopped += OutputOnPlaybackStopped;
            output.Init(signal);
            output.Play();
            UpdateControls();
        }
        catch (Exception ex)
        {
            StopKeepAlive();
            ShowError("无法启动 KeepAlive。设备可能已断开或被独占。", ex);
        }
    }

    private void StopKeepAlive()
    {
        if (output is not null)
        {
            output.PlaybackStopped -= OutputOnPlaybackStopped;
            try { output.Stop(); } catch { }
            output.Dispose();
            output = null;
        }
        signal = null;
        UpdateControls();
    }

    private void OutputOnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OutputOnPlaybackStopped(sender, e));
            return;
        }

        if (output is not null)
        {
            output.PlaybackStopped -= OutputOnPlaybackStopped;
            output.Dispose();
            output = null;
            signal = null;
            UpdateControls();
        }

        if (e.Exception is not null)
            ShowError("音频输出已停止。请检查设备连接后重新启动。", e.Exception);
    }

    private void UpdateControls()
    {
        bool running = output is not null;
        toggleButton.Text = running ? "停止 KeepAlive" : "启动 KeepAlive";
        trayToggleItem.Text = toggleButton.Text;
        deviceBox.Enabled = !running;
        refreshButton.Enabled = !running;
        statusLabel.Text = running ? "运行中" : "已停止";
        statusLabel.ForeColor = running ? Color.ForestGreen : Color.Firebrick;
        string deviceName = (deviceBox.SelectedItem as AudioDeviceItem)?.Name ?? "无设备";
        trayIcon.Text = TruncateTrayText($"KeepAlive {(running ? "运行中" : "已停止")} - {deviceName}");
    }

    private void UpdateLevelLabel() => levelLabel.Text = $"{levelSlider.Value} dBFS";

    private void LoadStartupSetting()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        bool enabled = key?.GetValue(StartupValueName) is string;
        suppressStartupEvent = true;
        startupCheck.Checked = enabled;
        suppressStartupEvent = false;
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
            {
                string exePath = Environment.ProcessPath
                    ?? throw new InvalidOperationException("无法确定程序路径。");
                string minimizedArgument = startMinimizedCheck.Checked ? " --minimized" : string.Empty;
                key.SetValue(StartupValueName, $"\"{exePath}\" --autostart{minimizedArgument}");
            }
            else
            {
                key.DeleteValue(StartupValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            suppressStartupEvent = true;
            startupCheck.Checked = !enabled;
            suppressStartupEvent = false;
            ShowError("无法修改开机自启设置。", ex);
        }
    }

    private void HideToTray(bool showNotification)
    {
        Hide();
        ShowInTaskbar = false;
        if (showNotification)
            trayIcon.ShowBalloonTip(1500, Text, "程序仍在系统托盘运行。", ToolTipIcon.Info);
    }

    private void RestoreWindow()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray(showNotification: true);
        }
    }

    private void ExitApplication()
    {
        allowExit = true;
        StopKeepAlive();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        enumerator?.Dispose();
        Close();
    }

    private void ShowError(string message, Exception ex) =>
        MessageBox.Show(this, $"{message}\n\n{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static string TruncateTrayText(string text) => text.Length <= 63 ? text : text[..63];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            output?.Dispose();
            enumerator?.Dispose();
            trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record AudioDeviceItem(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}
