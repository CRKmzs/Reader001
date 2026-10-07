// ---------------------------------------------------------------------------
//  Reader001 安装程序  (Setup.cs)
//  用户级安装：只写 HKCU 和用户目录，不需要管理员权限（清单 asInvoker）。
//  编译环境：C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
//            /target:winexe /platform:x64 /codepage:65001   （C# 5 语言级别）
//  资源名映射：PAY_app_exe / PAY_uninst_exe / PAY_loader.dll / PAY_core.dll /
//              PAY_winforms.dll / PAY_index.html / PAY_css_base.css /
//              PAY_css_shelf.css / PAY_css_reader.css / PAY_js_parser.js /
//              PAY_js_storage.js / PAY_js_app.js /
//              PAY_js_reader.js / PAY_js_boot.js
// ---------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class SetupProgram
{
    internal const string AppName = "Reader001";
    internal const string AppVersion = "1.0.0";
    internal const string AppPublisher = "CRKmzs";
    internal const string UninstallSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Reader001";
    internal const string MarkerFileName = ".reader001-install";

    // 内嵌资源逻辑名（顺序与 PayloadTargets 一一对应）
    internal static readonly string[] PayloadResources = new string[]
    {
        "PAY_app_exe",
        "PAY_uninst_exe",
        "PAY_loader.dll",
        "PAY_core.dll",
        "PAY_winforms.dll",
        "PAY_index.html",
        "PAY_css_base.css",
        "PAY_css_shelf.css",
        "PAY_css_reader.css",
        "PAY_js_parser.js",
        "PAY_js_storage.js",
        "PAY_js_app.js",
        "PAY_js_reader.js",
        "PAY_js_boot.js"
    };

    // 相对安装目录的落盘路径
    internal static readonly string[] PayloadTargets = new string[]
    {
        "Reader001.exe",
        "Uninstall.exe",
        "WebView2Loader.dll",
        "Microsoft.Web.WebView2.Core.dll",
        "Microsoft.Web.WebView2.WinForms.dll",
        "app\\index.html",
        "app\\css\\base.css",
        "app\\css\\shelf.css",
        "app\\css\\reader.css",
        "app\\js\\parser.js",
        "app\\js\\storage.js",
        "app\\js\\app.js",
        "app\\js\\reader.js",
        "app\\js\\boot.js"
    };

    // 安装进度回调（step 从 1 开始）
    internal delegate void ProgressCallback(int step, string message);

    // 原始命令行（提权重启时原样传递）
    internal static string[] RawArgs = new string[0];

    // 本进程最终使用的日志路径（提权重启时传给管理员实例，保证父子写同一份日志）
    internal static string ResolvedLogFile = "";

    // 用户是否用 /LOG= 明确指定了日志位置（否则日志跟随安装目录，安装目录确定前不落盘）
    internal static bool LogFileExplicit;

    // 安装目录里的安装日志文件名
    internal const string SetupLogFileName = "Reader001Setup.log";

    [STAThread]
    private static int Main(string[] args)
    {
        CommandLineOptions options = CommandLineOptions.Parse(args);
        RawArgs = args == null ? new string[0] : args;
        if (options.Help)
        {
            PrintUsage();
            return 0;
        }
        // 只有用户明确用 /LOG= 指定时才在启动阶段创建日志文件；
        // 否则日志先留在内存里，等安装目录确定后写进 <安装目录>\Reader001Setup.log
        // —— 安装过程只允许在安装文件夹里增加文件。
        LogFileExplicit = options.LogFileExplicit && !string.IsNullOrEmpty(options.LogFile);
        if (!LogFileExplicit)
        {
            options.LogFile = null;
        }
        ResolvedLogFile = LogFileExplicit ? options.LogFile : "";
        using (InstallerLog log = new InstallerLog(options.LogFile, options.Elevated))
        {
            bool elevated = InstallerOperations.IsElevated();
            log.Write(AppName + " 安装程序 " + AppVersion + " 启动");
            log.Write("  安装包位置: " + InstallerOperations.InstallerLocation());
            log.Write("  日志文件: " + (LogFileExplicit ? options.LogFile : "(安装目录确定后写 <安装目录>\\" + SetupLogFileName + "，在此之前只输出到控制台)"));
            log.Write("  当前是否已提权: " + (elevated ? "是" : "否"));

            // 默认以管理员身份运行：未提权时先请求提权（/NOELEVATE 可关闭；/ELEVATED 是提权后的内部标记）
            if (!elevated && !options.NoElevate && !options.Elevated)
            {
                log.Write("  [提权] 默认以管理员身份运行，正在请求提权（用 /NOELEVATE 可关闭）…");
                Process elevatedProcess = InstallerOperations.RelaunchElevated(log);
                if (elevatedProcess != null)
                {
                    if (options.Silent)
                    {
                        // 静默安装：等管理员实例结束，把它的退出码原样返回给调用者
                        try { elevatedProcess.WaitForExit(); }
                        catch (Exception) { }
                        int childCode = 0;
                        try { childCode = elevatedProcess.ExitCode; }
                        catch (Exception) { }
                        log.Write("  [提权] 管理员实例已结束，退出码 " + childCode + "（完整安装过程见同一份日志）");
                        return childCode;
                    }
                    log.Write("  [提权] 已在新进程中以管理员身份启动安装程序，当前进程退出");
                    return 0;
                }
                log.Write("  [警告] 未取得管理员权限（用户取消或系统不允许），继续以当前权限安装");
            }

            if (options.Silent)
            {
                return RunSilent(options, log);
            }
            return RunGui(options, log);
        }
    }

    private static void PrintUsage()
    {
        try
        {
            Console.Out.WriteLine("Reader001 安装程序 1.0.0");
            Console.Out.WriteLine("");
            Console.Out.WriteLine("用法: Setup.exe [选项]");
            Console.Out.WriteLine("");
            Console.Out.WriteLine("  /S                 静默安装，不显示任何界面（进度写入标准输出）");
            Console.Out.WriteLine("  /D=<目录>          指定安装目录");
            Console.Out.WriteLine("  /NODESKTOP         不创建桌面快捷方式");
            Console.Out.WriteLine("  /NOSTART           兼容参数（本版起不再创建开始菜单快捷方式）");
            Console.Out.WriteLine("  /NORUN             安装完成后不启动 Reader001");
            Console.Out.WriteLine("  /LOG=<文件>        把安装日志写入指定文件（UTF-8）；默认等安装目录确定后写 <安装目录>" + Path.DirectorySeparatorChar + "Reader001Setup.log（在此之前不创建任何文件）");
            Console.Out.WriteLine("  /STRICT            目标目录不可写时直接失败，不自动改选其他可写目录");
            Console.Out.WriteLine("  /NOELEVATE         不请求管理员权限（默认启动后立即请求提权）");
            Console.Out.WriteLine("  /ELEVATED          内部标记：本次已由安装程序自己提权重启（无需手动指定）");
            Console.Out.WriteLine("  /HELP              显示本帮助并退出");
            Console.Out.WriteLine("");
            Console.Out.WriteLine("默认行为：安装程序启动后立即请求管理员权限（会弹出 UAC 提示）。若选择「否」或系统不允许提权，");
            Console.Out.WriteLine("安装会以当前权限继续，并在目标目录不可写时自动改选；静默安装会等待管理员实例结束并返回它的退出码。");
            Console.Out.WriteLine("安装/运行时只在安装目录内写入文件（data\\ 与 书架\\ 两个文件夹）；桌面只创建 Reader001 快捷方式，不写其它位置。");
            Console.Out.WriteLine("安装目录不可写时，安装程序会自动改选第一个可写目录（候选：默认目录、%LOCALAPPDATA%\\Reader001、%USERPROFILE%\\Reader001），并在日志中记录每一项探测结果。");
            Console.Out.WriteLine("退出码: 0 = 成功, 1 = 失败, 2 = 用户取消");
            Console.Out.WriteLine("默认安装目录: " + InstallerOperations.GetDefaultInstallDir());
        }
        catch (Exception)
        {
            // 没有可用的标准输出时忽略（例如由资源管理器直接启动）
        }
    }

    // ---------------- 静默模式 ----------------
    private static int RunSilent(CommandLineOptions options, InstallerLog log)
    {
        try
        {
            string installDir = InstallerOperations.ResolveInstallDir(options, log);
            if (InstallerOperations.IsForbiddenDirectory(installDir))
            {
                throw new InstallerException("安装目录不安全，已拒绝: " + installDir);
            }
            string probeReason;
            if (!InstallerOperations.IsDirectoryWritable(installDir, out probeReason))
            {
                log.Write("  [探测] " + installDir + " -> 不可写: " + probeReason);
                if (options.Strict)
                {
                    throw new InstallerException(InstallerOperations.WriteFailureMessage(installDir, probeReason));
                }
                List<string> attempts;
                string writable = InstallerOperations.ChooseWritableInstallDir(installDir, log, out attempts);
                if (writable == null)
                {
                    throw new InstallerException(
                        "无法写入任何可用目录，安装已停止。" + Environment.NewLine +
                        InstallerOperations.BuildWriteDiagnostics(installDir, attempts));
                }
                log.Write("  [改选] 原定目录不可写，改用: " + writable);
                installDir = writable;
            }
            else
            {
                log.Write("  [探测] " + installDir + " -> 可写");
            }
            if (!SetupProgram.LogFileExplicit)
            {
                // 安装目录确定了：日志现在才落到安装目录里
                string logPath = Path.Combine(installDir, SetupProgram.SetupLogFileName);
                log.UseFile(logPath);
                log.Write("  [日志] 日志文件: " + logPath);
            }
            log.Write("静默安装开始 -> " + installDir);
            long totalBytes = InstallerOperations.InstallPayload(installDir, log, null);
            totalBytes += InstallerOperations.CreateBookshelfFolder(installDir, log);
            totalBytes += InstallerOperations.WriteMarkerFile(installDir, log);
            bool registered = InstallerOperations.WriteUninstallRegistry(installDir, totalBytes, log);
            if (!options.NoDesktop)
            {
                InstallerOperations.CreateDesktopShortcut(installDir, log);
            }
            InstallerOperations.LaunchApplication(installDir, log, options.NoRun);
            string done = "Reader001 安装完成: " + installDir + "（书架文件夹：" + Path.Combine(installDir, InstallerOperations.BookshelfFolderName) + "）";
            if (!registered)
            {
                done += "（未能写入卸载信息，可能被安全软件拦截；卸载请运行安装目录下的 Uninstall.exe）";
            }
            log.Write(done);
            try { Console.Out.WriteLine(done); }
            catch (Exception) { }
            return 0;
        }
        catch (Exception ex)
        {
            string error = "安装失败: " + ex.Message;
            log.Write(error);
            try { Console.Error.WriteLine(error); }
            catch (Exception) { }
            return 1;
        }
    }

    // ---------------- 图形界面模式 ----------------
    private static int RunGui(CommandLineOptions options, InstallerLog log)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string initialDir = InstallerOperations.ResolveInstallDir(options, log);
        if (InstallerOperations.IsForbiddenDirectory(initialDir))
        {
            initialDir = InstallerOperations.GetDefaultInstallDir();
        }
        string probeReason;
        bool writable = InstallerOperations.IsDirectoryWritable(initialDir, out probeReason);
        log.Write("  [探测] 初始安装目录 " + initialDir + " -> " + (writable ? "可写" : "不可写: " + probeReason));
        using (SetupForm form = new SetupForm(options, log, initialDir))
        {
            Application.Run(form);
            return form.ExitCode;
        }
    }
}

