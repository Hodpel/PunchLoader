using System;
using System.Drawing;
using System.Windows.Forms;

public sealed class PunchLoaderSetupForm : Form
{
    readonly TextBox gamePath;
    readonly Label stateValue;
    readonly Label backupValue;
    readonly TextBox log;
    readonly Button installButton;
    readonly Button uninstallButton;
    readonly Button refreshButton;

    public PunchLoaderSetupForm()
    {
        Text = "PunchLoader 安装程序 v" + PunchLoaderSetup.Version;
        ClientSize = new Size(620, 430);
        MinimumSize = new Size(636, 469);
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        Label title = new Label();
        title.Text = "PunchLoader";
        title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
        title.AutoSize = true;
        title.Location = new Point(20, 16);
        Controls.Add(title);

        Label description = new Label();
        description.Text = "为 Megabyte Punch 安装、更新或卸载 PunchLoader。";
        description.AutoSize = true;
        description.Location = new Point(22, 55);
        Controls.Add(description);

        Label pathLabel = MakeLabel("游戏目录", 22, 91);
        Controls.Add(pathLabel);
        gamePath = new TextBox();
        gamePath.Location = new Point(105, 87);
        gamePath.Size = new Size(487, 24);
        gamePath.ReadOnly = true;
        Controls.Add(gamePath);

        Controls.Add(MakeLabel("程序集状态", 22, 127));
        stateValue = MakeValueLabel(105, 127);
        Controls.Add(stateValue);

        Controls.Add(MakeLabel("原版备份", 22, 158));
        backupValue = MakeValueLabel(105, 158);
        backupValue.AutoEllipsis = true;
        backupValue.Size = new Size(487, 22);
        Controls.Add(backupValue);

        installButton = MakeButton("安装 / 更新", 22, 198, 128);
        installButton.Click += delegate { RunOperation("安装或更新 PunchLoader", PunchLoaderSetup.Install); };
        Controls.Add(installButton);

        uninstallButton = MakeButton("卸载", 160, 198, 100);
        uninstallButton.Click += delegate { RunOperation("卸载 PunchLoader 并恢复原版程序集", PunchLoaderSetup.Uninstall); };
        Controls.Add(uninstallButton);

        refreshButton = MakeButton("刷新状态", 270, 198, 100);
        refreshButton.Click += delegate { RefreshStatus(); };
        Controls.Add(refreshButton);

        Button closeButton = MakeButton("退出", 492, 198, 100);
        closeButton.Click += delegate { Close(); };
        Controls.Add(closeButton);

        log = new TextBox();
        log.Location = new Point(22, 241);
        log.Size = new Size(570, 164);
        log.Multiline = true;
        log.ReadOnly = true;
        log.ScrollBars = ScrollBars.Vertical;
        log.BackColor = SystemColors.Window;
        Controls.Add(log);

        Shown += delegate { RefreshStatus(); };
    }

    static Label MakeLabel(string text, int x, int y)
    {
        Label label = new Label();
        label.Text = text;
        label.Location = new Point(x, y);
        label.AutoSize = true;
        return label;
    }

    static Label MakeValueLabel(int x, int y)
    {
        Label label = new Label();
        label.Location = new Point(x, y);
        label.AutoSize = true;
        label.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point);
        return label;
    }

    static Button MakeButton(string text, int x, int y, int width)
    {
        Button button = new Button();
        button.Text = text;
        button.Location = new Point(x, y);
        button.Size = new Size(width, 31);
        return button;
    }

    void RefreshStatus()
    {
        SetBusy(true);
        try
        {
            SetupSnapshot snapshot = PunchLoaderSetup.ReadSnapshot();
            gamePath.Text = snapshot.Root;
            stateValue.Text = snapshot.State;
            backupValue.Text = snapshot.BackupValid ? snapshot.BackupPath : "不可用";
            installButton.Enabled = snapshot.CanInstall;
            uninstallButton.Enabled = snapshot.CanUninstall;
            if (!snapshot.GameFound)
                AppendLog("请把 PunchLoader.Setup.exe 放在 MegabytePunch.exe 所在目录。\r\n");
        }
        finally
        {
            refreshButton.Enabled = true;
            UseWaitCursor = false;
        }
    }

    void RunOperation(string description, Func<int> operation)
    {
        DialogResult answer = MessageBox.Show(
            this,
            "确定要" + description + "吗？\r\n\r\n游戏必须处于关闭状态。",
            "PunchLoader 安装程序",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        SetBusy(true);
        log.Clear();
        PunchLoaderSetup.MessageSink = AppendLog;
        try
        {
            int result = operation();
            if (result != 0) throw new InvalidOperationException("操作未成功完成，退出码: " + result);
            RefreshStatus();
            MessageBox.Show(
                this,
                log.Text.Length == 0 ? "操作已成功完成。" : log.Text,
                "操作完成",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("[失败] " + ex.Message + "\r\n");
            MessageBox.Show(this, ex.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshStatus();
        }
        finally
        {
            PunchLoaderSetup.MessageSink = null;
            UseWaitCursor = false;
        }
    }

    void AppendLog(string message)
    {
        if (message == null || message.Length == 0) return;
        log.AppendText(message);
        if (!message.EndsWith("\r\n", StringComparison.Ordinal)) log.AppendText("\r\n");
    }

    void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        installButton.Enabled = !busy;
        uninstallButton.Enabled = !busy;
        refreshButton.Enabled = !busy;
    }
}
