/*
 * 桌面窗口：WinForms 窗体 + WebView2 控件（不启动外部浏览器）
 * ------------------------------------------------------------------
 * - ReaderForm        : 主窗口，加载本地服务地址，支持网页里的全屏按钮
 * - DesktopHost       : 运行窗口 / 单实例唤醒 / 自检（截图 + JSON 报告）
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class ReaderForm : Form
{
    private readonly WebView2 view;
    private readonly string startUrl;
    private readonly bool testProfile;

    internal ReaderForm(string url, Icon icon, bool testProfile)
    {
        this.startUrl = url;
        this.testProfile = testProfile;

        Text = Program.Title;
        ClientSize = new Size(1280, 880);
        MinimumSize = new Size(760, 560);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(245, 245, 247);
        if (icon != null)
        {
            try { Icon = icon; } catch (Exception) { }
        }

        view = new WebView2();
        view.Dock = DockStyle.Fill;
        Controls.Add(view);
        Load += OnLoaded;
    }

    internal WebView2 View { get { return view; } }
    internal bool Ready { get; private set; }
    internal string LastError { get; private set; }

    private async void OnLoaded(object sender, EventArgs e)
    {
        try
        {
            string folder = Path.Combine(Program.AppDataDir, testProfile ? "WebView2.selftest" : "WebView2");
            Directory.CreateDirectory(folder);
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, folder, null);
            await view.EnsureCoreWebView2Async(env);

            CoreWebView2 core = view.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.ContainsFullScreenElementChanged += OnFullScreenChanged;

            try
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(
                    "window.__qdErr=[];(function(){function add(m){try{window.__qdErr.push(String(m));}catch(e){}}"
                    + "window.addEventListener('error',function(e){add((e.message||'error')+' @'+(e.filename||'')+':'+(e.lineno||0));});"
                    + "window.addEventListener('unhandledrejection',function(e){add('rejection: '+String(e.reason&&e.reason.message?e.reason.message:e.reason));});"
                    + "window.__qdl=function(k){try{return localStorage.getItem(k);}catch(e){return 'ERR:'+e.message;}};"
                    + "window.__qde=function(){return (window.__qdErr||[]).join(' ; ');};"
                    + "})();");
            }
            catch (Exception) { }

            Ready = true;
            core.Navigate(startUrl);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Program.Log("WebView2 初始化失败: " + ex);
            DialogResult r = MessageBox.Show(this,
                "内嵌浏览器（WebView2）初始化失败：\n" + ex.Message
                + "\n\n是否改用系统默认浏览器打开？（功能相同，但会是浏览器窗口）",
                Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes) Program.OpenSystemBrowser(startUrl);
            Close();
        }
    }

    private void OnFullScreenChanged(object sender, object e)
    {
        bool full = false;
        try { full = view.CoreWebView2.ContainsFullScreenElement; }
        catch (Exception) { }
        try
        {
            if (full)
            {
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else if (WindowState == FormWindowState.Maximized)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
            }
        }
        catch (Exception) { }
    }

    internal void FocusWindow()
    {
        try
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }
        catch (Exception) { }
    }
}

internal static class DesktopHost
{
    /* -------------------------------------------- 正常启动窗口 */
    internal static int RunWindow(string url, EventWaitHandle activate)
    {
        ReaderForm form = new ReaderForm(url, Program.LoadAppIcon(), false);

        if (activate != null)
        {
            Thread watcher = new Thread(new ThreadStart(delegate
            {
                while (true)
                {
                    bool signaled;
                    try { signaled = activate.WaitOne(); }
                    catch (Exception) { break; }
                    if (!signaled) break;
                    try { form.BeginInvoke((MethodInvoker)delegate { form.FocusWindow(); }); }
                    catch (Exception) { break; }
                }
            }));
            watcher.IsBackground = true;
            watcher.Start();
        }

        Application.Run(form);
        return 0;
    }

    /* -------------------------------------------- 诊断：只跑脚本探针 */
    internal static int RunDiag(string url, string outDir)
    {
        if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Environment.CurrentDirectory, "selftest-output");
        try { Directory.CreateDirectory(outDir); } catch (Exception) { }
        ReaderForm form = new ReaderForm(url, Program.LoadAppIcon(), true);
        form.Shown += async delegate
        {
            await DiagSteps(form, url, outDir);
            form.Close();
        };
        Application.Run(form);
        return 0;
    }

    private static async Task Probe(WebView2 view, List<string> lines, string label, string js)
    {
        string res;
        try { res = await view.ExecuteScriptAsync(js); }
        catch (Exception ex) { res = "异常: " + ex.Message; }
        string line = label + " = " + res;
        lines.Add(line);
        Program.Log("[diag] " + line);
    }

    // 书架上当前渲染出的书籍卡片数（-1 表示读取失败）
    private static async Task<int> CountBooks(WebView2 view)
    {
        try
        {
            string raw = await view.ExecuteScriptAsync("document.querySelectorAll('.q-book').length + ''");
            int n;
            if (int.TryParse(raw == null ? "" : raw.Trim().Trim('"'), out n)) return n;
        }
        catch (Exception) { }
        return -1;
    }

    private static async Task DiagSteps(ReaderForm form, string url, string outDir)
    {
        WebView2 view = form.View;
        List<string> lines = new List<string>();
        try
        {
            for (int i = 0; i < 100 && !form.Ready; i++) await Task.Delay(150);
            await Task.Delay(2500);
            await Probe(view, lines, "env",
                "'hash='+location.hash+'|store='+(typeof window.QDStore)+'|reader='+(typeof window.QDReader)"
                + "+'|err='+window.__qde()+'|settings='+window.__qdl('qdr.settings.v1')");
            await Probe(view, lines, "listBooks",
                "window.QDStore.listBooks().then(function(b){return 'n='+b.length+' :: '+b.map(function(x){return x.id+'|'+x.name+'|'+(x.chapters?x.chapters.length:'-')+'|'+(x.words||0)}).join(' , ')})");

            // 本程序不再内置示例书：书架为空时先导入一本，后面的阅读探针才有内容可查
            string probeCount = await view.ExecuteScriptAsync("document.querySelectorAll('.q-book').length + ''");
            if (probeCount != null && probeCount.Trim().Trim('"') == "0")
            {
                await view.ExecuteScriptAsync(DropImportJs(Encoding.UTF8.GetBytes(BuildTestBook()), "diag-book.txt"));
                await Task.Delay(3000);
                lines.Add("自动导入 diag-book.txt（书架原本为空）");
            }
            await Probe(view, lines, "getText(first)",
                "window.QDStore.listBooks().then(function(b){return b.length?b[0].id:'NONE'}).then(function(id){return window.QDStore.getText(id)}).then(function(t){return 'len='+(t?t.length:'null')})");
            await Probe(view, lines, "getBook(first)",
                "window.QDStore.listBooks().then(function(b){return b.length?b[0].id:'NONE'}).then(function(id){return window.QDStore.getBook(id)}).then(function(b){return b?('ok name='+b.name):'NULL'})");
            await Probe(view, lines, "persistent", "Promise.resolve(window.QDStore.isPersistent()).then(function(p){return 'persistent='+p})");
            await Probe(view, lines, "lsWrite",
                "(function(){try{localStorage.setItem('qdr.diag','1');return 'wrote='+localStorage.getItem('qdr.diag');}catch(e){return 'ERR:'+e.message}})()");
            await Probe(view, lines, "idbWrite",
                "window.QDStore.putBook({id:'__diag',name:'诊断',author:'',chapters:[]},'x').then(function(){return 'wrote'});");

            await Probe(view, lines, "setHash",
                "window.QDStore.listBooks().then(function(b){return b.length?b[0].id:'NONE'}).then(function(id){location.hash='#/read/'+id;return 'set'})");
            await Task.Delay(3000);
            await Probe(view, lines, "readerState",
                "'hidden='+document.getElementById('pageReader').hidden+'|isOpen='+(window.QDReader?window.QDReader.isOpen():'-')"
                + "+'|title='+document.getElementById('rdTitle').textContent"
                + "+'|paras='+document.getElementById('rdContent').querySelectorAll('p').length"
                + "+'|toast='+document.getElementById('rdToast').textContent"
                + "+'|hash='+location.hash+'|err='+window.__qde()");
            await Probe(view, lines, "readerHTML",
                "document.getElementById('pageReader').innerHTML.replace(/[\r\n]+/g,' ').slice(0,160)");

            view.CoreWebView2.Navigate(url + "#/");
            await Task.Delay(2600);
            await Probe(view, lines, "reload",
                "'books='+document.querySelectorAll('.q-book').length+'|ls='+window.__qdl('qdr.diag')+'|err='+window.__qde()");
            await Probe(view, lines, "listBooks2",
                "window.QDStore.listBooks().then(function(b){return 'n='+b.length+' :: '+b.map(function(x){return x.id+'|'+(x.chapters?x.chapters.length:'-')}).join(' , ')})");
            await Probe(view, lines, "getText2",
                "window.QDStore.listBooks().then(function(b){return b.length?b[0].id:'NONE'}).then(function(id){return window.QDStore.getText(id)}).then(function(t){return 'text='+(t?t.length:'null')})");
        }
        catch (Exception ex) { lines.Add("诊断异常: " + ex); }
        try { File.WriteAllText(Path.Combine(outDir, "diag-report.txt"), string.Join("\r\n", lines.ToArray()), new UTF8Encoding(false)); }
        catch (Exception) { }
        Program.Say("诊断完成，共 " + lines.Count + " 条。");
    }

    /* -------------------------------------------- 自检：截图 + 报告 */
    internal static int RunSelfTest(string url, string outDir)
    {
        if (string.IsNullOrEmpty(outDir))
        {
            outDir = Path.Combine(Environment.CurrentDirectory, "selftest-output");
        }
        try { Directory.CreateDirectory(outDir); } catch (Exception) { }

        // 自检使用独立 profile：先清空，避免上一轮遗留的书籍影响数量断言
        try
        {
            string profile = Path.Combine(Program.AppDataDir, "WebView2.selftest");
            if (Directory.Exists(profile)) Directory.Delete(profile, true);
        }
        catch (Exception) { }

        List<string> log = new List<string>();
        List<string> fails = new List<string>();
        ReaderForm form = new ReaderForm(url, Program.LoadAppIcon(), true);

        form.Shown += async delegate
        {
            await SelfTestSteps(form, url, outDir, log, fails);
            form.Close();
        };

        Application.Run(form);

        StringBuilder sb = new StringBuilder();
        sb.Append("{\n  \"url\": \"").Append(Esc(url)).Append("\",\n");
        sb.Append("  \"ok\": ").Append(fails.Count == 0 ? "true" : "false").Append(",\n");
        sb.Append("  \"failures\": [");
        for (int i = 0; i < fails.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('"').Append(Esc(fails[i])).Append('"');
        }
        sb.Append("],\n  \"steps\": [");
        for (int i = 0; i < log.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('"').Append(Esc(log[i])).Append('"');
        }
        sb.Append("]\n}\n");

        string reportPath = Path.Combine(outDir, "selftest-report.json");
        try { File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false)); }
        catch (Exception) { }
        Program.Say("自检报告：" + reportPath);
        for (int i = 0; i < fails.Count; i++) Program.Say("自检失败：" + fails[i]);
        return fails.Count == 0 ? 0 : 1;
    }

    private static async Task SelfTestSteps(ReaderForm form, string url, string outDir, List<string> log, List<string> fails)
    {
        WebView2 view = form.View;
        try
        {
            for (int i = 0; i < 100 && !form.Ready; i++) await Task.Delay(150);
            if (!form.Ready)
            {
                fails.Add("WebView2 初始化超时：" + form.LastError);
                return;
            }
            await Task.Delay(2600);

            // 1) 首次启动：书架应当为空，并显示空状态提示（本程序不内置示例书）
            string emptyShelf = await view.ExecuteScriptAsync(
                "'books=' + document.querySelectorAll('.q-book').length"
                + " + '|empty=' + (document.getElementById('shelfEmpty').hidden === false)"
                + " + '|hint=' + document.getElementById('shelfEmpty').textContent.replace(/\\s+/g,' ').trim().slice(0,40)");
            log.Add("空书架: " + emptyShelf);
            if (emptyShelf.IndexOf("books=0") < 0) fails.Add("首次启动书架不是空的：" + emptyShelf);
            if (emptyShelf.IndexOf("empty=true") < 0) fails.Add("空书架没有显示空状态提示：" + emptyShelf);
            await Shot(view, Path.Combine(outDir, "01-shelf-empty.png"));

            // 1b) 导入第一本书：自检不再依赖内置示例书，后续步骤都读这一本
            await view.ExecuteScriptAsync(DropImportJs(Encoding.UTF8.GetBytes(BuildTestBook()), "自动测试书.txt"));
            await Task.Delay(3200);
            string shelf = await view.ExecuteScriptAsync(
                "'books=' + document.querySelectorAll('.q-book').length"
                + " + '|names=' + [].map.call(document.querySelectorAll('.q-book'),function(e){return e.textContent.replace(/\\s+/g,' ').trim().slice(0,36)}).join(' / ')");
            log.Add("导入后书架: " + shelf);
            if (shelf.IndexOf("books=1") < 0) fails.Add("导入测试书失败：" + shelf);
            await Shot(view, Path.Combine(outDir, "01-shelf.png"));

            // 1c) 导入的文件必须复制一份进安装目录的「书架」文件夹（安装/运行只往安装文件夹里增加文件）
            string shelfDir = Program.BookshelfDir();
            string copiedFile = Path.Combine(shelfDir, "自动测试书.txt");
            for (int i = 0; i < 24 && !File.Exists(copiedFile); i++) await Task.Delay(150);
            log.Add("书架文件夹: " + shelfDir + " | 已复制=" + File.Exists(copiedFile)
                + " | txt数=" + Directory.GetFiles(shelfDir, "*.txt").Length);
            if (!File.Exists(copiedFile)) fails.Add("导入的 txt 没有复制进「书架」文件夹：" + copiedFile);

            // 1d) 往「书架」文件夹里放一个 txt，点「⟳ 书架」应能扫描导入；已导入的那本不重复导入
            string scanFile = Path.Combine(shelfDir, "书架扫描测试.txt");
            File.WriteAllText(scanFile, BuildTestBook(), new UTF8Encoding(false));
            // 记录点击「⟳ 书架」期间的所有 fetch（URL 与状态码），便于定位扫描导入问题
            await view.ExecuteScriptAsync(
                "(function(){window.__scanLog=[];var of=window.fetch;window.fetch=function(u,o){"
                + "var p=of.apply(window,arguments);p.then(function(r){window.__scanLog.push(String(u)+' -> '+r.status)})"
                + ".catch(function(e){window.__scanLog.push(String(u)+' !! '+e.message)});return p;};return 'hooked';})()");
            string scanBtn = await view.ExecuteScriptAsync(
                "(function(){var b=document.getElementById('btnShelfScan');if(!b)return 'missing';b.click();"
                + "return b.hidden?'hidden':'visible';})()");
            log.Add("扫描按钮: " + scanBtn);
            if (scanBtn.IndexOf("visible") < 0) fails.Add("「⟳ 书架」按钮不可见：" + scanBtn);
            int scanned = 0;
            for (int i = 0; i < 80; i++)
            {
                await Task.Delay(200);
                scanned = await CountBooks(view);
                if (scanned >= 2) break;
            }
            string afterScan = await view.ExecuteScriptAsync(
                "'books=' + document.querySelectorAll('.q-book').length"
                + " + '|names=' + [].map.call(document.querySelectorAll('.q-book'),function(e){return e.textContent.replace(/\\s+/g,' ').trim().slice(0,18)}).join(' / ')");
            log.Add("扫描「书架」后: " + afterScan);
            log.Add("扫描 fetch: " + (await view.ExecuteScriptAsync("(window.__scanLog||[]).join(' ; ')")));
            await view.ExecuteScriptAsync(
                "window.QDStore.listBooks().then(function(bs){window.__bk=bs.map(function(b){"
                + "return b.name+'|'+(b.sourceName||'-')+'|'+(b.sourceSize===undefined?'-':b.sourceSize)}).join(' , ')})");
            await Task.Delay(700);
            log.Add("库中书: " + (await view.ExecuteScriptAsync("window.__bk||'-'")));
            if (scanned < 2) fails.Add("「书架」扫描导入失败（应为 2 本，已入库的 txt 不再重复导入）：" + afterScan);
            await Shot(view, Path.Combine(outDir, "01b-shelf-folder.png"));
            try { File.Delete(scanFile); File.Delete(copiedFile); } catch (Exception) { }

            // 1e) 把扫描导入的那本从书架删掉，后面的数量断言继续按「书架里只有 1 本」计算
            await view.ExecuteScriptAsync(
                "window.QDStore.listBooks().then(function(bs){for(var i=0;i<bs.length;i++){"
                + "if(bs[i].sourceName==='书架扫描测试.txt'){return window.QDStore.deleteBook(bs[i].id);}}return 0;})"
                + ".then(function(){return window.QDApp.refresh();}).then(function(){window.QDApp.render();})");
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(200);
                if (await CountBooks(view) <= 1) break;
            }
            log.Add("清理扫描测试: books=" + (await CountBooks(view))
                + " | 书架txt数=" + Directory.GetFiles(shelfDir, "*.txt").Length);

            /* 注意：ExecuteScriptAsync 不会等待 Promise（Promise 会被序列化成 {}），
               所以书籍 id 直接从书架卡片的 data-id 上读，保证拿到真实字符串 */
            string idRaw = await view.ExecuteScriptAsync(
                "(function(){var el=document.querySelector('.q-book');return el?el.getAttribute('data-id'):'NONE';})()");
            string bookId = idRaw == null ? "" : idRaw.Trim().Trim('"');
            log.Add("首本书 id: " + bookId);
            if (bookId.Length == 0 || bookId.IndexOf("NONE") >= 0)
            {
                fails.Add("没有拿到书籍 id，无法继续阅读相关自检：" + idRaw);
                return;
            }

            // 2) 阅读页
            view.CoreWebView2.Navigate(url + "#/read/" + bookId);
            await Task.Delay(2600);
            string reader = await view.ExecuteScriptAsync(
                "'title=' + document.getElementById('rdTitle').textContent"
                + " + '|paras=' + document.getElementById('rdContent').querySelectorAll('p').length"
                + " + '|footer=' + document.getElementById('rdFooterInfo').textContent");
            log.Add("阅读页: " + reader);
            if (reader.IndexOf("paras=0") >= 0) fails.Add("阅读页没有正文段落");
            await Shot(view, Path.Combine(outDir, "02-reader.png"));

            // 3) 设置面板 + 夜间主题
            await view.ExecuteScriptAsync("document.getElementById('rdBtnSettings').click();");
            await Task.Delay(600);
            await Shot(view, Path.Combine(outDir, "03-settings.png"));
            await view.ExecuteScriptAsync("document.querySelector('button[data-theme=\"night\"]').click();");
            await Task.Delay(900);
            string night = await view.ExecuteScriptAsync(
                "'theme=' + document.getElementById('pageReader').getAttribute('data-rd-theme')"
                + " + '|color=' + getComputedStyle(document.getElementById('rdArticle')).color");
            log.Add("夜间主题: " + night);
            if (night.IndexOf("theme=night") < 0) fails.Add("夜间主题没有生效：" + night);
            await Shot(view, Path.Combine(outDir, "04-reader-night.png"));

            // 4) 米黄 + 楷体 + 26px，然后重新载入验证设置持久化
            await view.ExecuteScriptAsync(
                "document.querySelector('button[data-theme=\"paper\"]').click();"
                + "document.querySelector('button[data-font=\"kai\"]').click();"
                + "var s=document.getElementById('rdFontSize');s.value=26;s.dispatchEvent(new Event('input',{bubbles:true}));"
                + "document.getElementById('rdSettingsClose').click();");
            await Task.Delay(900);
            string paper = await view.ExecuteScriptAsync(
                "'theme=' + document.getElementById('pageReader').getAttribute('data-rd-theme')"
                + " + '|font=' + document.getElementById('pageReader').getAttribute('data-rd-font')"
                + " + '|size=' + getComputedStyle(document.querySelector('#rdContent p') || document.getElementById('rdArticle')).fontSize");
            log.Add("米黄+楷体: " + paper);
            if (paper.IndexOf("theme=paper") < 0 || paper.IndexOf("font=kai") < 0 || paper.IndexOf("size=26px") < 0)
            {
                fails.Add("阅读设置没有生效：" + paper);
            }
            await Shot(view, Path.Combine(outDir, "05-reader-paper.png"));

            view.CoreWebView2.Navigate(url + "#/read/" + bookId);
            await Task.Delay(2600);
            string again = await view.ExecuteScriptAsync(
                "'theme=' + document.getElementById('pageReader').getAttribute('data-rd-theme')"
                + " + '|font=' + document.getElementById('pageReader').getAttribute('data-rd-font')"
                + " + '|size=' + getComputedStyle(document.querySelector('#rdContent p') || document.getElementById('rdArticle')).fontSize");
            log.Add("重新载入后的设置: " + again);
            if (again.IndexOf("theme=paper") < 0 || again.IndexOf("font=kai") < 0 || again.IndexOf("size=26px") < 0)
            {
                fails.Add("阅读设置没有持久化：" + again);
            }

            // 5) 目录抽屉
            await view.ExecuteScriptAsync("document.getElementById('rdBtnCatalog').click();");
            await Task.Delay(800);
            string catalog = await view.ExecuteScriptAsync(
                "'items=' + document.getElementById('rdCatalogList').querySelectorAll('.rd-cat-item').length");
            log.Add("目录: " + catalog);
            if (catalog.IndexOf("items=0") >= 0) fails.Add("目录没有章节条目");
            await Shot(view, Path.Combine(outDir, "06-catalog.png"));
            await view.ExecuteScriptAsync("document.getElementById('rdBtnCatalog').click();");

            // 6) 翻章
            string before = await view.ExecuteScriptAsync("document.getElementById('rdTitle').textContent");
            await view.ExecuteScriptAsync("document.getElementById('rdArrowNext').click();");
            await Task.Delay(1400);
            string after = await view.ExecuteScriptAsync(
                "document.getElementById('rdTitle').textContent"
                + " + '|progress=' + document.getElementById('rdProgressBar').style.width");
            log.Add("翻章: " + before + " -> " + after);
            if (before == after) fails.Add("点击下一章没有切换章节");

            // 6b) 左右翻章箭头必须固定在视口两侧：正文滚到底部时也不能跑掉
            string arrow = await view.ExecuteScriptAsync(
                "(function(){"
                + "var m=document.querySelector('.rd-main');"
                + "if(m.scrollHeight<=m.clientHeight){document.documentElement.style.setProperty('--rd-width','320px');}"
                + "m.scrollTop=0;var r0=document.getElementById('rdArrowNext').getBoundingClientRect();"
                + "m.scrollTop=m.scrollHeight;"
                + "var a=document.getElementById('rdArrowNext');var b=document.getElementById('rdArrowPrev');"
                + "var r1=a.getBoundingClientRect();var p1=b.getBoundingClientRect();var cs=getComputedStyle(a);"
                + "return 'pos='+cs.position+'|top0='+Math.round(r0.top)+'|top1='+Math.round(r1.top)"
                + "+'|sameTop='+(Math.round(r0.top)===Math.round(r1.top))"
                + "+'|nextLeft='+Math.round(r1.left)+'|prevLeft='+Math.round(p1.left)"
                + "+'|canScroll='+(m.scrollHeight>m.clientHeight)+'|scrolled='+Math.round(m.scrollTop)"
                + "+'|visible='+(cs.opacity!=='0'&&!a.disabled)+'|vw='+window.innerWidth;})()");
            log.Add("翻章箭头(滚动后): " + arrow);
            if (arrow.IndexOf("pos=fixed") < 0) fails.Add("翻章箭头不是固定定位，会随滚轮跑掉：" + arrow);
            if (arrow.IndexOf("sameTop=true") < 0) fails.Add("滚动后箭头位置发生变化：" + arrow);
            if (arrow.IndexOf("canScroll=true") < 0) fails.Add("阅读区没有产生滚动，箭头测试无效：" + arrow);
            if (arrow.IndexOf("scrolled=0") >= 0) fails.Add("阅读区滚动未生效，箭头测试无效：" + arrow);
            string arrowSides = await view.ExecuteScriptAsync(
                "(function(){var n=document.getElementById('rdArrowNext').getBoundingClientRect();"
                + "var p=document.getElementById('rdArrowPrev').getBoundingClientRect();"
                + "return 'vw='+window.innerWidth+'|next='+Math.round(n.left)+'|prev='+Math.round(p.left);})()");
            log.Add("箭头左右位置: " + arrowSides);
            if (!ArrowOnCorrectSides(arrowSides)) fails.Add("箭头没有分别贴在窗口左右两侧：" + arrowSides);
            await view.ExecuteScriptAsync("document.querySelector('.rd-main').scrollTop=Math.round(document.querySelector('.rd-main').scrollHeight/2);'ok'");
            await Task.Delay(500);
            await Shot(view, Path.Combine(outDir, "10-reader-scrolled.png"));
            string arrowEnd = await view.ExecuteScriptAsync(
                "(function(){var m=document.querySelector('.rd-main');m.scrollTop=m.scrollHeight;"
                + "var a=document.getElementById('rdArrowNext').getBoundingClientRect();"
                + "return 'top='+Math.round(a.top)+'|inView='+(a.top>0&&a.bottom<window.innerHeight)+'|scrolledToBottom='+(m.scrollTop>0);})()");
            log.Add("翻章箭头(滚到底): " + arrowEnd);
            if (arrowEnd.IndexOf("inView=true") < 0) fails.Add("滚动到底部后箭头不在可视区内：" + arrowEnd);
            if (arrowEnd.IndexOf("scrolledToBottom=true") < 0) fails.Add("滚动到底部失败：" + arrowEnd);
            await view.ExecuteScriptAsync("document.documentElement.style.removeProperty('--rd-width');document.querySelector('.rd-main').scrollTop=0;'ok'");

            // 7) 再导入一本 TXT（File -> ArrayBuffer -> 解码 -> 章节解析 -> IndexedDB）
            view.CoreWebView2.Navigate(url + "#/");
            await Task.Delay(2400);
            await view.ExecuteScriptAsync(DropImportJs(Encoding.UTF8.GetBytes(BuildTestBook()), "自动测试书2.txt"));
            await Task.Delay(3200);
            string imported = await view.ExecuteScriptAsync(
                "'books=' + document.querySelectorAll('.q-book').length"
                + " + '|text=' + [].map.call(document.querySelectorAll('.q-book'),function(e){return e.textContent.replace(/\\s+/g,' ').trim().slice(0,60)}).join(' // ')");
            log.Add("导入后书架: " + imported);
            if (imported.IndexOf("books=2") < 0) fails.Add("导入后书架数量不是 2：" + imported);
            await Shot(view, Path.Combine(outDir, "07-shelf-after-import.png"));

            // 8) 重新载入，确认书籍已持久化到 IndexedDB
            view.CoreWebView2.Navigate(url + "#/");
            await Task.Delay(2600);
            string persisted = await view.ExecuteScriptAsync("'books=' + document.querySelectorAll('.q-book').length");
            log.Add("重新载入后书架: " + persisted);
            if (persisted.IndexOf("books=2") < 0) fails.Add("书籍没有持久化：" + persisted);

            // 9) 导入 GBK / Big5 编码的 TXT：中文必须正确解码，不能出现乱码
            string gbkName = "GBK编码测试书";
            string gbkBody = gbkName + "\n作者：自检程序\n简介：简体中文 GBK 样本，关键词：编码测试通过。\n\n第一章 编码测试\n这一段简体中文用来验证 GB18030 解码是否正确，关键词：编码测试通过。\n";
            string big5Name = "BIG5編碼測試書";
            string big5Body = big5Name + "\n作者：自檢程式\n簡介：繁體中文 Big5 樣本，關鍵詞：編碼測試通過。\n\n第一章 編碼測試\n這一段繁體中文用來驗證 Big5 解碼是否正確，關鍵詞：編碼測試通過。\n";
            try
            {
                byte[] gbkBytes = Encoding.GetEncoding(936).GetBytes(gbkBody);
                byte[] big5Bytes = Encoding.GetEncoding(950).GetBytes(big5Body);
                await view.ExecuteScriptAsync(DropImportJs(gbkBytes, gbkName + ".txt"));
                await Task.Delay(3400);
                string gbkRes = await view.ExecuteScriptAsync(ShelfProbe("编码测试通过", "GB18030"));
                log.Add("GBK 导入: " + gbkRes);
                if (gbkRes.IndexOf("books=3") < 0) fails.Add("GBK 文件没有导入成功：" + gbkRes);
                if (gbkRes.IndexOf("hit=true") < 0) fails.Add("GBK 正文没有正确解码（找不到预期中文）：" + gbkRes);
                if (gbkRes.IndexOf("repl=true") >= 0) fails.Add("GBK 解码出现替换字符 U+FFFD：" + gbkRes);
                if (gbkRes.IndexOf("GB18030") < 0) fails.Add("书架没有显示 GB18030 编码标记：" + gbkRes);
                await Shot(view, Path.Combine(outDir, "08-shelf-gbk.png"));

                await view.ExecuteScriptAsync(DropImportJs(big5Bytes, big5Name + ".txt"));
                await Task.Delay(3400);
                string big5Res = await view.ExecuteScriptAsync(ShelfProbe("編碼測試通過", "Big5"));
                log.Add("Big5 导入: " + big5Res);
                if (big5Res.IndexOf("books=4") < 0) fails.Add("Big5 文件没有导入成功：" + big5Res);
                if (big5Res.IndexOf("hit=true") < 0) fails.Add("Big5 正文没有正确解码（找不到预期繁体中文）：" + big5Res);
                if (big5Res.IndexOf("repl=true") >= 0) fails.Add("Big5 解码出现替换字符 U+FFFD：" + big5Res);
                if (big5Res.IndexOf("Big5") < 0) fails.Add("书架没有显示 Big5 编码标记：" + big5Res);
                await Shot(view, Path.Combine(outDir, "09-shelf-big5.png"));
            }
            catch (Exception exEnc)
            {
                fails.Add("编码导入自检异常: " + exEnc.Message);
            }

            // 10) 收尾：清掉自检在「书架」文件夹里留下的测试 txt，避免影响下一轮
            string[] testTxts = new string[] { "自动测试书.txt", "自动测试书2.txt", "书架扫描测试.txt", gbkName + ".txt", big5Name + ".txt" };
            for (int i = 0; i < testTxts.Length; i++)
            {
                try
                {
                    string tf = Path.Combine(Program.BookshelfDir(), testTxts[i]);
                    if (File.Exists(tf)) File.Delete(tf);
                }
                catch (Exception) { }
            }
            log.Add("收尾清理「书架」测试文件: 剩余 txt=" + Directory.GetFiles(Program.BookshelfDir(), "*.txt").Length);
        }
        catch (Exception ex)
        {
            fails.Add("自检异常: " + ex.Message);
        }
    }

    private static int IntVal(string s, string key)
    {
        if (s == null) return -1;
        int i = s.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return -1;
        string rest = s.Substring(i + key.Length);
        int end = rest.IndexOf('|');
        if (end >= 0) rest = rest.Substring(0, end);
        /* ExecuteScriptAsync 返回的是 JSON 字符串，末尾会带一个引号，需要一并去掉 */
        rest = rest.Trim().Trim('"');
        int v;
        return int.TryParse(rest, out v) ? v : -1;
    }

    /* 上一章箭头应贴左半边、下一章箭头应贴右半边 */
    private static bool ArrowOnCorrectSides(string s)
    {
        int vw = IntVal(s, "vw=");
        int next = IntVal(s, "next=");
        int prev = IntVal(s, "prev=");
        if (vw <= 0 || next < 0 || prev < 0) return false;
        return next >= vw / 2 && prev <= vw / 2;
    }

    private static async Task Shot(WebView2 view, string path)
    {
        try
        {
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                await view.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fs);
            }
        }
        catch (Exception) { }
    }

    private static string BuildTestBook()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("自动测试书\n作者：自检程序\n简介：用于验证导入流程的样例文本。\n\n");
        for (int c = 1; c <= 6; c++)
        {
            sb.Append("第").Append(c).Append("章 自动测试章节").Append(c).Append("\n");
            for (int p = 1; p <= 12; p++)
            {
                sb.Append("这是第").Append(c).Append("章的第").Append(p).Append("段文字，用来验证章节切分、字数统计与阅读排版是否正确显示。\n");
            }
            sb.Append("\n");
        }
        return sb.ToString();
    }

    /* 用给定的字节构造一个 File 并触发书架的导入流程（用于验证各种编码的 TXT） */
    private static string DropImportJs(byte[] bytes, string fileName)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("(function(){var bytes=[");
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(bytes[i]);
        }
        sb.Append("];var dt=new DataTransfer();dt.items.add(new File([new Uint8Array(bytes)],");
        sb.Append(JsString(fileName));
        sb.Append(",{type:'text/plain'}));var inp=document.getElementById('fileInput');inp.files=dt.files;inp.dispatchEvent(new Event('change',{bubbles:true}));return 'ok';})()");
        return sb.ToString();
    }

    /* 读取书架：书籍数量、是否出现预期关键词、是否出现替换字符、每本书的编码标记 */
    private static string ShelfProbe(string keyword, string encTag)
    {
        return "'books=' + document.querySelectorAll('.q-book').length"
            + " + '|hit=' + (document.body.textContent.indexOf(" + JsString(keyword) + ") >= 0)"
            + " + '|repl=' + (document.body.textContent.indexOf('\\uFFFD') >= 0)"
            + " + '|enc=' + (document.body.textContent.indexOf(" + JsString(encTag) + ") >= 0)"
            + " + '|meta=' + [].map.call(document.querySelectorAll('.q-book-meta'),function(e){return e.textContent.replace(/\\s+/g,' ').trim()}).join(' // ').slice(0,240)";
    }

    private static string JsString(string s)
    {
        if (s == null) return "''";
        StringBuilder sb = new StringBuilder("'");
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\') sb.Append("\\\\");
            else if (c == '\'') sb.Append("\\'");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '<') sb.Append("\\u003c");
            else sb.Append(c);
        }
        sb.Append("'");
        return sb.ToString();
    }

    private static string Esc(string s)
    {
        if (s == null) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' || c == '\\') sb.Append('\\').Append(c);
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
