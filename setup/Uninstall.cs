// ---------------------------------------------------------------------------
//  Reader001 卸载程序  (Uninstall.cs)
//  用户级卸载：只删除 HKCU 注册表项、自己安装目录内的文件，以及指向本程序的快捷方式。
//  编译环境：C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//            /target:winexe /platform:x64 /codepage:65001   （C# 5 语言级别）
//  命令行：/S 静默  /D=<目录>  /PURGE 同时删除书架数据  /LOG=<文件>  /NOELEVATE  /HELP
//  退出码：0 成功，1 失败/拒绝删除，2 用户取消
// ---------------------------------------------------------------------------
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class UninstallProgram
{
    internal const string AppName = "Reader001";
    internal const string AppVersion = "1.0.0";
    internal const string UninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reader001";
    internal const string MarkerFileName = ".reader001-install";
    internal const string MainExeName = "Reader001.exe";
    internal const string UninstallExeName = "Uninstall.exe";
    internal const string BookshelfFolderName = "书架";

    [STAThread]
    private static int Main(string[] args)
    {
        UninstallOptions options = UninstallOptions.Parse(args);
        if (options.Help)
        {
            PrintUsage();
            return 0;
        }

        // 默认以管理员身份运行（/NOELEVATE 可关闭；/ELEVATED 是提权后的内部标记）：
        // 提权后“删除桌面快捷方式 / 删除 HKCU 卸载项 / 删除安装目录”在安全软件拦截或 ACL 受限时更容易成功。
        if (!IsElevated() && !HasFlag(args, "/NOELEVATE") && !HasFlag(args, "/ELEVATED"))
        {
            AppendLogLine(options.LogFile, "  [提权] 默认以管理员身份运行，正在请求提权（用 /NOELEVATE 可关闭）…");
            Process elevated = RelaunchElevated(args);
            if (elevated != null)
            {
                try { elevated.WaitForExit(); }
                catch (Exception) { }
                int childCode = 0;
                try { childCode = elevated.ExitCode; }
                catch (Exception) { }
                AppendLogLine(options.LogFile, "  [提权] 提权进程已结束，退出码 " + childCode);
                return childCode;
            }
            AppendLogLine(options.LogFile, "  [警告] 未能获得管理员权限（被取消或被安全软件拦截），继续以当前用户身份卸载。");
        }

        using (UninstallLog log = new UninstallLog(options.LogFile))
        {
            return Run(options, log);
        }
    }

    private static bool HasFlag(string[] args, string flag)
    {
        if (args == null)
        {
            return false;
        }
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == null)
            {
                continue;
            }
            if (string.Equals(args[i].Trim(), flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsElevated()
    {
        try
        {
            System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 用 runas 重新启动自己并带上 /ELEVATED；剔除 /NOELEVATE 与 /LOG=（日志由父进程用 AppendLogLine 追加）
    private static Process RelaunchElevated(string[] args)
    {
        try
        {
            string exe = null;
            try { exe = Assembly.GetExecutingAssembly().Location; }
            catch (Exception) { }
            if (string.IsNullOrEmpty(exe))
            {
                return null;
            }
            StringBuilder arguments = new StringBuilder();
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string item = args[i];
                    if (item == null)
                    {
                        continue;
                    }
                    string upper = item.Trim().ToUpperInvariant();
                    if (upper == "/NOELEVATE" || upper == "-NOELEVATE" || upper == "/ELEVATED" || upper == "-ELEVATED")
                    {
                        continue;
                    }
                    if (upper.StartsWith("/LOG=") || upper.StartsWith("-LOG="))
                    {
                        continue;
                    }
                    arguments.Append('"').Append(item.Replace("\"", "")).Append("\" ");
                }
            }
            arguments.Append("/ELEVATED");
            ProcessStartInfo info = new ProcessStartInfo(exe);
            info.Arguments = arguments.ToString();
            info.UseShellExecute = true;
            info.Verb = "runas";
            try { info.WorkingDirectory = Path.GetDirectoryName(exe); }
            catch (Exception) { }
            return Process.Start(info);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // 追加一行到日志文件：父进程等待提权子进程时不能持有日志文件句柄，否则子进程开不了日志
    private static void AppendLogLine(string logPath, string message)
    {
        if (logPath == null || logPath.Length == 0)
        {
            return;
        }
        try
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            using (StreamWriter writer = new StreamWriter(logPath, true, new UTF8Encoding(true)))
            {
                writer.WriteLine(line);
            }
        }
        catch (Exception)
        {
        }
    }

    private static void PrintUsage()
    {
        try
        {
            Console.Out.WriteLine("Reader001 卸载程序 1.0.0");
            Console.Out.WriteLine("");
            Console.Out.WriteLine("用法: Uninstall.exe [选项]");
            Console.Out.WriteLine("");
            Console.Out.WriteLine("  /S                 静默卸载，不显示任何界面");
            Console.Out.WriteLine("  /D=<目录>          指定要卸载的安装目录");
            Console.Out.WriteLine("  /PURGE             同时删除安装目录里的 书架\\ 文件夹（默认保留）");
            Console.Out.WriteLine("  /LOG=<文件>        把卸载日志写入指定文件（UTF-8）");
            Console.Out.WriteLine("  /NOELEVATE         不请求管理员权限（默认启动后立即请求提权）");
            Console.Out.WriteLine("  /HELP              显示本帮助并退出");
            Console.Out.WriteLine("");
            Console.Out.WriteLine("没有 /PURGE 时安装目录里的 书架\\ 文件夹默认保留。");
            Console.Out.WriteLine("退出码: 0 = 成功, 1 = 失败/拒绝删除, 2 = 用户取消");
        }
        catch (Exception)
        {
            // 没有可用的标准输出时忽略
        }
    }

    private static int Run(UninstallOptions options, UninstallLog log)
    {
        // 1. 确定安装目录：/D= -> 自身所在目录（需有标记或主程序）-> 注册表 InstallLocation
        string note;
        string installDir = ResolveInstallDir(options, log, out note);
        if (installDir == null)
        {
            ReportProblem(options, log, "无法确定 Reader001 的安装目录。" + Environment.NewLine + note);
            return 1;
        }

        // 2. 安全检查（拒绝时绝不删除任何文件）
        string problem = ValidateTargetDirectory(installDir);
        if (problem != null)
        {
            ReportProblem(options, log, "已拒绝卸载：" + problem + Environment.NewLine + "目标目录：" + installDir);
            return 1;
        }

        // 3. 询问（非静默）；/PURGE 直接删除数据
        bool purgeData = options.Purge;
        if (!options.Silent)
        {
            using (UninstallConfirmDialog dialog = new UninstallConfirmDialog(installDir))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    log.Write("用户取消卸载");
                    return 2;
                }
                purgeData = dialog.PurgeData;
            }
        }

        // 关键：把当前目录移出安装目录。用户双击运行时 cwd 往往就是安装目录，
        // 而 Windows 拒绝删除“仍被某进程当作当前目录”的文件夹，会留下空目录。
        // 注意：必须放在路径解析（/D=、/LOG= 相对路径）之后，否则会改变相对路径的基准。
        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
        }
        catch (Exception)
        {
        }

        log.Write("开始卸载: " + installDir);

        // 4. 卸载步骤（每一步都返回“结束时不残留”的校验结果）
        StopRunningApplication(installDir, log);
        bool desktopCleaned = RemoveDesktopShortcut(installDir, log);
        bool startMenuCleaned = RemoveStartMenuFolder(log);
        bool registryCleaned = RemoveRegistryEntry(log);
        if (purgeData)
        {
            RemoveBookshelfData(log);
        }
        else
        {
            log.Write("  [保留] 旧版数据目录（若存在）: " + BookshelfDataDirectory());
        }
        string bookshelfDir = Path.Combine(installDir, BookshelfFolderName);
        ClearInstallDirectory(installDir, log, !purgeData);

        // 5. 自删除：延迟后用 rd 清理安装目录，然后删掉脚本自身
        ScheduleSelfDelete(installDir, log, !purgeData);

        string summary = "已卸载到 " + installDir;
        if (purgeData)
        {
            summary = summary + "（书架文件夹已删除：" + bookshelfDir + "）";
        }
        else
        {
            summary = summary + "（书架文件夹已保留：" + bookshelfDir + "，里面的 txt 可再次导入）";
        }
        log.Write(summary);
        try { Console.Out.WriteLine(summary); }
        catch (Exception) { }
        if (!options.Silent)
        {
            try
            {
                MessageBox.Show(summary + Environment.NewLine + Environment.NewLine +
                                "卸载程序退出后，安装目录会被自动清理。",
                                "Reader001 卸载程序", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception)
            {
            }
        }
        if (!desktopCleaned || !startMenuCleaned || !registryCleaned)
        {
            string leftOver = "以下项目没能清理干净（通常是被安全软件拦截）：" + Environment.NewLine;
            if (!desktopCleaned)
            {
                leftOver += "· 桌面快捷方式: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Reader001.lnk") + Environment.NewLine;
            }
            if (!startMenuCleaned)
            {
                leftOver += "· 开始菜单目录: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Reader001") + Environment.NewLine;
            }
            if (!registryCleaned)
            {
                leftOver += "· 注册表项: HKCU\\" + UninstallSubKey + Environment.NewLine;
            }
            leftOver += "请手动删除以上残留，或右键 Uninstall.exe 选择“以管理员身份运行”后再卸载一次。";
            log.Write(leftOver);
            if (!options.Silent)
            {
                try
                {
                    MessageBox.Show(leftOver, "Reader001 卸载程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception)
                {
                }
            }
        }
        return 0;
    }

    private static void ReportProblem(UninstallOptions options, UninstallLog log, string message)
    {
        log.Write("错误: " + message);
        try { Console.Error.WriteLine("错误: " + message); }
        catch (Exception) { }
        if (!options.Silent)
        {
            try
            {
                MessageBox.Show(message, "Reader001 卸载程序", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception)
            {
            }
        }
    }

    // ---------------- 安装目录判定 ----------------
    private static string ResolveInstallDir(UninstallOptions options, UninstallLog log, out string note)
    {
        note = "";
        if (options.Dir != null && options.Dir.Length > 0)
        {
            string full;
            try
            {
                full = NormalizePath(options.Dir);
            }
            catch (Exception ex)
            {
                note = "/D= 指定的目录无效：" + ex.Message;
                return null;
            }
            log.Write("使用命令行指定的安装目录: " + full);
            return full;
        }

        string ownDir = null;
        try
        {
            string location = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(location))
            {
                ownDir = Path.GetDirectoryName(location);
            }
        }
        catch (Exception ex)
        {
            log.Write("警告: 无法获取卸载程序自身路径: " + ex.Message);
        }
        if (!string.IsNullOrEmpty(ownDir))
        {
            bool looksInstalled = File.Exists(Path.Combine(ownDir, MarkerFileName)) ||
                                  File.Exists(Path.Combine(ownDir, MainExeName));
            if (looksInstalled)
            {
                log.Write("使用卸载程序所在目录: " + ownDir);
                try
                {
                    return NormalizePath(ownDir);
                }
                catch (Exception ex)
                {
                    log.Write("警告: 卸载程序所在目录无效: " + ex.Message);
                }
            }
            else
            {
                log.Write("卸载程序所在目录没有 " + MarkerFileName + " 或 " + MainExeName + "，改查注册表: " + ownDir);
            }
        }

        string registered = ReadRegisteredInstallLocation(log);
        if (!string.IsNullOrEmpty(registered))
        {
            try
            {
                string full = NormalizePath(registered);
                log.Write("使用注册表 InstallLocation: " + full);
                return full;
            }
            catch (Exception ex)
            {
                note = "注册表 InstallLocation 无效：" + ex.Message;
                return null;
            }
        }
        note = "没有 /D= 参数；卸载程序所在目录不像安装目录；注册表 HKCU\\" + UninstallSubKey + " 也没有 InstallLocation。";
        return null;
    }

    private static string ReadRegisteredInstallLocation(UninstallLog log)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallSubKey))
            {
                if (key == null)
                {
                    return null;
                }
                object value = key.GetValue("InstallLocation");
                string text = value as string;
                if (!string.IsNullOrEmpty(text))
                {
                    return text;
                }
            }
        }
        catch (Exception ex)
        {
            log.Write("警告: 读取注册表失败: " + ex.Message);
        }
        return null;
    }

    // 返回 null 表示通过安全检查，否则返回拒绝原因
    private static string ValidateTargetDirectory(string installDir)
    {
        if (string.IsNullOrEmpty(installDir))
        {
            return "目标目录为空。";
        }
        string full;
        try
        {
            full = NormalizePath(installDir);
        }
        catch (Exception ex)
        {
            return "目标路径无效：" + ex.Message;
        }
        if (!Directory.Exists(full))
        {
            return "目标目录不存在：" + full;
        }
        if (IsForbiddenDirectory(full))
        {
            return "目标目录是盘根、用户根目录或系统目录，出于安全考虑拒绝删除：" + full;
        }
        bool hasMarker = File.Exists(Path.Combine(full, MarkerFileName));
        bool hasExe = File.Exists(Path.Combine(full, MainExeName));
        if (!hasMarker && !hasExe)
        {
            return "目标目录既没有 " + MarkerFileName + " 标记，也没有 " + MainExeName + "：" + full;
        }
        return null;
    }

    internal static string NormalizePath(string path)
    {
        string full = Path.GetFullPath(path);
        while (full.Length > 3 && (full.EndsWith("\\") || full.EndsWith("/")))
        {
            full = full.Substring(0, full.Length - 1);
        }
        return full;
    }

    internal static bool IsForbiddenDirectory(string dir)
    {
        if (dir == null || dir.Length == 0)
        {
            return true;
        }
        string full;
        try
        {
            full = NormalizePath(dir);
        }
        catch (Exception)
        {
            return true;
        }
        if (full.Length < 4)
        {
            return true;
        }
        if (full.Length == 3 && full[1] == ':' && (full[2] == '\\' || full[2] == '/'))
        {
            return true;
        }
        Environment.SpecialFolder[] folders = new Environment.SpecialFolder[]
        {
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.Programs,
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86
        };
        for (int i = 0; i < folders.Length; i++)
        {
            string candidate = null;
            try
            {
                candidate = Environment.GetFolderPath(folders[i]);
            }
            catch (Exception)
            {
                candidate = null;
            }
            if (string.IsNullOrEmpty(candidate))
            {
                continue;
            }
            try
            {
                if (string.Equals(full, NormalizePath(candidate), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (Exception)
            {
            }
        }
        try
        {
            string temp = Path.GetTempPath();
            if (!string.IsNullOrEmpty(temp) && string.Equals(full, NormalizePath(temp), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        catch (Exception)
        {
        }
        return false;
    }

    internal static string BookshelfDataDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reader001");
    }

    // ---------------- 卸载步骤 ----------------
    private static void StopRunningApplication(string installDir, UninstallLog log)
    {
        try
        {
            string expected = Path.Combine(installDir, MainExeName);
            Process[] processes = Process.GetProcessesByName("Reader001");
            for (int i = 0; i < processes.Length; i++)
            {
                Process process = processes[i];
                try
                {
                    string path = null;
                    try
                    {
                        path = process.MainModule.FileName;
                    }
                    catch (Exception)
                    {
                        path = null;
                    }
                    if (path == null)
                    {
                        continue;
                    }
                    bool same = false;
                    try
                    {
                        same = string.Equals(NormalizePath(path), NormalizePath(expected), StringComparison.OrdinalIgnoreCase);
                    }
                    catch (Exception)
                    {
                        same = false;
                    }
                    if (!same)
                    {
                        continue;
                    }
                    process.Kill();
                    process.WaitForExit(5000);
                    log.Write("  [停止] Reader001.exe (PID " + process.Id + ")");
                }
                catch (Exception ex)
                {
                    log.Write("  [警告] 无法停止进程: " + ex.Message);
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 枚举 Reader001 进程失败: " + ex.Message);
        }
    }

    private static bool RemoveDesktopShortcut(string installDir, UninstallLog log)
    {
        string shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Reader001.lnk");
        return RemoveShortcutIfTargets(shortcut, installDir, log);
    }

    // 返回 true 表示结束时不残留该快捷方式（删掉了，或本来就不存在）
    private static bool RemoveShortcutIfTargets(string shortcutPath, string installDir, UninstallLog log)
    {
        if (!File.Exists(shortcutPath))
        {
            log.Write("  [跳过] 快捷方式不存在: " + shortcutPath);
            return true;
        }
        string expected = Path.Combine(installDir, MainExeName);
        string target = ReadShortcutTargetSafe(shortcutPath, log);
        if (target == null)
        {
            log.Write("  [警告] 无法读取快捷方式目标，保留不删: " + shortcutPath);
            return true;
        }
        bool same = false;
        try
        {
            same = string.Equals(NormalizePath(target), NormalizePath(expected), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            same = false;
        }
        if (!same)
        {
            log.Write("  [警告] 快捷方式指向的不是本安装目录（" + target + "），保留不删: " + shortcutPath);
            return true;
        }
        try
        {
            File.Delete(shortcutPath);
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 删除快捷方式失败: " + DescribeException(ex));
        }
        if (File.Exists(shortcutPath))
        {
            log.Write("  [警告] 快捷方式仍然存在，未能删除: " + shortcutPath);
            return false;
        }
        log.Write("  [删除] " + shortcutPath + "（已校验）");
        return true;
    }

    // 把异常链（类型 + 消息）拼成一行，避免只看到笼统的“调用的目标发生了异常”
    private static string DescribeException(Exception ex)
    {
        if (ex == null)
        {
            return "(没有异常信息)";
        }
        StringBuilder text = new StringBuilder();
        Exception current = ex;
        int depth = 0;
        while (current != null && depth < 4)
        {
            if (depth > 0)
            {
                text.Append(" <- ");
            }
            text.Append(current.GetType().Name).Append(": ").Append(current.Message);
            current = current.InnerException;
            depth++;
        }
        if (ex is UnauthorizedAccessException)
        {
            text.Append("（访问被拒绝）");
        }
        return text.ToString();
    }

    // 只用 late-bound COM 调 WScript.Shell 读取 .lnk 目标
    private static string ReadShortcutTargetSafe(string shortcutPath, UninstallLog log)
    {
        try
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                log.Write("  [警告] 未注册 WScript.Shell，无法读取快捷方式");
                return null;
            }
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            object target = shortcutType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            return target as string;
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 读取快捷方式失败 " + shortcutPath + ": " + ex.Message);
            return null;
        }
    }

    private static bool RemoveStartMenuFolder(UninstallLog log)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Reader001");
        if (!Directory.Exists(folder))
        {
            log.Write("  [跳过] 开始菜单目录不存在: " + folder);
            return true;
        }
        string leaf = Path.GetFileName(folder.TrimEnd('\\', '/'));
        if (!string.Equals(leaf, "Reader001", StringComparison.OrdinalIgnoreCase))
        {
            log.Write("  [警告] 开始菜单目录名不是 Reader001，跳过: " + folder);
            return true;
        }
        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 删除开始菜单目录失败: " + DescribeException(ex));
        }
        if (Directory.Exists(folder))
        {
            log.Write("  [警告] 开始菜单目录仍然存在，未能删除: " + folder);
            return false;
        }
        log.Write("  [删除] " + folder + "（已校验）");
        return true;
    }

    private static bool RemoveRegistryEntry(UninstallLog log)
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallSubKey))
            {
                if (key == null)
                {
                    log.Write("  [跳过] 注册表项不存在: HKCU\\" + UninstallSubKey);
                    return true;
                }
            }
            bool deleted = false;
            Exception lastError = null;
            for (int attempt = 0; attempt < 2 && !deleted; attempt++)
            {
                if (attempt > 0)
                {
                    System.Threading.Thread.Sleep(300);
                }
                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(UninstallSubKey, false);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                using (RegistryKey check = Registry.CurrentUser.OpenSubKey(UninstallSubKey))
                {
                    deleted = (check == null);
                }
            }
            if (!deleted)
            {
                log.Write("  [警告] 删除注册表项失败，删除后仍能打开 HKCU\\" + UninstallSubKey + ": " + DescribeException(lastError));
                return false;
            }
            log.Write("  [注册表] 已删除 HKCU\\" + UninstallSubKey + "（已校验）");
            return true;
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 删除注册表项失败: " + DescribeException(ex));
            return false;
        }
    }

    private static void RemoveBookshelfData(UninstallLog log)
    {
        string dataDir = BookshelfDataDirectory();
        if (!Directory.Exists(dataDir))
        {
            log.Write("  [跳过] 书架数据目录不存在: " + dataDir);
            return;
        }
        if (IsForbiddenDirectory(dataDir))
        {
            log.Write("  [警告] 书架数据目录不安全，跳过: " + dataDir);
            return;
        }
        string leaf = Path.GetFileName(dataDir.TrimEnd('\\', '/'));
        if (!string.Equals(leaf, "Reader001", StringComparison.OrdinalIgnoreCase))
        {
            log.Write("  [警告] 书架数据目录名不是 Reader001，跳过: " + dataDir);
            return;
        }
        try
        {
            Directory.Delete(dataDir, true);
            log.Write("  [删除数据] " + dataDir);
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 删除书架数据失败: " + ex.Message);
        }
    }

    // 只动 <install> 里面的东西，Uninstall.exe 自己留给自删除命令处理
    private static void ClearInstallDirectory(string installDir, UninstallLog log, bool keepBookshelf)
    {
        string keep = Path.Combine(installDir, UninstallExeName);
        string[] files;
        try
        {
            files = Directory.GetFiles(installDir);
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 无法枚举安装目录: " + ex.Message);
            return;
        }
        for (int i = 0; i < files.Length; i++)
        {
            if (string.Equals(files[i], keep, StringComparison.OrdinalIgnoreCase))
            {
                log.Write("  [保留] " + files[i] + " （卸载程序自身，稍后由自删除命令清理）");
                continue;
            }
            try
            {
                File.Delete(files[i]);
                log.Write("  [删除] " + files[i]);
            }
            catch (Exception ex)
            {
                log.Write("  [警告] 无法删除 " + files[i] + ": " + ex.Message);
            }
        }
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(installDir);
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 无法枚举子目录: " + ex.Message);
            return;
        }
        for (int i = 0; i < directories.Length; i++)
        {
            if (keepBookshelf && string.Equals(Path.GetFileName(directories[i]), BookshelfFolderName, StringComparison.OrdinalIgnoreCase))
            {
                log.Write("  [保留] 书架文件夹（里面的 txt 不动）: " + directories[i]);
                continue;
            }
            try
            {
                Directory.Delete(directories[i], true);
                log.Write("  [删除目录] " + directories[i]);
            }
            catch (Exception ex)
            {
                log.Write("  [警告] 无法删除目录 " + directories[i] + ": " + ex.Message);
            }
        }
    }

    // 生成自删除脚本（延迟几秒后清理安装目录，并删除脚本自身）。
    // 默认放安装目录里（只往安装文件夹增加文件）；若安装目录不可写，再回退到 %TEMP%、安装目录的上一级。
    private static void ScheduleSelfDelete(string installDir, UninstallLog log, bool keepBookshelf)
    {
        string script = BuildSelfDeleteScript(installDir, keepBookshelf);
        string fileName = "reader001-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".cmd";
        string cmdPath = null;

        string[] candidates = new string[]
        {
            installDir,
            Path.GetTempPath(),
            Path.GetDirectoryName(installDir)
        };
        for (int i = 0; i < candidates.Length && cmdPath == null; i++)
        {
            string dir = candidates[i];
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                continue;
            }
            try
            {
                string path = Path.Combine(dir, fileName);
                File.WriteAllText(path, script, Encoding.Default);
                cmdPath = path;
                if (i > 0)
                {
                    log.Write("  [提示] 安装目录不可写，清理脚本临时放在: " + dir);
                }
            }
            catch (Exception ex)
            {
                log.Write("  [警告] 无法在 " + dir + " 创建自删除脚本: " + ex.Message);
            }
        }

        if (cmdPath == null)
        {
            log.Write("  [警告] 未能创建自删除脚本，安装目录可能残留 Uninstall.exe，请手动删除。");
            TryDeleteSelf(log);
            return;
        }

        try
        {
            ProcessStartInfo info = new ProcessStartInfo("cmd.exe", "/c \"" + cmdPath + "\"");
            info.CreateNoWindow = true;
            info.UseShellExecute = false;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            // 中立工作目录（存在即可，不要求可写）：绝不能是安装目录，
            // 否则 cmd 自己占着该目录，rd /s /q 会失败并留下空目录。
            info.WorkingDirectory = Path.GetTempPath();
            Process.Start(info);
            log.Write("  [自删除] 已启动清理脚本: " + cmdPath + " -> rd /s /q \"" + installDir + "\"");
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 无法启动自删除命令: " + ex.Message);
            TryDeleteSelf(log);
        }
    }

    // 脚本内容：防呆（拒绝空目录/盘根）+ 延迟 + 删目录 + 删脚本自身 + 再删一次目录
    // 脚本内容：防呆（拒绝空目录/盘根）+ 延迟 + 删卸载程序 + 删脚本自身 + 清理安装目录。
    // keepBookshelf 时只做非递归 rd：安装目录里若还有 书架\ 就保留下来（其余文件都已删掉）。
    private static string BuildSelfDeleteScript(string installDir, bool keepBookshelf)
    {
        StringBuilder script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("set \"TARGET=" + installDir + "\"");
        script.AppendLine("if \"%TARGET%\"==\"\" goto :eof");
        script.AppendLine("if \"%TARGET:~1,1%\"==\":\" if \"%TARGET:~3%\"==\"\" goto :eof");
        script.AppendLine("ping -n 4 127.0.0.1 >nul");
        script.AppendLine("del /f /q \"%TARGET%\\Uninstall.exe\" >nul 2>nul");
        script.AppendLine("del /f /q \"%~f0\" >nul 2>nul");
        if (keepBookshelf)
        {
            script.AppendLine("rd \"%TARGET%\" 2>nul");
        }
        else
        {
            script.AppendLine("rd /s /q \"%TARGET%\" 2>nul");
        }
        return script.ToString();
    }

    // 兜底：直接删除运行中的卸载程序自身（NTFS 上标记为“关闭时删除”）
    private static void TryDeleteSelf(UninstallLog log)
    {
        try
        {
            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(self) && File.Exists(self))
            {
                File.Delete(self);
                log.Write("  [自删除] 已直接删除卸载程序自身（进程退出后生效）");
            }
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 无法删除卸载程序自身: " + ex.Message);
        }
    }
}

