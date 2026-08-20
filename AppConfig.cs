namespace RockyBackup;

public sealed class AppConfig
{
    public string Host { get; set; } = "192.168.148.62";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    public string PasswordProtected { get; set; } = "";
    public string RemoteDirectory { get; set; } = "/backup";
    public string LocalDirectory { get; set; } = @"D:\backup";
    public string BackupTime { get; set; } = "02:00";
    public bool MirrorMode { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public string LastScheduledRunDate { get; set; } = "";
}
