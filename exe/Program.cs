/*
 * Reader001 - 本地 TXT 阅读器（单文件桌面版，蓝色主题）
 * ------------------------------------------------------------------
 * exe 里内嵌了全部网页资源（index.html / css / js）、WebView2 运行库和图标：
 *   1. 在本机回环地址起一个极简 HTTP 服务，把内嵌资源喂给内嵌的 WebView2；
 *   2. 用 WinForms 窗口 + WebView2 控件显示界面 —— 不启动任何外部浏览器；
 *   3. 书籍数据存在 WebView2 的用户数据目录（IndexedDB），下次打开书架还在。
 *
 * 编译：build.ps1（只用 .NET Framework 自带的 csc.exe，不需要 NuGet / SDK）。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    internal const string AppTag = "READER001-TXT/1";
    internal const string Title = "Reader001";
    internal const string PingPath = "/__qdr/ping";
    internal const string ShelfListPath = "/__qdr/shelf";
    internal const string ShelfGetPath = "/__qdr/shelf/file";
    internal const string ShelfPutPath = "/__qdr/shelf/save";
    internal const int MaxShelfFileBytes = 64 * 1024 * 1024;

    private static readonly int[] PreferredPorts = new int[] { 47113, 47114, 47115, 47116, 47117, 47118, 47119, 47120 };

    internal static string AppDataDir = "";
    private static string libDir = "";
    private static TcpListener listener;
    private static readonly Dictionary<string, string> Routes = BuildRoutes();
    private static readonly Dictionary<string, string> DiskRoutes = BuildDiskRoutes();
    private static volatile bool keepServing = true;
    private static long lastRequestTicks = DateTime.UtcNow.Ticks;
    private static int requestCount = 0;

    [STAThread]
    private static int Main(string[] args)
    {
        bool serveOnly = false;
        bool selfTest = false;
        bool diag = false;
        string reportDir = null;
        string dataDir = null;
        int wanted = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string a = (args[i] == null ? "" : args[i]).Trim().ToLowerInvariant();
            if (a == "--serve" || a == "--no-browser") serveOnly = true;
            else if (a == "--selftest") selfTest = true;
            else if (a == "--diag") diag = true;
            else if (a == "--out" && i + 1 < args.Length) { reportDir = args[i + 1]; i++; }
            else if (a == "--data" && i + 1 < args.Length) { dataDir = args[i + 1]; i++; }
            else if ((a == "--port" || a == "-p") && i + 1 < args.Length) { int.TryParse(args[i + 1], out wanted); i++; }
            else if (a == "--help" || a == "-h" || a == "/?") { Say(HelpText()); return 0; }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
        {
            Log("UnhandledException: " + (e.ExceptionObject == null ? "(null)" : e.ExceptionObject.ToString()));
        };
        Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
        {
            Log("ThreadException: " + (e.Exception == null ? "(null)" : e.Exception.ToString()));
        };

        // 指定了 --data 就直接用它，避免再去探测/创建其它候选目录
        if (!string.IsNullOrEmpty(dataDir))
        {
            try
            {
                string full = Path.GetFullPath(dataDir);
                Directory.CreateDirectory(full);
                AppDataDir = full;
            }
            catch (Exception ex) { Log("--data 目录不可用: " + ex.Message); }
        }
        if (string.IsNullOrEmpty(AppDataDir)) AppDataDir = ResolveDataDir();
        Log("数据目录: " + AppDataDir);
        PrepareWebView2Libraries();

        // 单实例：已经有窗口在运行时，通知它把窗口调到前台，然后自己退出。
        EventWaitHandle activate = null;
        if (!serveOnly && !selfTest && !diag)
        {
            bool createdNew;
            try
            {
                activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\QidianTxtReader.Activate", out createdNew);
            }
            catch (Exception) { createdNew = true; }
            if (!createdNew)
            {
                try { activate.Set(); } catch (Exception) { }
                try { activate.Close(); } catch (Exception) { }
                return 0;
            }
        }

        // ---- 本地服务 ----
        if (wanted > 0)
        {
            if (!TryListen(wanted))
            {
                ShowError("端口 " + wanted + " 已被占用。\n请换一个端口，例如：Reader001.exe --port 47999");
                return 2;
            }
        }
        else
        {
            for (int i = 0; i < PreferredPorts.Length; i++)
            {
                if (TryListen(PreferredPorts[i])) break;
            }
            if (listener == null)
            {
                try
                {
                    listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    listenPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                }
                catch (Exception ex)
                {
                    ShowError("无法启动本地服务：" + ex.Message);
                    return 3;
                }
            }
        }

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        string url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";

        Thread accepter = new Thread(new ThreadStart(AcceptLoop));
        accepter.IsBackground = true;
        accepter.Start();

        WriteStateFile(url);
        Say(Title + " 已启动：" + url);

        int exitCode;
        try
        {
            if (diag) exitCode = DesktopHost.RunDiag(url, reportDir);
            else if (selfTest) exitCode = DesktopHost.RunSelfTest(url, reportDir);
            else if (serveOnly) { WaitForServiceOnly(); exitCode = 0; }
            else exitCode = DesktopHost.RunWindow(url, activate);
        }
        catch (Exception ex)
        {
            Log("启动窗口失败: " + ex);
            ShowError("启动窗口失败：\n" + ex.Message);
            exitCode = 9;
        }

        keepServing = false;
        try { listener.Stop(); } catch (Exception) { }
        if (activate != null) { try { activate.Close(); } catch (Exception) { } }
        return exitCode;
    }

    /* ------------------------------------------------ WebView2 运行库解包 */

    private static void PrepareWebView2Libraries()
    {
        // 首选解包到 exe 同目录：CLR 默认探测即可绑定，无需 AssemblyResolve
        string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
        libDir = exeDir;
        ExtractLibs(libDir);
        bool nearExe = HasLibs(exeDir);
        if (!nearExe)
        {
            // 次选：数据目录 + 手动解析（AssemblyResolve / SetDllDirectory）
            libDir = Path.Combine(AppDataDir, "lib");
            ExtractLibs(libDir);
            AppDomain.CurrentDomain.AssemblyResolve += OnResolveAssembly;
            TryPreload("Microsoft.Web.WebView2.Core.dll");
            TryPreload("Microsoft.Web.WebView2.WinForms.dll");
        }
        Log("运行库目录: " + libDir + " 与exe同目录=" + nearExe
            + " core=" + File.Exists(Path.Combine(libDir, "Microsoft.Web.WebView2.Core.dll"))
            + " winforms=" + File.Exists(Path.Combine(libDir, "Microsoft.Web.WebView2.WinForms.dll"))
            + " loader=" + File.Exists(Path.Combine(libDir, "WebView2Loader.dll")));
        try { SetDllDirectory(libDir); } catch (Exception) { }
        try
        {
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (path.IndexOf(libDir, StringComparison.OrdinalIgnoreCase) < 0)
            {
                Environment.SetEnvironmentVariable("PATH", libDir + ";" + path);
            }
        }
        catch (Exception) { }
    }

    private static void ExtractLibs(string dir)
    {
        try { Directory.CreateDirectory(dir); } catch (Exception) { }
        ExtractTo("QDR.lib.core.dll", Path.Combine(dir, "Microsoft.Web.WebView2.Core.dll"));
        ExtractTo("QDR.lib.winforms.dll", Path.Combine(dir, "Microsoft.Web.WebView2.WinForms.dll"));
        ExtractTo("QDR.lib.loader.dll", Path.Combine(dir, "WebView2Loader.dll"));
    }

    private static bool HasLibs(string dir)
    {
        return File.Exists(Path.Combine(dir, "Microsoft.Web.WebView2.Core.dll"))
            && File.Exists(Path.Combine(dir, "Microsoft.Web.WebView2.WinForms.dll"))
            && File.Exists(Path.Combine(dir, "WebView2Loader.dll"));
    }

    private static Assembly OnResolveAssembly(object sender, ResolveEventArgs args)
    {
        if (args == null || args.Name == null) return null;
        string simple;
        try { simple = new AssemblyName(args.Name).Name; }
        catch (Exception) { return null; }
        string file = null;
        if (simple == "Microsoft.Web.WebView2.Core") file = Path.Combine(libDir, "Microsoft.Web.WebView2.Core.dll");
        else if (simple == "Microsoft.Web.WebView2.WinForms") file = Path.Combine(libDir, "Microsoft.Web.WebView2.WinForms.dll");
        if (file == null) { Log("AssemblyResolve 未匹配: " + args.Name); return null; }
        try
        {
            if (!File.Exists(file)) { Log("AssemblyResolve 缺文件: " + file); return null; }
            Assembly asm = Assembly.LoadFrom(file);
            Log("AssemblyResolve 成功: " + simple);
            return asm;
        }
        catch (Exception ex) { Log("AssemblyResolve 失败 " + file + " : " + ex.Message); return null; }
    }

    // 内嵌程序集直接以字节数组载入默认加载上下文，避免 LoadFrom 上下文绑定失败。
    private static void TryPreload(string name)
    {
        string file = Path.Combine(libDir, name);
        if (!File.Exists(file)) return;
        try
        {
            Assembly asm = Assembly.Load(File.ReadAllBytes(file));
            Log("预加载成功: " + name + " -> " + asm.FullName);
        }
        catch (Exception ex) { Log("预加载失败 " + name + " : " + ex.Message); }
    }

    private static void ExtractTo(string resource, string target)
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(resource))
            {
                if (s == null) { Log("内嵌资源缺失: " + resource); return; }
                FileInfo fi = new FileInfo(target);
                if (fi.Exists && fi.Length == s.Length) return;
                using (FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) fs.Write(buf, 0, n);
                }
            }
        }
        catch (Exception ex) { Log("解包失败 " + target + " : " + ex.Message); }
    }

    /* 数据目录固定在 exe 同目录的 data\ 下（即安装文件夹内）。
       运行期产生的所有文件——书架数据库、WebView2 配置、日志——都只落在安装文件夹里，
       不再往 %LOCALAPPDATA%、%TEMP%、桌面或其它任何位置写东西。*/
    internal static string ResolveDataDir()
    {
        string dir = Path.Combine(ExeDir(), "data");
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, "write-test.tmp");
            File.WriteAllText(probe, "ok", new UTF8Encoding(false));
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            Log("数据目录不可写: " + ex.Message + "（" + dir + "）");
        }
        return dir;
    }

    /* 书架文件夹：安装文件夹下的 书架\，用户把 .txt 放进去，程序启动时自动导入。*/
    internal static string BookshelfDir()
    {
        string dir = Path.Combine(ExeDir(), "书架");
        try { Directory.CreateDirectory(dir); } catch (Exception) { }
        return dir;
    }

    internal static void Log(string msg)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + "\r\n";
        try { Console.Write(line); } catch (Exception) { }
        try
        {
            string dir = AppDataDir;
            if (string.IsNullOrEmpty(dir))
            {
                // 严格规则：只在安装文件夹内写文件（原先回退到 %LOCALAPPDATA% 会写到安装文件夹之外）
                dir = Path.Combine(ExeDir(), "data");
            }
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "Reader001.log"), line, new UTF8Encoding(false));
        }
        catch (Exception) { }
        try
        {
            string near = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Reader001.log");
            File.AppendAllText(near, line, new UTF8Encoding(false));
        }
        catch (Exception) { }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    /* ------------------------------------------------ HTTP 服务 */

    /* 实际监听端口：Host 头校验用（只接受指向本机本端口的 Host） */
    private static int listenPort;

    private static bool TryListen(int port)
    {
        try
        {
            TcpListener l = new TcpListener(IPAddress.Loopback, port);
            l.Start();
            listener = l;
            listenPort = ((IPEndPoint)l.LocalEndpoint).Port;
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void AcceptLoop()
    {
        while (keepServing)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); }
            catch (Exception) { break; }
            ThreadPool.QueueUserWorkItem(new WaitCallback(HandleClient), client);
        }
    }

    private static void HandleClient(object state)
    {
        TcpClient client = state as TcpClient;
        if (client == null) return;
        try
        {
            client.NoDelay = true;
            using (client)
            {
                NetworkStream ns = client.GetStream();
                byte[] leftover;
                string head = ReadHead(ns, out leftover);
                if (head == null) return;
                string[] lines = head.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0) return;
                string[] parts = lines[0].Split(' ');
                if (parts.Length < 2) return;

                string method = parts[0].ToUpperInvariant();
                string rawTarget = parts[1];
                string path = NormalizePath(rawTarget);

                Interlocked.Increment(ref requestCount);
                Interlocked.Exchange(ref lastRequestTicks, DateTime.UtcNow.Ticks);

                int status = 200;
                string ctype = "text/plain; charset=utf-8";
                byte[] body;

                /* 安全闸门：只服务“本机 + 同源”的请求。
                   - Host 必须是 127.0.0.1 / localhost / [::1]（可带本机端口）：
                     挡住 DNS rebinding（外部域名解析到 127.0.0.1 后由浏览器发起的请求）。
                   - Sec-Fetch-Site 存在时只允许 none（地址栏直接访问）与 same-origin：
                     挡住其它网页或本机其它服务跨站读取本应用资源。*/
                string hostHeader = HeaderValue(lines, "Host");
                string fetchSite = HeaderValue(lines, "Sec-Fetch-Site");
                bool badSite = fetchSite != null && fetchSite.Length > 0
                    && !string.Equals(fetchSite, "none", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(fetchSite, "same-origin", StringComparison.OrdinalIgnoreCase);

                if (!IsAllowedHost(hostHeader))
                {
                    status = 403;
                    body = Encoding.UTF8.GetBytes("403 Forbidden：Host 头不是本机地址");
                }
                else if (badSite)
                {
                    status = 403;
                    body = Encoding.UTF8.GetBytes("403 Forbidden：已拒绝跨站请求");
                }
                else if (rawTarget.Length > 512)
                {
                    status = 414;
                    body = Encoding.UTF8.GetBytes("414 URI Too Long");
                }
                else if (path == PingPath)
                {
                    body = Encoding.UTF8.GetBytes(AppTag);
                }
                else if (path == ShelfListPath)
                {
                    ctype = "application/json; charset=utf-8";
                    body = Encoding.UTF8.GetBytes(ShelfListJson());
                }
                else if (path == ShelfGetPath)
                {
                    string readError;
                    body = ReadShelfFile(QueryValue(rawTarget, "name"), out readError);
                    if (body == null)
                    {
                        status = 404;
                        ctype = "text/plain; charset=utf-8";
                        body = Encoding.UTF8.GetBytes("404 " + (readError == null ? "找不到这个文件" : readError));
                    }
                    else
                    {
                        ctype = "application/octet-stream";
                    }
                }
                else if (path == ShelfPutPath && method == "POST")
                {
                    int declared = ParseContentLength(lines);
                    if (declared <= 0 || declared > MaxShelfFileBytes)
                    {
                        status = 413;
                        body = Encoding.UTF8.GetBytes("413 文件过大或长度未知（单个文件上限 64 MB）");
                    }
                    else
                    {
                        byte[] payload = ReadBody(ns, leftover, declared);
                        string saveError;
                        if (payload == null)
                        {
                            status = 400;
                            body = Encoding.UTF8.GetBytes("400 请求体不完整");
                        }
                        else if (!SaveShelfFile(QueryValue(rawTarget, "name"), payload, out saveError))
                        {
                            status = 400;
                            body = Encoding.UTF8.GetBytes("400 " + saveError);
                        }
                        else
                        {
                            body = Encoding.UTF8.GetBytes("saved");
                        }
                    }
                }
                else if (method != "GET" && method != "HEAD")
                {
                    status = 405;
                    body = Encoding.UTF8.GetBytes("405 Method Not Allowed");
                }
                else if (Routes.ContainsKey(path))
                {
                    body = ReadAsset(path);
                    ctype = ContentType(path);
                    if (body == null)
                    {
                        status = 500;
                        ctype = "text/plain; charset=utf-8";
                        body = Encoding.UTF8.GetBytes("500 资源缺失（磁盘与内嵌都没有）：" + path);
                    }
                }
                else
                {
                    status = 404;
                    body = Encoding.UTF8.GetBytes("404 Not Found: " + path);
                }

                WriteResponse(ns, status, ctype, body, method == "HEAD", ctype.StartsWith("text/html", StringComparison.OrdinalIgnoreCase));
            }
        }
        catch (Exception)
        {
            /* 客户端提前断开等：忽略 */
        }
    }

    private static string ReadHead(NetworkStream ns, out byte[] leftover)
    {
        leftover = null;
        MemoryStream ms = new MemoryStream();
        byte[] buf = new byte[8192];
        while (ms.Length < 32768)
        {
            int n;
            try { n = ns.Read(buf, 0, buf.Length); }
            catch (Exception) { break; }
            if (n <= 0) break;
            ms.Write(buf, 0, n);
            byte[] all = ms.GetBuffer();
            int len = (int)ms.Length;
            for (int i = 3; i < len; i++)
            {
                if (all[i - 3] == 13 && all[i - 2] == 10 && all[i - 1] == 13 && all[i] == 10)
                {
                    int headLen = i + 1;
                    if (len > headLen)
                    {
                        leftover = new byte[len - headLen];
                        Array.Copy(all, headLen, leftover, 0, leftover.Length);
                    }
                    return Encoding.ASCII.GetString(all, 0, headLen);
                }
            }
        }
        if (ms.Length == 0) return null;
        return Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /* ------------------------------------------------ 书架文件夹读写（/__qdr/shelf*）*/

    private static string QueryValue(string rawTarget, string key)
    {
        if (rawTarget == null) return null;
        int q = rawTarget.IndexOf('?');
        if (q < 0) return null;
        string[] pairs = rawTarget.Substring(q + 1).Split('&');
        for (int i = 0; i < pairs.Length; i++)
        {
            int eq = pairs[i].IndexOf('=');
            if (eq <= 0) continue;
            if (!string.Equals(pairs[i].Substring(0, eq), key, StringComparison.OrdinalIgnoreCase)) continue;
            string v = pairs[i].Substring(eq + 1).Replace("+", " ");
            try { return Uri.UnescapeDataString(v); } catch (Exception) { return v; }
        }
        return null;
    }

    private static int ParseContentLength(string[] lines)
    {
        string v = HeaderValue(lines, "Content-Length");
        int n;
        if (v != null && int.TryParse(v.Trim(), out n)) return n;
        return -1;
    }

    private static byte[] ReadBody(NetworkStream ns, byte[] leftover, int length)
    {
        byte[] data = new byte[length];
        int got = 0;
        if (leftover != null && leftover.Length > 0)
        {
            int take = Math.Min(leftover.Length, length);
            Array.Copy(leftover, 0, data, 0, take);
            got = take;
        }
        while (got < length)
        {
            int n;
            try { n = ns.Read(data, got, length - got); }
            catch (Exception) { return null; }
            if (n <= 0) return null;
            got += n;
        }
        return data;
    }

    /* 文件名消毒：只取纯文件名，拒绝路径分隔符与 ..，强制 .txt 后缀。*/
    private static string SafeShelfName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        name = name.Trim().Trim('"');
        int slash = name.LastIndexOfAny(new char[] { '\\', '/' });
        if (slash >= 0) name = name.Substring(slash + 1);
        name = name.Trim();
        if (name.Length == 0 || name.Length > 160) return null;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (name.IndexOf("..", StringComparison.Ordinal) >= 0) return null;
        if (!name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name = name + ".txt";
        return name;
    }

    private static string JsonEscape(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\t') sb.Append("\\t");
            else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ShelfListJson()
    {
        string dir = BookshelfDir();
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"dir\":\"").Append(JsonEscape(dir)).Append("\",\"files\":[");
        bool first = true;
        try
        {
            string[] files = Directory.GetFiles(dir, "*.txt");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                FileInfo fi = new FileInfo(files[i]);
                if (!fi.Exists || fi.Length <= 0) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"name\":\"").Append(JsonEscape(fi.Name)).Append("\",\"size\":").Append(fi.Length).Append('}');
            }
        }
        catch (Exception) { }
        sb.Append("]}");
        return sb.ToString();
    }

    private static byte[] ReadShelfFile(string name, out string error)
    {
        error = null;
        string safe = SafeShelfName(name);
        if (safe == null) { error = "文件名不合法"; return null; }
        string full = Path.Combine(BookshelfDir(), safe);
        try
        {
            if (!File.Exists(full)) { error = "书架文件夹里没有这个文件"; return null; }
            if (new FileInfo(full).Length > MaxShelfFileBytes) { error = "文件超过 64 MB"; return null; }
            return File.ReadAllBytes(full);
        }
        catch (Exception ex) { error = "读取失败：" + ex.Message; return null; }
    }

    private static bool SaveShelfFile(string name, byte[] data, out string error)
    {
        error = null;
        string safe = SafeShelfName(name);
        if (safe == null) { error = "文件名不合法"; return false; }
        if (data == null || data.Length == 0) { error = "内容是空的"; return false; }
        string full = Path.Combine(BookshelfDir(), safe);
        try
        {
            string tmp = full + ".part";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(full)) File.Delete(full);
            File.Move(tmp, full);
            return true;
        }
        catch (Exception ex) { error = "写入失败：" + ex.Message; return false; }
    }

    private static void WriteResponse(NetworkStream ns, int status, string ctype, byte[] body, bool headOnly, bool html)
    {
        if (body == null) body = new byte[0];
        StringBuilder sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(status.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(StatusText(status)).Append("\r\n");
        sb.Append("Content-Type: ").Append(ctype).Append("\r\n");
        sb.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("Cache-Control: no-store, no-cache, must-revalidate\r\n");
        sb.Append("Pragma: no-cache\r\n");
        sb.Append("X-Content-Type-Options: nosniff\r\n");
        /* 安全头：本应用只是本地静态页面，不需要任何外部资源、嵌入与表单提交 */
        sb.Append("X-Frame-Options: DENY\r\n");
        sb.Append("Referrer-Policy: no-referrer\r\n");
        sb.Append("Cross-Origin-Resource-Policy: same-origin\r\n");
        sb.Append("Cross-Origin-Opener-Policy: same-origin\r\n");
        if (html)
        {
            sb.Append("Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'\r\n");
        }
        sb.Append("Connection: close\r\n\r\n");
        byte[] head = Encoding.ASCII.GetBytes(sb.ToString());
        ns.Write(head, 0, head.Length);
        if (!headOnly && body.Length > 0) ns.Write(body, 0, body.Length);
        ns.Flush();
    }

    private static string StatusText(int status)
    {
        if (status == 200) return "OK";
        if (status == 403) return "Forbidden";
        if (status == 404) return "Not Found";
        if (status == 405) return "Method Not Allowed";
        if (status == 414) return "URI Too Long";
        if (status == 500) return "Internal Server Error";
        return "OK";
    }

    private static string HeaderValue(string[] lines, string name)
    {
        if (lines == null) return null;
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            int c = line.IndexOf(':');
            if (c <= 0) continue;
            if (string.Equals(line.Substring(0, c).Trim(), name, StringComparison.OrdinalIgnoreCase))
                return line.Substring(c + 1).Trim();
        }
        return null;
    }

    private static bool IsAllowedHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        string h = host.Trim().ToLowerInvariant();
        string p = ":" + listenPort.ToString(CultureInfo.InvariantCulture);
        if (h.EndsWith(p, StringComparison.Ordinal)) h = h.Substring(0, h.Length - p.Length);
        return h == "127.0.0.1" || h == "localhost" || h == "[::1]" || h == "::1";
    }

    private static string NormalizePath(string target)
    {
        string t = target == null ? "/" : target;
        int q = t.IndexOf('?');
        if (q >= 0) t = t.Substring(0, q);
        int scheme = t.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            int slash = t.IndexOf('/', scheme + 3);
            t = slash >= 0 ? t.Substring(slash) : "/";
        }
        try { t = Uri.UnescapeDataString(t); } catch (Exception) { }
        if (t.Length == 0) t = "/";
        if (t.IndexOf("..", StringComparison.Ordinal) >= 0) return "/__qdr/blocked";
        return t;
    }

    private static string ContentType(string path)
    {
        if (path == "/" || path.EndsWith("/", StringComparison.Ordinal)) return "text/html; charset=utf-8";
        string ext = Path.GetExtension(path);
        ext = ext == null ? "" : ext.ToLowerInvariant();
        if (ext == ".html" || ext == ".htm") return "text/html; charset=utf-8";
        if (ext == ".css") return "text/css; charset=utf-8";
        if (ext == ".js") return "application/javascript; charset=utf-8";
        if (ext == ".ico") return "image/x-icon";
        if (ext == ".png") return "image/png";
        if (ext == ".svg") return "image/svg+xml";
        if (ext == ".json") return "application/json; charset=utf-8";
        if (ext == ".txt") return "text/plain; charset=utf-8";
        return "application/octet-stream";
    }

    private static byte[] ReadResource(string name)
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (MemoryStream ms = new MemoryStream())
                {
                    byte[] buf = new byte[81920];
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                    return ms.ToArray();
                }
            }
        }
        catch (Exception) { return null; }
    }

    private static Dictionary<string, string> BuildRoutes()
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        map["/"] = "QDR.index.html";
        map["/index.html"] = "QDR.index.html";
        map["/favicon.ico"] = "QDR.app.ico";
        map["/css/base.css"] = "QDR.css.base.css";
        map["/css/shelf.css"] = "QDR.css.shelf.css";
        map["/css/reader.css"] = "QDR.css.reader.css";
        map["/js/parser.js"] = "QDR.js.parser.js";
        map["/js/storage.js"] = "QDR.js.storage.js";
        map["/js/app.js"] = "QDR.js.app.js";
        map["/js/reader.js"] = "QDR.js.reader.js";
        map["/js/boot.js"] = "QDR.js.boot.js";
        return map;
    }

    /* 安装版：前端文件放在 exe 同目录的 app\ 下，优先从磁盘读取（改完刷新即可生效）；
       找不到时回退到 exe 内嵌资源，所以单独拷走一个 exe 也能用。
       注意：这里的相对路径来自下面这张固定白名单表，绝不拼接用户传来的 URL，
       因此不存在目录穿越；额外的 StartsWith 校验只是双保险。 */
    private static Dictionary<string, string> BuildDiskRoutes()
    {
        Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        map["/"] = "index.html";
        map["/index.html"] = "index.html";
        map["/css/base.css"] = "css\\base.css";
        map["/css/shelf.css"] = "css\\shelf.css";
        map["/css/reader.css"] = "css\\reader.css";
        map["/js/parser.js"] = "js\\parser.js";
        map["/js/storage.js"] = "js\\storage.js";
        map["/js/app.js"] = "js\\app.js";
        map["/js/reader.js"] = "js\\reader.js";
        map["/js/boot.js"] = "js\\boot.js";
        return map;
    }

    private static byte[] ReadAsset(string path)
    {
        byte[] fromDisk = ReadDiskFile(path);
        if (fromDisk != null) return fromDisk;
        string name;
        return Routes.TryGetValue(path, out name) ? ReadResource(name) : null;
    }

    private static byte[] ReadDiskFile(string path)
    {
        try
        {
            string rel;
            if (!DiskRoutes.TryGetValue(path, out rel)) return null;
            string root = Path.GetFullPath(Path.Combine(ExeDir(), "app"));
            string full = Path.GetFullPath(Path.Combine(root, rel));
            if (full.Length <= root.Length || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!File.Exists(full)) return null;
            return File.ReadAllBytes(full);
        }
        catch (Exception) { return null; }
    }

    internal static string ExeDir()
    {
        try
        {
            string loc = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(loc)) loc = Application.ExecutablePath;
            string dir = Path.GetDirectoryName(loc);
            return string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir;
        }
        catch (Exception) { return Environment.CurrentDirectory; }
    }

    /* ------------------------------------------------ 辅助 */

    private static void WaitForServiceOnly()
    {
        try
        {
            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                keepServing = false;
            };
        }
        catch (Exception) { }
        while (keepServing)
        {
            Thread.Sleep(500);
            long idle = (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastRequestTicks)) / TimeSpan.TicksPerSecond;
            int count = Interlocked.CompareExchange(ref requestCount, 0, 0);
            if (count > 0 && idle > 45) break;
            if (count == 0 && idle > 180) break;
        }
    }

    internal static void OpenSystemBrowser(string url)
    {
        try
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo(url);
            psi.UseShellExecute = true;
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception) { }
    }

    internal static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream("QDR.app.ico"))
            {
                if (s == null) return null;
                return new System.Drawing.Icon(s);
            }
        }
        catch (Exception) { return null; }
    }

    internal static void Say(string msg)
    {
        try { Console.WriteLine(msg); } catch (Exception) { }
    }

    private static void ShowError(string msg)
    {
        try { MessageBox.Show(msg, Title, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        catch (Exception) { Say(msg); }
    }

    private static void WriteStateFile(string url)
    {
        try { File.WriteAllText(Path.Combine(AppDataDir, "last-url.txt"), url, new UTF8Encoding(false)); }
        catch (Exception) { }
    }

    private static string HelpText()
    {
        return Title + " " + AppTag + "\n\n"
            + "用法:\n"
            + "  Reader001.exe                 打开阅读器窗口\n"
            + "  Reader001.exe --port 47113    指定本地服务端口\n"
            + "  Reader001.exe --serve         只启动本地服务，不打开窗口\n"
            + "  Reader001.exe --selftest      运行自检（截图 + 报告后退出）\n";
    }
}