// ---------------------------------------------------------------------------
//  命令行解析
// ---------------------------------------------------------------------------
internal sealed class UninstallOptions
{
    internal bool Silent;
    internal bool Purge;
    internal bool Help;
    internal string Dir;
    internal string LogFile;

    internal static UninstallOptions Parse(string[] args)
    {
        UninstallOptions options = new UninstallOptions();
        if (args == null)
        {
            return options;
        }
        for (int i = 0; i < args.Length; i++)
        {
            string raw = args[i];
            if (raw == null)
            {
                continue;
            }
            string arg = raw.Trim();
            if (arg.Length == 0)
            {
                continue;
            }
            string upper = arg.ToUpperInvariant();
            if (upper == "/S" || upper == "-S" || upper == "/SILENT" || upper == "-SILENT")
            {
                options.Silent = true;
            }
            else if (upper == "/PURGE" || upper == "-PURGE")
            {
                options.Purge = true;
            }
            else if (upper == "/HELP" || upper == "-HELP" || upper == "/H" || upper == "/?" || upper == "-?")
            {
                options.Help = true;
            }
            else if (upper.StartsWith("/D=") || upper.StartsWith("-D="))
            {
                options.Dir = Unquote(arg.Substring(3));
            }
            else if (upper.StartsWith("/LOG=") || upper.StartsWith("-LOG="))
            {
                options.LogFile = Unquote(arg.Substring(5));
            }
        }
        return options;
    }