// ---------------------------------------------------------------------------
//  命令行解析
// ---------------------------------------------------------------------------
internal sealed class CommandLineOptions
{
    internal bool Silent;
    internal bool NoDesktop;
    internal bool NoStart;
    internal bool NoRun;
    internal bool Help;
    internal bool Strict;
    internal bool NoElevate;
    internal bool Elevated;
    internal string Dir;
    internal string LogFile;

    // 是否由用户在命令行上明确指定（/LOG=）：只有明确指定时日志才不跟随安装目录
    internal bool LogFileExplicit;

    internal static CommandLineOptions Parse(string[] args)
    {
        CommandLineOptions options = new CommandLineOptions();
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
            else if (upper == "/NODESKTOP" || upper == "-NODESKTOP")
            {
                options.NoDesktop = true;
            }
            else if (upper == "/NOSTART" || upper == "-NOSTART")
            {
                options.NoStart = true;
            }
            else if (upper == "/NORUN" || upper == "-NORUN")
            {
                options.NoRun = true;
            }
            else if (upper == "/STRICT" || upper == "-STRICT")
            {
                options.Strict = true;
            }
            else if (upper == "/NOELEVATE" || upper == "-NOELEVATE")
            {
                options.NoElevate = true;
            }
            else if (upper == "/ELEVATED" || upper == "-ELEVATED")
            {
                options.Elevated = true;
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
                options.LogFileExplicit = true;
            }
            // 其它参数忽略
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
internal sealed class InstallerLog : IDisposable
{
    private readonly object _sync = new object();
    private string _path;

    // 安装目录还没确定时的内存日志：安装目录确定后由 UseFile 一次性写进安装目录
    private readonly List<string> _pending = new List<string>();

    // 本进程为日志创建过的目录（日志搬进安装目录后，若该目录空了就删掉）
    private string _createdDir;

    internal InstallerLog(string logPath) : this(logPath, false)
    {
    }

    // append = true 时追加写入：提权重启后的管理员实例用它续写父进程创建的同一份日志
    internal InstallerLog(string logPath, bool append)
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
                _createdDir = dir;
            }
            if (!append)
            {
                // 先清空：每次安装都从一份新日志开始（append = true 时由父进程清空过，这里不动）
                using (FileStream truncate = new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                }
            }
            _path = logPath;
        }
        catch (Exception)
        {
            _path = null;
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
            if (_path == null)
            {
                // 安装目录还没确定：先留在内存里，不往磁盘写任何东西，等 UseFile 时整体落盘
                if (_pending.Count < 50000)
                {
                    _pending.Add(line);
                }
                return;
            }
            try
            {
                // 每行单独以 FileMode.Append 打开：句柄每次都定位到文件末尾，
                // 因此父进程与提权后的子进程同时写同一份日志也不会互相覆盖
                byte[] bytes = new UTF8Encoding(true).GetBytes(line + Environment.NewLine);
                using (FileStream stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception)
            {
            }
        }
    }

