using System.Drawing;

namespace RockyBackup;

public sealed class MainForm : Form
{
    private readonly ConfigService _configService = new();
    private readonly BackupService _backupService = new();
    private readonly FileLogger _fileLogger;
    private AppConfig _config;

    private readonly TextBox txtHost = new();
    private readonly NumericUpDown numPort = new();
    private readonly TextBox txtUser = new();
    private readonly TextBox txtPassword = new();
    private readonly TextBox txtRemote = new();
    private readonly TextBox txtLocal = new();
    private readonly DateTimePicker timePicker = new();
    private readonly CheckBox chkMirror = new();
    private readonly CheckBox chkAutoStart = new();
    private readonly Button btnTest = new();
    private readonly Button btnBrowse = new();
    private readonly Button btnSave = new();
    private readonly Button btnBackup = new();
    private readonly Label lblStatus = new();
    private readonly RichTextBox txtLog = new();
    private readonly System.Windows.Forms.Timer schedulerTimer = new();
    private readonly NotifyIcon trayIcon = new();

    private bool _backupRunning;
    private bool _allowExit;
    private readonly bool _startMinimized;

    public MainForm(bool startMinimized = false)
    {
        _startMinimized = startMinimized;
        _config = _configService.Load();
        _fileLogger = new FileLogger(_configService.LogDirectory);

        Text = "Rocky Linux SSH 目录备份";
        Width = 920;
        Height = 700;
        MinimumSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildUi();
        LoadConfigToUi();
        ConfigureTray();

        schedulerTimer.Interval = 15000;
        schedulerTimer.Tick += SchedulerTimer_Tick;
        schedulerTimer.Start();

        Shown += (_, _) =>
        {
            UpdateNextBackupLabel();
            if (_startMinimized) HideToTray(showTip: false);
        };

        FormClosing += MainForm_FormClosing;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
                HideToTray(showTip: false);
        };
    }

    private void BuildUi()
    {
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(main);

        var title = new Label
        {
            Text = "Rocky Linux 10 远程目录备份",
            Font = new Font(Font.FontFamily, 14F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };
        main.Controls.Add(title, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        main.Controls.Add(grid, 0, 1);

        txtHost.Dock = DockStyle.Fill;
        numPort.Minimum = 1; numPort.Maximum = 65535; numPort.Value = 22; numPort.Dock = DockStyle.Fill;
        txtUser.Dock = DockStyle.Fill;
        txtPassword.Dock = DockStyle.Fill; txtPassword.UseSystemPasswordChar = true;
        txtRemote.Dock = DockStyle.Fill;
        txtLocal.Dock = DockStyle.Fill;
        timePicker.Format = DateTimePickerFormat.Custom; timePicker.CustomFormat = "HH:mm"; timePicker.ShowUpDown = true; timePicker.Dock = DockStyle.Fill;
        chkMirror.Text = "镜像模式：删除本地多余文件，使目标目录与源目录一致";
        chkMirror.AutoSize = true;
        chkAutoStart.Text = "Windows 登录后自动启动到托盘（定时备份建议开启）";
        chkAutoStart.AutoSize = true;

        AddRow(grid, 0, "SSH IP/主机：", txtHost, "SSH端口：", numPort);
        AddRow(grid, 1, "SSH账户：", txtUser, "SSH密码：", txtPassword);
        AddWideRow(grid, 2, "源主机目录：", txtRemote);

        var localPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        localPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        localPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        localPanel.Controls.Add(txtLocal, 0, 0);
        btnBrowse.Text = "选择..."; btnBrowse.AutoSize = true; btnBrowse.Click += BtnBrowse_Click;
        localPanel.Controls.Add(btnBrowse, 1, 0);
        AddWideRow(grid, 3, "目标主机目录：", localPanel);

        AddRow(grid, 4, "每日备份时间：", timePicker, "", new Label { AutoSize = true });

        grid.Controls.Add(chkMirror, 1, 5);
        grid.SetColumnSpan(chkMirror, 3);
        grid.Controls.Add(chkAutoStart, 1, 6);
        grid.SetColumnSpan(chkAutoStart, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 12, 0, 8)
        };
        btnTest.Text = "测试连接"; btnTest.AutoSize = true; btnTest.Click += BtnTest_Click;
        btnSave.Text = "保存配置"; btnSave.AutoSize = true; btnSave.Click += BtnSave_Click;
        btnBackup.Text = "立即备份"; btnBackup.AutoSize = true; btnBackup.Click += BtnBackup_Click;
        actions.Controls.AddRange([btnTest, btnSave, btnBackup]);

        lblStatus.AutoSize = true;
        lblStatus.Padding = new Padding(12, 7, 0, 0);
        actions.Controls.Add(lblStatus);
        main.Controls.Add(actions, 0, 2);

        txtLog.Dock = DockStyle.Fill;
        txtLog.ReadOnly = true;
        txtLog.BackColor = Color.White;
        txtLog.Font = new Font("Consolas", 9F);
        main.Controls.Add(txtLog, 0, 3);
    }

    private static void AddRow(TableLayoutPanel grid, int row, string l1, Control c1, string l2, Control c2)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(MakeLabel(l1), 0, row);
        grid.Controls.Add(c1, 1, row);
        grid.Controls.Add(MakeLabel(l2), 2, row);
        grid.Controls.Add(c2, 3, row);
        c1.Margin = new Padding(3, 5, 12, 5);
        c2.Margin = new Padding(3, 5, 3, 5);
    }

    private static void AddWideRow(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(MakeLabel(label), 0, row);
        grid.Controls.Add(control, 1, row);
        grid.SetColumnSpan(control, 3);
        control.Margin = new Padding(3, 5, 3, 5);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 8, 3, 3)
    };

    private void LoadConfigToUi()
    {
        txtHost.Text = _config.Host;
        numPort.Value = Math.Clamp(_config.Port, 1, 65535);
        txtUser.Text = _config.Username;
        txtPassword.Text = ConfigService.UnprotectPassword(_config.PasswordProtected);
        txtRemote.Text = _config.RemoteDirectory;
        txtLocal.Text = _config.LocalDirectory;
        chkMirror.Checked = _config.MirrorMode;
        chkAutoStart.Checked = _config.AutoStart;

        if (TimeSpan.TryParse(_config.BackupTime, out var time))
            timePicker.Value = DateTime.Today.Add(time);
        else
            timePicker.Value = DateTime.Today.AddHours(2);
    }

    private AppConfig ReadUiToConfig()
    {
        return new AppConfig
        {
            Host = txtHost.Text.Trim(),
            Port = (int)numPort.Value,
            Username = txtUser.Text.Trim(),
            PasswordProtected = ConfigService.ProtectPassword(txtPassword.Text),
            RemoteDirectory = txtRemote.Text.Trim(),
            LocalDirectory = txtLocal.Text.Trim(),
            BackupTime = timePicker.Value.ToString("HH:mm"),
            MirrorMode = chkMirror.Checked,
            AutoStart = chkAutoStart.Checked,
            LastScheduledRunDate = _config.LastScheduledRunDate
        };
    }

    private void SaveConfig(bool showMessage)
    {
        _config = ReadUiToConfig();
        _configService.Save(_config);
        AutoStartService.SetEnabled(_config.AutoStart);
        UpdateNextBackupLabel();
        Log("配置已保存。密码使用 Windows 当前用户 DPAPI 加密保存。", toFile: false);
        if (showMessage) MessageBox.Show(this, "配置已保存。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void BtnTest_Click(object? sender, EventArgs e)
    {
        try
        {
            ToggleButtons(false);
            var cfg = ReadUiToConfig();
            var password = txtPassword.Text;
            Log($"测试连接 {cfg.Host}:{cfg.Port} ...");
            await Task.Run(() => _backupService.TestConnection(cfg, password));
            Log("测试成功：SSH/SFTP 可连接，源目录存在。");
            MessageBox.Show(this, "连接成功，源目录存在。", "测试连接", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("测试失败：" + ex.Message);
            MessageBox.Show(this, ex.Message, "测试失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { ToggleButtons(true); }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try { SaveConfig(showMessage: true); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async void BtnBackup_Click(object? sender, EventArgs e)
    {
        SaveConfig(showMessage: false);
        await RunBackupAsync(isScheduled: false);
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择本地备份目录",
            SelectedPath = Directory.Exists(txtLocal.Text) ? txtLocal.Text : @"D:\"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            txtLocal.Text = dialog.SelectedPath;
    }

    private async Task RunBackupAsync(bool isScheduled)
    {
        if (_backupRunning)
        {
            Log("已有备份任务正在运行，本次跳过。");
            return;
        }

        _backupRunning = true;
        ToggleButtons(false);
        try
        {
            var cfg = _config;
            var password = ConfigService.UnprotectPassword(cfg.PasswordProtected);
            if (string.IsNullOrEmpty(password))
                throw new InvalidOperationException("无法读取 SSH 密码，请重新输入并保存配置。当前定时任务必须在保存密码的同一 Windows 用户下运行。");

            Log(isScheduled ? "===== 定时备份开始 =====" : "===== 手动备份开始 =====");
            var progress = new Progress<string>(m => Log(m));
            await Task.Run(() => _backupService.Run(cfg, password, progress));
            Log("===== 备份成功 =====");
            trayIcon.ShowBalloonTip(3000, "RockyBackup", "备份完成", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log("备份失败：" + ex);
            trayIcon.ShowBalloonTip(5000, "RockyBackup", "备份失败：" + ex.Message, ToolTipIcon.Error);
            if (!isScheduled)
                MessageBox.Show(this, ex.Message, "备份失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _backupRunning = false;
            ToggleButtons(true);
            UpdateNextBackupLabel();
        }
    }

    private async void SchedulerTimer_Tick(object? sender, EventArgs e)
    {
        if (_backupRunning) return;
        if (!TimeSpan.TryParse(_config.BackupTime, out var scheduled)) return;

        var now = DateTime.Now;
        if (now.Hour != scheduled.Hours || now.Minute != scheduled.Minutes) return;

        var today = now.ToString("yyyy-MM-dd");
        if (_config.LastScheduledRunDate == today) return;

        _config.LastScheduledRunDate = today;
        _configService.Save(_config);
        await RunBackupAsync(isScheduled: true);
    }

    private void UpdateNextBackupLabel()
    {
        if (!TimeSpan.TryParse(_config.BackupTime, out var scheduled))
        {
            lblStatus.Text = "定时：未设置";
            return;
        }

        var now = DateTime.Now;
        var next = DateTime.Today.Add(scheduled);
        if (next <= now || _config.LastScheduledRunDate == now.ToString("yyyy-MM-dd"))
            next = next.AddDays(1);
        lblStatus.Text = $"下次：{next:yyyy-MM-dd HH:mm}";
    }

    private void ConfigureTray()
    {
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Text = "RockyBackup";
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => RestoreFromTray());
        menu.Items.Add("立即备份", null, async (_, _) => await RunBackupAsync(isScheduled: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _allowExit = true;
            trayIcon.Visible = false;
            Close();
        });
        trayIcon.ContextMenuStrip = menu;
    }

    private void HideToTray(bool showTip)
    {
        Hide();
        ShowInTaskbar = false;
        if (showTip)
            trayIcon.ShowBalloonTip(2500, "RockyBackup", "程序已在系统托盘运行，定时备份仍有效。", ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_allowExit && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray(showTip: true);
        }
        else
        {
            trayIcon.Visible = false;
        }
    }

    private void ToggleButtons(bool enabled)
    {
        btnTest.Enabled = enabled;
        btnSave.Enabled = enabled;
        btnBackup.Enabled = enabled;
        btnBrowse.Enabled = enabled;
    }

    private void Log(string message, bool toFile = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Log(message, toFile)));
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        txtLog.AppendText(line + Environment.NewLine);
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.ScrollToCaret();
        if (toFile) _fileLogger.Write(message);
    }
}