    private static string Unquote(string value)
    {
        if (value == null)
        {
            return null;
        }
        string text = value.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
        {
            text = text.Substring(1, text.Length - 2);
        }
        return text.Trim();
    }
}

// ---------------------------------------------------------------------------
//  日志
// ---------------------------------------------------------------------------
internal sealed class UninstallLog : IDisposable
{
    private readonly object _sync = new object();
    private StreamWriter _writer;

    internal UninstallLog(string logPath)
    {
        if (logPath == null || logPath.Length == 0)
        {
            return;
        }
        try
        {
            string dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _writer = new StreamWriter(logPath, false, new UTF8Encoding(true));
            _writer.AutoFlush = true;
        }
        catch (Exception)
        {
            _writer = null;
        }
    }

    internal void Write(string message)
    {
        lock (_sync)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message;
            try
            {
                Console.Out.WriteLine(line);
            }
            catch (Exception)
            {
            }
            if (_writer != null)
            {
                try
                {
                    _writer.WriteLine(line);
                }
                catch (Exception)
                {
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_writer != null)
            {
                try
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
                catch (Exception)
                {
                }
                _writer = null;
            }
        }
    }
}

// ---------------------------------------------------------------------------
//  卸载确认窗口（含“同时删除书架数据”复选框，默认不勾选）
// ---------------------------------------------------------------------------
internal sealed class UninstallConfirmDialog : Form
{
    private readonly CheckBox _purgeBox;
    internal bool PurgeData;