    /* 安装目录确定后再落盘：把此前记在内存里的日志一起写进 <安装目录>\Reader001Setup.log。
       在此之前不创建任何日志文件/目录——安装过程只允许往安装文件夹里增加文件。*/
    internal void UseFile(string logPath)
    {
        if (logPath == null || logPath.Length == 0)
        {
            return;
        }
        lock (_sync)
        {
            if (_path != null && string.Equals(_path, logPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            string stalePath = _path;
            string staleDir = _createdDir;
            try
            {
                string dir = Path.GetDirectoryName(logPath);
                string createdDir = null;
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    createdDir = dir;
                }
                using (StreamWriter writer = new StreamWriter(logPath, false, new UTF8Encoding(true)))
                {
                    for (int i = 0; i < _pending.Count; i++)
                    {
                        writer.WriteLine(_pending[i]);
                    }
                    writer.Flush();
                }
                _pending.Clear();
                _path = logPath;
                _createdDir = createdDir;
                RemoveStale(stalePath, staleDir);
            }
            catch (Exception)
            {
                // 新位置写不进去：保持原状态，日志至少还在内存与标准输出里
            }
        }
    }

    // 日志搬进安装目录后，清掉之前意外落在别处的日志文件与由它创建的空目录
    private static void RemoveStale(string path, string createdDir)
    {
        if (!string.IsNullOrEmpty(path))
        {
            try { if (File.Exists(path)) { File.Delete(path); } }
            catch (Exception) { }
        }
        if (!string.IsNullOrEmpty(createdDir))
        {
            try
            {
                if (Directory.Exists(createdDir) && Directory.GetFileSystemEntries(createdDir).Length == 0)
                {
                    Directory.Delete(createdDir, false);
                }
            }
            catch (Exception) { }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            // 每行都是独立打开的句柄，写到磁盘即结束，这里只需要断开路径
            _path = null;
        }
    }
}

internal sealed class InstallerException : Exception
{
    internal InstallerException(string message) : base(message)
    {
    }
}

// ---------------------------------------------------------------------------
//  安装操作
// ---------------------------------------------------------------------------
internal static class InstallerOperations
{
    // 安装程序自身所在目录
    internal static string InstallerLocation()
    {
        try
        {
            string location = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(location))
            {
                return Path.GetDirectoryName(location);
            }
        }
        catch (Exception)
        {
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    // 无法写入安装目录时的提示（中立描述：只说明失败与系统返回，不推断具体原因）
    internal static string WriteFailureMessage(string targetDir, string reason)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("无法写入安装目录: " + targetDir);
        if (!string.IsNullOrEmpty(reason))
        {
            sb.AppendLine("系统返回: " + reason);
        }
        sb.AppendLine("请确认该目录可写，或改用其他安装目录后重试。");
        return sb.ToString();
    }

    internal static string GetDefaultInstallDir()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\Reader001");
    }

    internal const string BookshelfFolderName = "书架";

