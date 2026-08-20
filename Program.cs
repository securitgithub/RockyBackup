namespace RockyBackup;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Any(a => string.Equals(a, "--run-backup", StringComparison.OrdinalIgnoreCase)))
        {
            RunHeadlessBackup();
            return;
        }

        var startMinimized = args.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));
        Application.Run(new MainForm(startMinimized));
    }

    private static void RunHeadlessBackup()
    {
        var configService = new ConfigService();
        var config = configService.Load();
        var logger = new FileLogger(configService.LogDirectory);
        var password = ConfigService.UnprotectPassword(config.PasswordProtected);

        try
        {
            logger.Write("===== 命令行备份开始 =====");
            var progress = new ImmediateProgress<string>(logger.Write);
            new BackupService().Run(config, password, progress);
            logger.Write("===== 命令行备份成功 =====");
        }
        catch (Exception ex)
        {
            logger.Write("命令行备份失败：" + ex);
            Environment.ExitCode = 1;
        }
    }
}

internal sealed class ImmediateProgress<T>(Action<T> action) : IProgress<T>
{
    public void Report(T value) => action(value);
}
