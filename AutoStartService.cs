using Microsoft.Win32;

namespace RockyBackup;

public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RockyBackup";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

        if (enabled)
        {
            var exe = Application.ExecutablePath;
            key.SetValue(ValueName, $"\"{exe}\" --tray");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