    // 书架文件夹：安装目录\\书架 —— 用户把 .txt 放进去，程序启动时自动导入
    internal static long CreateBookshelfFolder(string installDir, InstallerLog log)
    {
        string dir = Path.Combine(installDir, BookshelfFolderName);
        try
        {
            Directory.CreateDirectory(dir);
            if (log != null)
            {
                log.Write("  [书架] 已创建书架文件夹: " + dir);
            }
        }
        catch (Exception ex)
        {
            if (log != null)
            {
                log.Write("  [警告] 无法创建书架文件夹: " + ex.Message);
            }
        }
        return 0;
    }

    // 目录可写性探测：必要时创建目录，写一个探测文件再删除（不会破坏用户已有文件）
    internal static bool IsDirectoryWritable(string dir, out string reason)
    {
        reason = null;
        if (string.IsNullOrEmpty(dir))
        {
            reason = "目录为空";
            return false;
        }
        string probeFile = null;
        try
        {
            bool createdDir = false;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                createdDir = true;
            }
            probeFile = Path.Combine(dir, SetupProgram.MarkerFileName + "-write-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (FileStream stream = new FileStream(probeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] probe = new byte[] { 0x52, 0x30, 0x30, 0x31 };
                stream.Write(probe, 0, probe.Length);
                stream.Flush();
            }
            File.Delete(probeFile);
            probeFile = null;
            if (createdDir)
            {
                try { Directory.Delete(dir, false); } catch (Exception) { }
            }
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            if (probeFile != null)
            {
                try { File.Delete(probeFile); } catch (Exception) { }
            }
            return false;
        }
    }

    // 目标目录不可写时的候选目录（按优先级去重）
    internal static List<string> GetCandidateInstallDirs(string preferred)
    {
        List<string> candidates = new List<string>();
        AddCandidate(candidates, preferred);
        try { AddCandidate(candidates, GetDefaultInstallDir()); } catch (Exception) { }
        try
        {
            AddCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reader001"));
        }
        catch (Exception) { }
        try
        {
            AddCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Reader001"));
        }
        catch (Exception) { }
        return candidates;
    }

    private static void AddCandidate(List<string> candidates, string dir)
    {
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }
        string full;
        try
        {
            full = NormalizePath(dir);
        }
        catch (Exception)
        {
            return;
        }
        if (IsForbiddenDirectory(full))
        {
            return;
        }
        for (int i = 0; i < candidates.Count; i++)
        {
            if (string.Equals(candidates[i], full, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        candidates.Add(full);
    }

    // 依次探测候选目录，返回第一个可写目录；attempts 记录每一项的结果（用于日志与故障提示）
    internal static string ChooseWritableInstallDir(string preferred, InstallerLog log, out List<string> attempts)
    {
        List<string> candidates = GetCandidateInstallDirs(preferred);
        attempts = new List<string>();
        for (int i = 0; i < candidates.Count; i++)
        {
            string reason;
            bool ok = IsDirectoryWritable(candidates[i], out reason);
            attempts.Add(candidates[i] + (ok ? "  [可写]" : "  [不可写] " + reason));
            if (ok)
            {
                if (log != null)
                {
                    log.Write("  [探测] " + candidates[i] + " -> 可写");
                }
                return candidates[i];
            }
            if (log != null)
            {
                log.Write("  [探测] " + candidates[i] + " -> 不可写: " + reason);
            }
        }
        return null;
    }

    internal static string FormatAttempts(List<string> attempts)
    {
        if (attempts == null || attempts.Count == 0)
        {
            return "(无)";
        }
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < attempts.Count; i++)
        {
            sb.AppendLine("  " + (i + 1) + ". " + attempts[i]);
        }
        return sb.ToString().TrimEnd();
    }

    internal static string BuildWriteDiagnostics(string preferred, List<string> attempts)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("安装包位置: " + InstallerLocation());
        sb.AppendLine("原定安装目录: " + preferred);
        sb.AppendLine("已尝试的目录（按优先级）:");
        sb.AppendLine(FormatAttempts(attempts));
        sb.AppendLine("建议: 1) 换一个可写目录（例如桌面或文档里新建的文件夹）；2) 在安全软件中放行本安装程序；3) 以管理员身份运行安装程序。");
        return sb.ToString();
    }

    internal static bool IsElevated()
    {
        try
        {
            using (System.Security.Principal.WindowsIdentity identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                System.Security.Principal.WindowsPrincipal principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    // 提权重启：把原始命令行原样传给提权后的自己（会弹 UAC）；成功返回新进程对象，失败或用户取消返回 null
    internal static Process RelaunchElevated(InstallerLog log)
    {
        try
        {
            string exe = null;
            try
            {
                exe = Assembly.GetExecutingAssembly().Location;
            }
            catch (Exception)
            {
            }
            if (string.IsNullOrEmpty(exe))
            {
                exe = Path.Combine(InstallerLocation(), "Reader001Setup.exe");
            }
            StringBuilder arguments = new StringBuilder();
            string[] raw = SetupProgram.RawArgs;
            for (int i = 0; i < raw.Length; i++)
            {
                string item = raw[i];
                if (string.IsNullOrEmpty(item))
                {
                    continue;
                }
                string upper = item.Trim().ToUpperInvariant();
                if (upper == "/ELEVATED" || upper == "-ELEVATED")
                {
                    continue;
                }
                // 日志位置由父进程决定：这里剔除子进程可能自己猜出来的 /LOG=，下面统一补一条
                if (upper.StartsWith("/LOG=") || upper.StartsWith("-LOG="))
                {
                    continue;
                }
                arguments.Append('"').Append(item.Replace("\"", "")).Append("\" ");
            }
            // 只有用户明确指定的日志才转给子进程；默认日志由子进程自己写进安装目录
            string resolvedLog = SetupProgram.ResolvedLogFile;
            if (SetupProgram.LogFileExplicit && !string.IsNullOrEmpty(resolvedLog))
            {
                arguments.Append('"').Append("/LOG=" + resolvedLog.Replace("\"", "")).Append("\" ");
            }
            arguments.Append("/ELEVATED");
            ProcessStartInfo info = new ProcessStartInfo(exe);
            info.Arguments = arguments.ToString();
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.WorkingDirectory = InstallerLocation();
            Process started = Process.Start(info);
            if (started == null)
            {
                if (log != null)
                {
                    log.Write("  [警告] 提权进程没有成功启动");
                }
                return null;
            }
            if (log != null)
            {
                string pid = "未知";
                try { pid = started.Id.ToString(); }
                catch (Exception) { }
                log.Write("  [提权] 已请求以管理员身份重新运行安装程序（PID " + pid + "）");
            }
            return started;
        }
        catch (Exception ex)
        {
            if (log != null)
            {
                log.Write("  [警告] 提权启动失败或被用户取消: " + ex.Message);
            }
            return null;
        }
    }

    internal static string ResolveInstallDir(CommandLineOptions options, InstallerLog log)
    {
        if (options != null && options.Dir != null && options.Dir.Length > 0)
        {
            try
            {
                string full = NormalizePath(options.Dir);
                log.Write("使用命令行指定的安装目录: " + full);
                return full;
            }
            catch (Exception ex)
            {
                log.Write("警告: /D= 指定的目录无效（" + ex.Message + "），改用默认目录");
            }
        }
        return GetDefaultInstallDir();
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

    // 盘根 / 用户配置文件 / 各种系统目录一律拒绝
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

    // ----- 内嵌资源 -----
    internal static Stream OpenResource(Assembly assembly, string logicalName)
    {
        Stream stream = assembly.GetManifestResourceStream(logicalName);
        if (stream != null)
        {
            return stream;
        }
        string[] names = assembly.GetManifestResourceNames();
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], logicalName, StringComparison.OrdinalIgnoreCase) ||
                names[i].EndsWith("." + logicalName, StringComparison.OrdinalIgnoreCase) ||
                names[i].EndsWith(logicalName, StringComparison.OrdinalIgnoreCase))
            {
                stream = assembly.GetManifestResourceStream(names[i]);
                if (stream != null)
                {
                    return stream;
                }
            }
        }
        return null;
    }

    private static void ValidatePayload(Assembly assembly)
    {
        List<string> missing = new List<string>();
        for (int i = 0; i < SetupProgram.PayloadResources.Length; i++)
        {
            Stream stream = OpenResource(assembly, SetupProgram.PayloadResources[i]);
            if (stream == null)
            {
                missing.Add(SetupProgram.PayloadResources[i]);
            }
            else
            {
                stream.Close();
            }
        }
        if (missing.Count > 0)
        {
            throw new InstallerException("缺少内嵌资源: " + string.Join(", ", missing.ToArray()));
        }
    }

    private static long WriteStream(Stream source, string destination)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        using (FileStream target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                target.Write(buffer, 0, read);
                total += read;
            }
            target.Flush();
        }
        return total;
    }

    internal static long InstallPayload(string installDir, InstallerLog log, SetupProgram.ProgressCallback progress)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        ValidatePayload(assembly);
        try
        {
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InstallerException(WriteFailureMessage(installDir, ex.Message));
        }
        long total = 0;
        for (int i = 0; i < SetupProgram.PayloadResources.Length; i++)
        {
            string logicalName = SetupProgram.PayloadResources[i];
            string relativePath = SetupProgram.PayloadTargets[i];
            if (progress != null)
            {
                progress(i + 1, "正在写出 " + relativePath + " ...");
            }
            string destination = Path.Combine(installDir, relativePath);
            try
            {
                string parent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }
                using (Stream source = OpenResource(assembly, logicalName))
                {
                    if (source == null)
                    {
                        throw new InstallerException("缺少内嵌资源: " + logicalName);
                    }
                    total += WriteStream(source, destination);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InstallerException(WriteFailureMessage(installDir, ex.Message));
            }
            log.Write("  [写出] " + relativePath);
        }
        return total;
    }

    internal static long WriteMarkerFile(string installDir, InstallerLog log)
    {
        string markerPath = Path.Combine(installDir, SetupProgram.MarkerFileName);
        StringBuilder text = new StringBuilder();
        text.AppendLine("# Reader001 install marker - do not edit");
        text.AppendLine("InstallLocation=" + installDir);
        text.AppendLine("Version=" + SetupProgram.AppVersion);
        text.AppendLine("InstalledAt=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        byte[] bytes = new UTF8Encoding(false).GetBytes(text.ToString());
        File.WriteAllBytes(markerPath, bytes);
        log.Write("  [标记] " + markerPath);
        return bytes.Length;
    }

    // 写入 HKCU 卸载信息：失败重试一次 -> 写后重新打开校验 -> 记录完整异常链
    internal static bool WriteUninstallRegistry(string installDir, long totalBytes, InstallerLog log)
    {
        string uninstallExe = Path.Combine(installDir, "Uninstall.exe");
        string mainExe = Path.Combine(installDir, "Reader001.exe");
        Exception lastError = null;
        bool registered = false;
        for (int attempt = 0; attempt < 2 && !registered; attempt++)
        {
            if (attempt > 0)
            {
                System.Threading.Thread.Sleep(400);
                log.Write("  [重试] 再次尝试写入卸载信息 HKCU\\" + SetupProgram.UninstallSubKey);
            }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SetupProgram.UninstallSubKey))
                {
                    if (key == null)
                    {
                        lastError = new InvalidOperationException("CreateSubKey 返回 null");
                        continue;
                    }
                    key.SetValue("DisplayName", SetupProgram.AppName, RegistryValueKind.String);
                    key.SetValue("DisplayVersion", SetupProgram.AppVersion, RegistryValueKind.String);
                    key.SetValue("Publisher", SetupProgram.AppPublisher, RegistryValueKind.String);
                    key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
                    key.SetValue("DisplayIcon", mainExe, RegistryValueKind.String);
                    key.SetValue("UninstallString", "\"" + uninstallExe + "\"", RegistryValueKind.String);
                    key.SetValue("QuietUninstallString", "\"" + uninstallExe + "\" /S", RegistryValueKind.String);
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("EstimatedSize", (int)(totalBytes / 1024L), RegistryValueKind.DWord);
                    key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
                }
                // 写后校验：重新打开注册表项，确认它和关键值真的存在
                using (RegistryKey check = Registry.CurrentUser.OpenSubKey(SetupProgram.UninstallSubKey))
                {
                    if (check != null && check.GetValue("UninstallString") != null)
                    {
                        registered = true;
                    }
                    else
                    {
                        lastError = new InvalidOperationException("写入后校验未通过（重新打开注册表项失败或缺少 UninstallString）");
                    }
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }
        if (registered)
        {
            log.Write("  [注册表] HKCU\\" + SetupProgram.UninstallSubKey + "（已校验）");
            return true;
        }
        log.Write("  [警告] 写入卸载信息失败，已跳过注册表: " + DescribeException(lastError));
        log.Write("         提示：多为安全软件拦截未签名程序的注册表写入。请右键安装程序选择“以管理员身份运行”，或在安全软件中放行后重装；");
        log.Write("         卸载仍可直接运行安装目录下的 Uninstall.exe（它会尝试请求管理员权限）。");
        return false;
    }

    // 把异常链（类型 + 消息）拼成一行，避免只看到笼统的 "调用的目标发生了异常"
    internal static string DescribeException(Exception ex)
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

    internal static string DesktopShortcutPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Reader001.lnk");
    }

    internal static string StartMenuFolderPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Reader001");
    }

    internal static string StartMenuShortcutPath()
    {
        return Path.Combine(StartMenuFolderPath(), "Reader001.lnk");
    }

    internal static void CreateDesktopShortcut(string installDir, InstallerLog log)
    {
        CreateShortcut(DesktopShortcutPath(), Path.Combine(installDir, "Reader001.exe"), installDir, log);
    }

    internal static void CreateStartMenuShortcut(string installDir, InstallerLog log)
    {
        CreateShortcut(StartMenuShortcutPath(), Path.Combine(installDir, "Reader001.exe"), installDir, log);
    }

    // late-bound COM 调用 WScript.Shell（不使用 IShellLink P/Invoke）；失败重试一次并记录完整异常链
    internal static bool CreateShortcut(string shortcutPath, string targetExe, string workingDir, InstallerLog log)
    {
        string lastError = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                System.Threading.Thread.Sleep(400);
                log.Write("  [重试] 再次尝试创建快捷方式: " + shortcutPath);
            }
            string error;
            if (TryCreateShortcut(shortcutPath, targetExe, workingDir, log, out error))
            {
                return true;
            }
            lastError = error;
        }
        if (!string.IsNullOrEmpty(lastError))
        {
            log.Write("  [警告] 创建快捷方式失败 " + shortcutPath + ": " + lastError);
        }
        log.Write("         提示：多为安全软件拦截未签名程序创建快捷方式。请右键安装程序选择“以管理员身份运行”，或在安全软件中放行后重装；");
        log.Write("         也可以手动为 " + targetExe + " 创建桌面快捷方式。");
        return false;
    }

    private static bool TryCreateShortcut(string shortcutPath, string targetExe, string workingDir, InstallerLog log, out string error)
    {
        error = null;
        try
        {
            string parent = Path.GetDirectoryName(shortcutPath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                error = "未注册 WScript.Shell";
                log.Write("  [警告] 未注册 WScript.Shell，跳过快捷方式: " + shortcutPath);
                return false;
            }
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetExe + ",0" });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "Reader001 本地 TXT 阅读器" });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

            // Save 之后先确认文件真的落到磁盘（安全软件可能静默拦截）
            if (!File.Exists(shortcutPath))
            {
                error = "Save 之后快捷方式文件不存在（可能被安全软件拦截）";
                log.Write("  [警告] 快捷方式文件未生成: " + shortcutPath);
                return false;
            }

            // 回读校验
            string actual = ReadShortcutTarget(shellType, shortcutPath);
            bool ok = false;
            if (actual != null)
            {
                try
                {
                    ok = string.Equals(NormalizePath(actual), NormalizePath(targetExe), StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception)
                {
                    ok = false;
                }
            }
            if (!ok)
            {
                error = "目标校验未通过 -> " + (actual == null ? "(无法读取)" : actual);
                log.Write("  [警告] 快捷方式校验未通过: " + shortcutPath + " -> " + (actual == null ? "(无法读取)" : actual));
                return false;
            }
            log.Write("  [快捷方式] " + shortcutPath + "（已校验）");
            return true;
        }
        catch (Exception ex)
        {
            error = DescribeException(ex);
            return false;
        }
    }

    internal static string ReadShortcutTarget(Type shellType, string shortcutPath)
    {
        try
        {
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();
            object target = shortcutType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, shortcut, null);
            return target as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static void LaunchApplication(string installDir, InstallerLog log, bool skip)
    {
        if (skip)
        {
            log.Write("  [跳过] 按 /NORUN 不启动主程序");
            return;
        }
        string exe = Path.Combine(installDir, "Reader001.exe");
        if (!File.Exists(exe))
        {
            log.Write("  [警告] 未找到 Reader001.exe，跳过启动");
            return;
        }
        try
        {
            ProcessStartInfo info = new ProcessStartInfo(exe);
            info.WorkingDirectory = installDir;
            info.UseShellExecute = true;
            Process.Start(info);
            log.Write("  [启动] " + exe);
        }
        catch (Exception ex)
        {
            log.Write("  [警告] 启动 Reader001 失败: " + ex.Message);
        }
    }
}

