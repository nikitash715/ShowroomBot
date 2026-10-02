using ShowroomBot.Configuration;
using ShowroomBot.Core;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;

namespace ShowroomBot.UI;

public sealed class MainForm : Form
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly IIdleDetector _idleDetector;
    private readonly VpnDetector _vpnDetector;
    private readonly RdpAvailabilityChecker _rdpAvailabilityChecker;
    private readonly RdpController _rdpController;
    private readonly DemoScenario _demoScenario;
    private readonly DemoController _demoController;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startMenuItem;
    private readonly ToolStripMenuItem _stopMenuItem;
    private readonly ToolStripMenuItem _openRdpMenuItem;

    private readonly Label _stateValueLabel;
    private readonly Label _idleValueLabel;
    private readonly Label _thresholdValueLabel;
    private readonly Label _vpnValueLabel;
    private readonly Label _rdpValueLabel;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Button _openRdpButton;
    private readonly CheckBox _autoStartCheckBox;
    private readonly NumericUpDown _idleMinutesInput;
    private readonly ComboBox _scenarioComboBox;

    private VpnStatus _vpnStatus = VpnStatus.Disconnected;
    private bool _isRdpAvailable;
    private bool _isCheckingInfrastructure;
    private DateTime _lastInfrastructureCheckUtc = DateTime.MinValue;
    private bool _isExiting;
    private bool _isTestScenarioRunning;
    private CancellationTokenSource? _scenarioCancellation;
    private EmergencyStopHotkey? _emergencyStop;

    public MainForm(
        SettingsService settingsService,
        AppSettings settings,
        IReadOnlyList<ScenarioDescriptor> scenarios,
        IIdleDetector idleDetector,
        VpnDetector vpnDetector,
        RdpAvailabilityChecker rdpAvailabilityChecker,
        RdpController rdpController,
        DemoScenario demoScenario,
        DemoController demoController)
    {
        _settingsService = settingsService;
        _settings = settings;
        _idleDetector = idleDetector;
        _vpnDetector = vpnDetector;
        _rdpAvailabilityChecker = rdpAvailabilityChecker;
        _rdpController = rdpController;
        _demoScenario = demoScenario;
        _demoController = demoController;

        Text = "ShowroomBot";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(480, 340);
        Size = new Size(620, 380);
        Icon = LoadApplicationIcon();

        _stateValueLabel = CreateValueLabel();
        _idleValueLabel = CreateValueLabel();
        _thresholdValueLabel = CreateValueLabel();
        _vpnValueLabel = CreateValueLabel();
        _rdpValueLabel = CreateValueLabel();
        _startButton = new Button { Text = "Запустить демонстрацию", AutoSize = true };
        _stopButton = new Button { Text = "Остановить (Ctrl+Alt+F12)", AutoSize = true };
        _openRdpButton = new Button { Text = "Открыть RDP", AutoSize = true };
        _autoStartCheckBox = new CheckBox
        {
            Text = "Автоматически запускать при бездействии пользователя",
            AutoSize = true,
            Checked = false,
            Enabled = false
        };
        _idleMinutesInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 1440,
            Value = Math.Clamp(_settings.IdleMinutes, 1, 1440),
            Width = 80
        };
        _scenarioComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 320,
            DisplayMember = nameof(ScenarioDescriptor.Name),
            DataSource = scenarios.ToList()
        };

        Controls.Add(BuildLayout());

        _startMenuItem = new ToolStripMenuItem("Запустить демонстрацию", null, async (_, _) => await StartDemoAsync());
        _stopMenuItem = new ToolStripMenuItem("Остановить демонстрацию", null, (_, _) => StopDemo());
        _openRdpMenuItem = new ToolStripMenuItem("Открыть RDP", null, (_, _) => OpenRdp());
        var openMenuItem = new ToolStripMenuItem("Открыть", null, (_, _) => ShowMainWindow());
        var exitMenuItem = new ToolStripMenuItem("Выход", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = Icon,
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
            Text = "ShowroomBot"
        };
        _notifyIcon.ContextMenuStrip.Items.Add(openMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(_startMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(_stopMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(_openRdpMenuItem);
        _notifyIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add(exitMenuItem);
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += async (_, _) => await RefreshAsync();

        _startButton.Click += async (_, _) => await StartDemoAsync();
        _stopButton.Click += (_, _) => StopDemo();
        _openRdpButton.Click += (_, _) => OpenRdp();
        _autoStartCheckBox.CheckedChanged += (_, _) =>
        {
            _settings.AutoStartDemo = _autoStartCheckBox.Checked;
            SaveSettings();
            UpdateView();
        };
        _idleMinutesInput.ValueChanged += (_, _) =>
        {
            _settings.IdleMinutes = (int)_idleMinutesInput.Value;
            SaveSettings();
            UpdateView();
        };
        _scenarioComboBox.SelectedIndexChanged += (_, _) => UpdateView();
        _demoController.StateChanged += (_, _) => UpdateView();

        UpdateView();
        _ = RefreshAsync(forceInfrastructureCheck: true);
        _timer.Start();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_isExiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        StopDemo();
        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _emergencyStop = new EmergencyStopHotkey(() =>
        {
            if (!IsDisposed && IsHandleCreated)
            {
                try { BeginInvoke(new Action(StopDemo)); }
                catch (InvalidOperationException) { /* The form is closing. */ }
            }
        });
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _emergencyStop?.Dispose();
        _emergencyStop = null;
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ScenarioExecution.CancelCurrent();
            _scenarioCancellation?.Cancel();
            _emergencyStop?.Dispose();
            _timer.Dispose();
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 2,
            RowCount = 9
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(panel, 0, "Статус:", _stateValueLabel);
        AddRow(panel, 1, "Бездействие:", _idleValueLabel);
        AddRow(panel, 2, "Порог запуска:", _thresholdValueLabel);
        AddRow(panel, 3, "VPN:", _vpnValueLabel);
        AddRow(panel, 4, "RDP:", _rdpValueLabel);
        AddRow(panel, 5, "Сценарий:", _scenarioComboBox);

        var settingsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        settingsPanel.Controls.Add(_autoStartCheckBox);
        settingsPanel.Controls.Add(new Label
        {
            Text = "Минут:",
            AutoSize = true,
            Margin = new Padding(12, 6, 4, 0)
        });
        settingsPanel.Controls.Add(_idleMinutesInput);

        panel.Controls.Add(settingsPanel, 0, 6);
        panel.SetColumnSpan(settingsPanel, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 16, 0, 0)
        };
        buttonPanel.Controls.Add(_startButton);
        buttonPanel.Controls.Add(_stopButton);
        buttonPanel.Controls.Add(_openRdpButton);

        panel.Controls.Add(buttonPanel, 0, 7);
        panel.SetColumnSpan(buttonPanel, 2);

        return panel;
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 4, 0, 8)
        };
    }

    private static void AddRow(TableLayoutPanel panel, int row, string title, Control value)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Margin = new Padding(0, 4, 12, 8)
        }, 0, row);
        panel.Controls.Add(value, 1, row);
    }

    private async Task RefreshAsync(bool forceInfrastructureCheck = false)
    {
        var idleTime = _idleDetector.GetIdleTime();
        _idleValueLabel.Text = FormatIdleTime(idleTime);

        if (forceInfrastructureCheck || DateTime.UtcNow - _lastInfrastructureCheckUtc >= GetInfrastructureCheckInterval())
        {
            await RefreshInfrastructureStateAsync();
        }

        UpdateView();
    }

    private async Task RefreshInfrastructureStateAsync()
    {
        if (_isCheckingInfrastructure)
        {
            return;
        }

        _isCheckingInfrastructure = true;
        try
        {
            _lastInfrastructureCheckUtc = DateTime.UtcNow;
            _vpnStatus = _vpnDetector.GetStatus(_settings.Vpn.ConnectionNames);
            _isRdpAvailable = _vpnStatus.IsConnected &&
                await _rdpAvailabilityChecker.IsAvailableAsync(
                    _settings.Rdp.Host,
                    _settings.Rdp.Port,
                    TimeSpan.FromSeconds(_settings.Rdp.ConnectTimeoutSeconds));
        }
        finally
        {
            _isCheckingInfrastructure = false;
        }
    }

    private TimeSpan GetInfrastructureCheckInterval()
    {
        var seconds = Math.Min(_settings.Vpn.CheckIntervalSeconds, _settings.Rdp.CheckIntervalSeconds);
        return TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    private async Task StartDemoAsync()
    {
        if (_scenarioComboBox.SelectedItem is not ScenarioDescriptor scenario)
        {
            MessageBox.Show(
                this,
                "В папке Scenarios не найдено доступных YAML-сценариев.",
                "ShowroomBot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (_isTestScenarioRunning)
        {
            return;
        }

        _demoController.StartDemo(automatic: false);
        _isTestScenarioRunning = true;
        using var cancellation = new CancellationTokenSource();
        _scenarioCancellation = cancellation;
        UpdateView();
        try
        {
            await _demoScenario.RunAsync(scenario.Definition, cancellation.Token);
            _notifyIcon.ShowBalloonTip(
                4000,
                "ShowroomBot",
                $"Сценарий завершён: {scenario.Name}",
                ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            _demoController.StopDemo();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "Ошибка сценария",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            _demoController.StopDemo();
        }
        finally
        {
            _scenarioCancellation = null;
            _isTestScenarioRunning = false;
            _demoController.StopDemo();
            UpdateView();
        }
    }

    private void StopDemo()
    {
        ScenarioExecution.CancelCurrent();
        _scenarioCancellation?.Cancel();
        _demoController.StopDemo();
    }

    private void OpenRdp()
    {
        if (!IsReadyForRdp())
        {
            return;
        }

        _rdpController.OpenOrActivate(_settings.Rdp.Host);
    }

    private bool IsReadyForRdp()
    {
        return _vpnStatus.IsConnected && _isRdpAvailable;
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        _notifyIcon.Visible = false;
        Close();
    }

    private void UpdateView()
    {
        _stateValueLabel.Text = GetDisplayState(_demoController.State);
        _thresholdValueLabel.Text = $"{_settings.IdleMinutes} мин.";
        _vpnValueLabel.Text = _vpnStatus.IsConnected
            ? $"{_vpnStatus.ActiveConnectionName} - подключен"
            : "не подключен";
        _vpnValueLabel.ForeColor = _vpnStatus.IsConnected ? Color.DarkGreen : Color.Firebrick;

        _rdpValueLabel.Text = GetRdpStatusText();
        _rdpValueLabel.ForeColor = _isRdpAvailable ? Color.DarkGreen : Color.Firebrick;

        var isRunning = _demoController.State == AppState.DemoRunning;
        var canOpenRdp = IsReadyForRdp();
        var hasSelectedScenario = _scenarioComboBox.SelectedItem is ScenarioDescriptor;
        _startButton.Enabled = !isRunning && !_isTestScenarioRunning && hasSelectedScenario;
        _stopButton.Enabled = isRunning || _isTestScenarioRunning;
        _openRdpButton.Enabled = canOpenRdp;
        _startMenuItem.Enabled = !isRunning && !_isTestScenarioRunning && hasSelectedScenario;
        _stopMenuItem.Enabled = isRunning || _isTestScenarioRunning;
        _openRdpMenuItem.Enabled = canOpenRdp;

        UpdateTrayTooltip();
    }

    private string GetRdpStatusText()
    {
        if (!_vpnStatus.IsConnected)
        {
            return "недоступен (VPN отключен)";
        }

        return _isRdpAvailable
            ? $"доступен ({_settings.Rdp.Host}:{_settings.Rdp.Port})"
            : "недоступен";
    }

    private void UpdateTrayTooltip()
    {
        _notifyIcon.Text = $"ShowroomBot: {GetDisplayState(_demoController.State)}";
    }

    private void SaveSettings()
    {
        _settingsService.Save(_settings);
    }

    private static Icon LoadApplicationIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ShowroomBot.ico");
        return File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application;
    }

    private static string GetDisplayState(AppState state)
    {
        return state switch
        {
            AppState.Waiting => "Ожидание",
            AppState.DemoRunning => "Демонстрация",
            _ => state.ToString()
        };
    }

    private static string FormatIdleTime(TimeSpan idleTime)
    {
        return idleTime.ToString(@"hh\:mm\:ss");
    }
}