    internal UninstallConfirmDialog(string installDir)
    {
        this.Text = "Reader001 卸载程序";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ClientSize = new Size(486, 220);
        this.AutoScaleMode = AutoScaleMode.None;
        try { this.Font = SystemFonts.MessageBoxFont; }
        catch (Exception) { }
        try
        {
            Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
            {
                this.Icon = icon;
            }
        }
        catch (Exception)
        {
        }

        Label question = new Label();
        question.Text = "确定要卸载 Reader001 吗？" + Environment.NewLine + Environment.NewLine +
                        "安装目录：" + installDir;
        question.SetBounds(16, 14, 454, 64);
        this.Controls.Add(question);

        Label dataLabel = new Label();
        dataLabel.Text = "书架文件夹：" + Path.Combine(installDir, UninstallProgram.BookshelfFolderName) + "（默认保留）";
        dataLabel.SetBounds(16, 82, 454, 20);
        this.Controls.Add(dataLabel);

        _purgeBox = new CheckBox();
        _purgeBox.Text = "同时删除书架文件夹里的 txt（默认保留）";
        _purgeBox.SetBounds(18, 112, 430, 24);
        _purgeBox.Checked = false;
        this.Controls.Add(_purgeBox);

        Button confirm = new Button();
        confirm.Text = "卸载";
        confirm.SetBounds(272, 162, 94, 30);
        confirm.Click += new EventHandler(OnConfirmClick);
        this.Controls.Add(confirm);

        Button cancel = new Button();
        cancel.Text = "取消";
        cancel.SetBounds(374, 162, 94, 30);
        cancel.DialogResult = DialogResult.Cancel;
        this.Controls.Add(cancel);

        this.AcceptButton = confirm;
        this.CancelButton = cancel;
    }

    private void OnConfirmClick(object sender, EventArgs e)
    {
        this.PurgeData = _purgeBox.Checked;
        this.DialogResult = DialogResult.OK;
    }
}
