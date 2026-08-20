using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace RockyBackup;

public sealed record BackupResult(int Downloaded, int Skipped, int Deleted, long DownloadedBytes);

internal sealed record RemoteFileInfo(string RemotePath, string RelativePath, long Length, DateTime LastWriteTimeUtc);

public sealed class BackupService
{
    public BackupResult Run(AppConfig config, string password, IProgress<string>? progress = null)
    {
        ValidateConfig(config, password);

        var localRoot = Path.GetFullPath(config.LocalDirectory);
        Directory.CreateDirectory(localRoot);

        var auth = new PasswordAuthenticationMethod(config.Username, password);
        var connectionInfo = new ConnectionInfo(config.Host, config.Port, config.Username, auth)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        using var client = new SftpClient(connectionInfo)
        {
            OperationTimeout = TimeSpan.FromMinutes(10),
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        };

        progress?.Report($"连接 SSH/SFTP：{config.Host}:{config.Port} ...");
        client.Connect();
        progress?.Report("连接成功。正在扫描远程目录...");

        var remoteRoot = NormalizeRemotePath(config.RemoteDirectory);
        if (!client.Exists(remoteRoot))
            throw new DirectoryNotFoundException($"远程目录不存在：{remoteRoot}");

        var files = new List<RemoteFileInfo>();
        var remoteFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remoteDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "" };
        var collisionGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ScanRemote(client, remoteRoot, remoteRoot, files, remoteFiles, remoteDirs, collisionGuard, progress);
        progress?.Report($"扫描完成：{files.Count} 个文件。开始同步...");

        // 先创建所有远程目录，确保空目录也能在本地保持一致。
        foreach (var remoteDir in remoteDirs.Where(d => !string.IsNullOrEmpty(d)))
            Directory.CreateDirectory(BuildLocalPath(localRoot, remoteDir));

        int downloaded = 0, skipped = 0, deleted = 0;
        long downloadedBytes = 0;

        foreach (var file in files)
        {
            var localPath = BuildLocalPath(localRoot, file.RelativePath);
            var localDir = Path.GetDirectoryName(localPath)!;
            Directory.CreateDirectory(localDir);

            if (IsSameFile(localPath, file))
            {
                skipped++;
                progress?.Report($"跳过：{file.RelativePath}");
                continue;
            }

            progress?.Report($"下载：{file.RelativePath}  ({FormatBytes(file.Length)})");
            var tempPath = localPath + ".rockybackup.part";
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
                {
                    client.DownloadFile(file.RemotePath, fs);
                    fs.Flush(true);
                }

                File.SetLastWriteTimeUtc(tempPath, file.LastWriteTimeUtc);
                File.Move(tempPath, localPath, true);
                File.SetLastWriteTimeUtc(localPath, file.LastWriteTimeUtc);
                downloaded++;
                downloadedBytes += file.Length;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        if (config.MirrorMode)
        {
            progress?.Report("镜像模式：检查本地多余文件...");
            deleted = CleanupLocalMirror(localRoot, remoteFiles, remoteDirs, progress);
        }

        client.Disconnect();
        progress?.Report($"同步完成：下载 {downloaded}，跳过 {skipped}，删除 {deleted}，本次下载 {FormatBytes(downloadedBytes)}。");
        return new BackupResult(downloaded, skipped, deleted, downloadedBytes);
    }

    public void TestConnection(AppConfig config, string password)
    {
        ValidateConfig(config, password);
        var auth = new PasswordAuthenticationMethod(config.Username, password);
        var info = new ConnectionInfo(config.Host, config.Port, config.Username, auth)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        using var client = new SftpClient(info) { OperationTimeout = TimeSpan.FromSeconds(30) };
        client.Connect();
        var remoteRoot = NormalizeRemotePath(config.RemoteDirectory);
        if (!client.Exists(remoteRoot))
            throw new DirectoryNotFoundException($"SSH连接成功，但远程目录不存在：{remoteRoot}");
        client.Disconnect();
    }