// ---------------------------------------------------------------------------
//  安装向导窗口
// ---------------------------------------------------------------------------
internal sealed class SetupForm : Form
{
    private readonly InstallerLog _log;
    private readonly bool _noElevate;
    private readonly TextBox _dirBox;
    private readonly CheckBox _desktopBox;
    private readonly CheckBox _runBox;
    private readonly Button _installButton;
    private readonly Button _browseButton;
    private readonly ProgressBar _progress;
    private readonly Label _statusLabel;
    private volatile bool _installing;
    private volatile bool _finished;
    private bool _runDesktop;
    private bool _runApp;
    internal int ExitCode;

    internal SetupForm(CommandLineOptions options, InstallerLog log, string initialDir)
    {
        _log = log;
        _noElevate = options.NoElevate;
        this.Text = "Reader001 安装程序";
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.StartPosition = FormStartPosition.CenterScreen;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.ShowInTaskbar = true;
        this.ClientSize = new Size(520, 264);
        this.AutoScaleMode = AutoScaleMode.None;
        try { this.Font = SystemFonts.MessageBoxFont; }
        catch (Exception) { }
        Icon appIcon = LoadAppIcon();
        if (appIcon != null)
        {
            this.Icon = appIcon;
        }

        Label welcome = new Label();
        welcome.Text = "欢迎使用 Reader001 本地 TXT 阅读器安装程序。" + Environment.NewLine +
                       "程序只写入您指定的安装目录（内含 data\\ 与 书架\\ 两个文件夹），桌面只创建快捷方式。";
        welcome.SetBounds(16, 12, 488, 44);
        this.Controls.Add(welcome);

        Label dirLabel = new Label();
        dirLabel.Text = "安装目录：";
        dirLabel.SetBounds(16, 72, 74, 22);
        this.Controls.Add(dirLabel);

        _dirBox = new TextBox();
        _dirBox.SetBounds(94, 69, 316, 23);
        _dirBox.Text = initialDir;
        this.Controls.Add(_dirBox);

        _browseButton = new Button();
        _browseButton.Text = "浏览...";
        _browseButton.SetBounds(418, 68, 86, 25);
        _browseButton.Click += new EventHandler(OnBrowseClick);
        this.Controls.Add(_browseButton);

        _desktopBox = new CheckBox();
        _desktopBox.Text = "创建桌面快捷方式";
        _desktopBox.SetBounds(18, 106, 300, 24);
        _desktopBox.Checked = !options.NoDesktop;
        this.Controls.Add(_desktopBox);

        _runBox = new CheckBox();
        _runBox.Text = "安装完成后启动 Reader001";
        _runBox.SetBounds(18, 134, 300, 24);
        _runBox.Checked = !options.NoRun;
        this.Controls.Add(_runBox);

        _installButton = new Button();
        _installButton.Text = "开始安装";
        _installButton.SetBounds(16, 170, 110, 30);
        _installButton.Click += new EventHandler(OnInstallClick);
        this.Controls.Add(_installButton);

        _statusLabel = new Label();
        _statusLabel.Text = "准备就绪。";
        _statusLabel.SetBounds(138, 178, 366, 20);
        this.Controls.Add(_statusLabel);

        _progress = new ProgressBar();
        _progress.SetBounds(16, 212, 488, 20);
        _progress.Minimum = 0;
        _progress.Maximum = SetupProgram.PayloadResources.Length + 5;
        _progress.Value = 0;
        this.Controls.Add(_progress);
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = InstallerOperations.OpenResource(assembly, "QDR.app.ico"))
            {
                if (stream != null)
                {
                    using (Icon temporary = new Icon(stream))
                    {
                        return (Icon)temporary.Clone();
                    }
                }
            }
        }
        catch (Exception)
        {
        }
        try
        {
            Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon != null)
            {
                return icon;
            }
        }
        catch (Exception)
        {
        }
        return SystemIcons.Application;
    }

    private void OnBrowseClick(object sender, EventArgs e)
    {
        using (FolderBrowserDialog dialog = new FolderBrowserDialog())
        {
            dialog.Description = "请选择 Reader001 的安装目录";
            dialog.ShowNewFolderButton = true;
            string current = _dirBox.Text.Trim();
            if (current.Length > 0 && Directory.Exists(current))
            {
                dialog.SelectedPath = current;
            }
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedPath.Length > 0)
            {
                _dirBox.Text = dialog.SelectedPath;
            }
        }
    }

    private void OnInstallClick(object sender, EventArgs e)
    {
        if (_installing)
        {
            return;
        }
        string dir = _dirBox.Text.Trim();
        if (dir.Length >= 2 && dir[0] == '"' && dir[dir.Length - 1] == '"')
        {
            dir = dir.Substring(1, dir.Length - 2).Trim();
        }
        if (dir.Length == 0)
        {
            MessageBox.Show(this, "请填写安装目录。", "Reader001 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            dir = InstallerOperations.NormalizePath(dir);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "安装目录无效：" + ex.Message, "Reader001 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (InstallerOperations.IsForbiddenDirectory(dir))
        {
            MessageBox.Show(this, "该目录不能作为安装目录：\r\n" + dir, "Reader001 安装程序", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string probeReason;
        bool writable = InstallerOperations.IsDirectoryWritable(dir, out probeReason);
        if (writable)
        {
            _log.Write("  [探测] " + dir + " -> 可写");
        }
        else
        {
            _log.Write("  [探测] " + dir + " -> 不可写: " + probeReason);
            List<string> attempts;
            string chosen = InstallerOperations.ChooseWritableInstallDir(dir, _log, out attempts);
            if (chosen == null)
            {
                string diagnostics = InstallerOperations.BuildWriteDiagnostics(dir, attempts);
                string text = "无法写入安装目录：" + Environment.NewLine + dir + Environment.NewLine + Environment.NewLine +
                              "系统返回：" + probeReason + Environment.NewLine + Environment.NewLine +
                              diagnostics;
                if (MessageBox.Show(this, text + Environment.NewLine + Environment.NewLine + "是否把这份诊断信息复制到剪贴板？",
                        "Reader001 安装程序", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                {
                    try { Clipboard.SetText("Reader001 安装失败诊断" + Environment.NewLine + text); }
                    catch (Exception) { }
                }
                if (!InstallerOperations.IsElevated() && !_noElevate)
                {
                    if (MessageBox.Show(this, "是否以管理员身份重新运行安装程序再试一次？", "Reader001 安装程序",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        if (InstallerOperations.RelaunchElevated(_log) != null)
                        {
                            _finished = true;
                            _installing = false;
                            ExitCode = 0;
                            this.Close();
                        }
                    }
                }
                return;
            }
            if (MessageBox.Show(this,
                    "无法写入安装目录：" + Environment.NewLine + dir + Environment.NewLine + Environment.NewLine +
                    "系统返回：" + probeReason + Environment.NewLine + Environment.NewLine +
                    "已找到可写目录：" + Environment.NewLine + chosen + Environment.NewLine + Environment.NewLine +
                    "是否改为安装到该目录？",
                    "Reader001 安装程序", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            dir = chosen;
        }

        // 安装目录已经确定：日志现在才落到安装目录里（在此之前只记在内存/标准输出）
        if (!SetupProgram.LogFileExplicit)
        {
            _log.UseFile(Path.Combine(dir, SetupProgram.SetupLogFileName));
        }

        // 在 UI 线程读取复选框状态，避免后台线程访问控件
        _runDesktop = _desktopBox.Checked;
        _runApp = _runBox.Checked;

        _installing = true;
        SetUiEnabled(false);
        _dirBox.Text = dir;
        _statusLabel.Text = "正在安装...";
        _progress.Value = 0;

        Thread worker = new Thread(new ThreadStart(delegate { InstallWorker(dir); }));
        worker.IsBackground = true;
        worker.Start();
    }

    private void SetUiEnabled(bool enabled)
    {
        _dirBox.Enabled = enabled;
        _browseButton.Enabled = enabled;
        _desktopBox.Enabled = enabled;
        _runBox.Enabled = enabled;
        _installButton.Enabled = enabled;
    }

    private void InstallWorker(string installDir)
    {
        int code = 0;
        string message;
        try
        {
            long totalBytes = InstallerOperations.InstallPayload(installDir, _log, new SetupProgram.ProgressCallback(ReportProgress));
            ReportProgress(SetupProgram.PayloadResources.Length + 1, "正在写入安装标记...");
            totalBytes += InstallerOperations.WriteMarkerFile(installDir, _log);
            ReportProgress(SetupProgram.PayloadResources.Length + 2, "正在写入注册表...");
            bool registered = InstallerOperations.WriteUninstallRegistry(installDir, totalBytes, _log);
            ReportProgress(SetupProgram.PayloadResources.Length + 3, "正在准备书架文件夹与快捷方式...");
            InstallerOperations.CreateBookshelfFolder(installDir, _log);
            if (_runDesktop)
            {
                InstallerOperations.CreateDesktopShortcut(installDir, _log);
            }
            ReportProgress(SetupProgram.PayloadResources.Length + 4, "正在完成...");
            if (_runApp)
            {
                InstallerOperations.LaunchApplication(installDir, _log, false);
            }
            _log.Write("安装完成: " + installDir);
            message = "Reader001 安装完成！" + Environment.NewLine + Environment.NewLine +
                      "安装目录：" + installDir + Environment.NewLine +
                      "书架文件夹：" + Path.Combine(installDir, InstallerOperations.BookshelfFolderName) +
                      Environment.NewLine + "（把 .txt 放进去，下次启动会自动导入）";
            if (!registered)
            {
                message += Environment.NewLine + Environment.NewLine +
                           "提示：未能写入“应用和功能”列表中的卸载信息（本机安全软件可能拦截未签名程序的注册表写入），" +
                           "不影响使用；卸载请运行安装目录下的 Uninstall.exe。";
            }
        }
        catch (Exception ex)
        {
            code = 1;
            message = "安装失败：" + ex.Message;
            _log.Write("安装失败: " + ex.Message);
        }
        FinishOnUi(code, message);
    }

    private void ReportProgress(int step, string message)
    {
        UpdateUi(step, message);
    }

    private void UpdateUi(int step, string status)
    {
        if (this.IsDisposed)
        {
            return;
        }
        try
        {
            this.Invoke((MethodInvoker)delegate
            {
                if (step >= _progress.Minimum && step <= _progress.Maximum)
                {
                    _progress.Value = step;
                }
                _statusLabel.Text = status;
            });
        }
        catch (Exception)
        {
        }
    }

    private void FinishOnUi(int code, string message)
    {
        try
        {
            this.Invoke((MethodInvoker)delegate
            {
                _finished = true;
                _installing = false;
                if (code == 0)
                {
                    _progress.Value = _progress.Maximum;
                    _statusLabel.Text = "安装完成。";
                }
                else
                {
                    _statusLabel.Text = "安装失败。";
                }
                ExitCode = code;
                MessageBox.Show(this, message, "Reader001 安装程序", MessageBoxButtons.OK,
                    code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                this.Close();
            });
        }
        catch (Exception)
        {
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_finished)
        {
            if (_installing)
            {
                DialogResult answer = MessageBox.Show(this,
                    "安装正在进行，确定要取消并退出吗？\r\n已经写入的文件将保留。",
                    "Reader001 安装程序", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _log.Write("用户取消安装（安装过程中关闭窗口）");
            }
            else
            {
                _log.Write("用户取消安装");
            }
            ExitCode = 2;
        }
        base.OnFormClosing(e);
    }
}