    private static void ScanRemote(
        SftpClient client,
        string root,
        string current,
        List<RemoteFileInfo> files,
        HashSet<string> remoteFiles,
        HashSet<string> remoteDirs,
        HashSet<string> collisionGuard,
        IProgress<string>? progress)
    {
        foreach (ISftpFile entry in client.ListDirectory(current))
        {
            if (entry.Name is "." or "..") continue;
            if (entry.IsSymbolicLink)
            {
                progress?.Report($"跳过符号链接：{entry.FullName}");
                continue;
            }

            var relative = GetRelativeRemotePath(root, entry.FullName);
            ValidateWindowsRelativePath(relative);

            if (!collisionGuard.Add(relative))
                throw new IOException($"远程目录存在 Windows 无法区分的同名路径（大小写冲突）：{relative}");

            if (entry.IsDirectory)
            {
                remoteDirs.Add(NormalizeRelative(relative));
                ScanRemote(client, root, entry.FullName, files, remoteFiles, remoteDirs, collisionGuard, progress);
            }
            else if (entry.IsRegularFile)
            {
                var rel = NormalizeRelative(relative);
                remoteFiles.Add(rel);
                files.Add(new RemoteFileInfo(entry.FullName, rel, entry.Length, entry.LastWriteTimeUtc));
            }
            else
            {
                progress?.Report($"跳过特殊文件：{entry.FullName}");
            }
        }
    }

    private static bool IsSameFile(string localPath, RemoteFileInfo remote)
    {
        if (!File.Exists(localPath)) return false;
        var info = new FileInfo(localPath);
        if (info.Length != remote.Length) return false;
        var delta = (info.LastWriteTimeUtc - remote.LastWriteTimeUtc).Duration();
        return delta <= TimeSpan.FromSeconds(2);
    }

    private static int CleanupLocalMirror(
        string localRoot,
        HashSet<string> remoteFiles,
        HashSet<string> remoteDirs,
        IProgress<string>? progress)
    {
        int deleted = 0;
        CleanupDirectory(localRoot, localRoot, remoteFiles, remoteDirs, progress, ref deleted);
        return deleted;
    }

    private static void CleanupDirectory(
        string root,
        string current,
        HashSet<string> remoteFiles,
        HashSet<string> remoteDirs,
        IProgress<string>? progress,
        ref int deleted)
    {
        foreach (var file in Directory.EnumerateFiles(current))
        {
            var relative = NormalizeRelative(Path.GetRelativePath(root, file));
            if (relative.EndsWith(".rockybackup.part", StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(file); deleted++; } catch { }
                continue;
            }

            if (!remoteFiles.Contains(relative))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                deleted++;
                progress?.Report($"删除本地多余文件：{relative}");
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(current).ToList())
        {
            var attrs = File.GetAttributes(dir);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                progress?.Report($"跳过本地重解析点：{dir}");
                continue;
            }

            CleanupDirectory(root, dir, remoteFiles, remoteDirs, progress, ref deleted);
            var relative = NormalizeRelative(Path.GetRelativePath(root, dir));
            if (!remoteDirs.Contains(relative) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                deleted++;
                progress?.Report($"删除本地多余目录：{relative}");
            }
        }
    }

    private static string BuildLocalPath(string root, string relative)
    {
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = root;
        foreach (var segment in segments)
            path = Path.Combine(path, segment);

        var full = Path.GetFullPath(path);
        var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"检测到非法相对路径：{relative}");
        return full;
    }

    private static void ValidateWindowsRelativePath(string relative)
    {
        foreach (var segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
                throw new IOException($"非法路径：{relative}");
            if (segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new IOException($"Linux 文件名包含 Windows 不支持的字符：{relative}");
        }
    }

    private static string NormalizeRemotePath(string path)
    {
        path = path.Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(path)) return "/";
        if (!path.StartsWith('/')) path = "/" + path;
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    private static string GetRelativeRemotePath(string root, string full)
    {
        root = root.TrimEnd('/');
        if (string.Equals(full, root, StringComparison.Ordinal)) return "";
        var prefix = root + "/";
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new IOException($"远程路径不在源目录内：{full}");
        return full[prefix.Length..];
    }

    private static string NormalizeRelative(string path) => path.Replace('\\', '/').Trim('/');

    private static void ValidateConfig(AppConfig config, string password)
    {
        if (string.IsNullOrWhiteSpace(config.Host)) throw new ArgumentException("SSH IP/主机不能为空。");
        if (config.Port is < 1 or > 65535) throw new ArgumentException("SSH端口不正确。");
        if (string.IsNullOrWhiteSpace(config.Username)) throw new ArgumentException("SSH账户不能为空。");
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("SSH密码不能为空。");
        if (string.IsNullOrWhiteSpace(config.RemoteDirectory)) throw new ArgumentException("源主机目录不能为空。");
        if (string.IsNullOrWhiteSpace(config.LocalDirectory)) throw new ArgumentException("目标主机目录不能为空。");
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = value;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
}
