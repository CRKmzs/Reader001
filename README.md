# Reader001 · 本地 TXT 阅读器（Windows 桌面版）

> ## ⚠️ 全部代码由 AI 生成
>
> 本项目的**每一行代码与文档都是 AI 生成的**：C# 外壳与本地 HTTP 服务、安装程序与卸载程序、HTML/CSS/JS 前端、构建脚本、测试脚本，以及这份 README。人类只负责提出需求、执行构建与验收。请在使用前自行评估代码质量与安全性，详见[代码来源](#代码来源)。

一个完全离线的 Windows 桌面 TXT 阅读器：外壳用 C#（WinForms + WebView2），界面是手写的原生前端
（无框架、无打包步骤、无 npm 依赖）。只做两件事——**书架**和**阅读**。

- 一个安装包即全部：运行 `Reader001Setup.exe` 完成安装（默认请求管理员权限，会弹一次 UAC；可用 `/NOELEVATE` 关闭），自动创建桌面快捷方式（不再创建开始菜单项）
- 自动识别 UTF-8 / GBK / GB18030 / Big5 / UTF-16，导入不用转码、不乱码
- 章节自动切分、目录跳转、阅读进度记忆、7 套主题 / 5 种字体 / 字号行距可调
- 阅读页两侧的上一章 / 下一章箭头**固定在窗口两侧**，不随正文滚动
- 安装与运行**只在安装文件夹内新增文件**（`data\` 与 `书架\` 两个子文件夹），桌面除 Reader001 快捷方式外不出现任何东西，不再写 `%LOCALAPPDATA%`、`%TEMP%`、文档目录等其它位置
- 「书架」文件夹：安装目录下的 `书架\` 用来直接放 .txt，程序启动时自动导入，也可随时点工具栏的「⟳ 书架」重新扫描
- 只监听 127.0.0.1，不联网、不上传
- **全部代码由 AI 生成**（见[代码来源](#代码来源)），前端零框架、零第三方库

## 功能

| 模块 | 能力 |
| --- | --- |
| 书架 | 导入或拖入多个 .txt；搜索；排序（最近阅读 / 名称 / 导入时间 / 阅读进度）；筛选（全部 / 在读 / 已读完）；重命名与改作者；删除；进度与字数一览；把 .txt 放进安装目录的 `书架\` 后启动时自动导入（或用「⟳ 书架」重新扫描） |
| 阅读 | 章节自动识别；目录抽屉（可按章节名或序号过滤）；上一章 / 下一章；进度条与百分比；滚动或点击翻页；全屏 |
| 版式 | 7 套主题（默认 / 米黄 / 护眼绿 / 粉色 / 灰色 / 夜间 / 纯黑）、5 种字体（宋体 / 雅黑 / 黑体 / 楷体 / 仿宋）、字号 14-40、行距、段间距、正文宽度 |
| 编码 | UTF-8（含 BOM）、UTF-16 LE/BE（含无 BOM）、GB18030/GBK、Big5 自动识别，也可在界面上手动指定 |
| 存储 | 书籍正文与元数据存 IndexedDB，阅读设置存 localStorage，WebView2 用户数据也在同一目录，全部位于安装目录下的 `data\`，安装目录之外零写入 |

## 快速开始

### 方式一：下载现成程序

到 [Releases](../../releases) 下载（也可以自己构建，见下）。

| 文件 | 说明 |
| --- | --- |
| `Reader001Setup.exe` | 唯一的发布产物：双击安装（启动后先请求管理员权限，见下），自动建立桌面快捷方式，安装位置是当前用户目录、不需要密钥。主程序与卸载器都在安装包里（安装后会释放到安装目录） |

运行环境：Windows x64 + WebView2 运行时（Windows 11 与较新的 Windows 10 自带；缺失时程序会弹窗提示，
并允许改用系统默认浏览器打开）。

静默安装（脚本 / 批量部署）：

```
Reader001Setup.exe /S /D=D:\Apps\Reader001 /LOG=install.log
```

| 开关 | 作用 |
| --- | --- |
| `/S` | 静默安装，不显示界面 |
| `/D=目录` | 指定安装目录（默认 `%LOCALAPPDATA%\Programs\Reader001`） |
| `/NODESKTOP` | 不创建桌面快捷方式（`/NOSTART` 为兼容参数，本版起已不创建开始菜单项） |
| `/NORUN` | 安装完不自动启动程序 |
| `/LOG=文件` | 把安装过程写成 UTF-8 日志；不指定时**等安装目录确定后**才写 `<安装目录>\Reader001Setup.log`（在此之前安装程序不创建任何文件或目录） |
| `/STRICT` | 目标目录不可写时直接失败，不自动改选其他目录 |
| `/NOELEVATE` | 不请求管理员权限（默认启动后立即请求提权） |
| `/ELEVATED` | 内部标记：本次已由安装程序自己提权重启，无需手动指定 |
| `/HELP` | 显示帮助 |

退出码：0 成功、1 失败、2 用户取消。

**默认以管理员身份运行。** 安装程序启动后立即用 `runas` 重新启动自己并请求提权（会弹一次 UAC），同意后整个安装过程都在管理员权限下进行。
若选择「否」或系统不允许提权，安装会以当前权限继续，并按下面的规则自动改选目录；用 `/NOELEVATE` 可以完全跳过提权请求。
静默安装（`/S`）在请求提权后会等待管理员实例结束，并把它（而不是自己的）退出码返回给调用者，父进程与子进程写同一份日志。

**安装目录不可写时会自动改选。** 安装程序启动时先探测目标目录能否写入；不能写就按
`%LOCALAPPDATA%\Programs\Reader001` → `%LOCALAPPDATA%\Reader001` → `%USERPROFILE%\Reader001`
的顺序找第一个可写目录，并把最终安装位置与每一步探测结果写进日志。候选目录只有这三个，
不会再往桌面、安装包所在目录或 `%TEMP%` 里安装。
所有候选都不可写时（例如安全软件限制了未签名程序写盘），界面会显示完整诊断信息（可一键复制到剪贴板），
并询问是否以管理员身份重试一次。

卸载：运行安装目录里的 `Uninstall.exe`（默认请求管理员权限，可用 `/NOELEVATE` 关闭；安装时登记过卸载项的话，也可以从设置 → 应用 → 已安装的应用 → Reader001 卸载）；
静默卸载 `Uninstall.exe /S`，加 `/PURGE` 可连 `书架\` 文件夹一起删除（默认保留里面的 txt）。
卸载会逐项校验：快捷方式 / 开始菜单 / 卸载项 / `data\` 任一没删干净都会写进日志并提示残留路径，不会假装成功。

### 方式二：从源码构建

```
git clone <本仓库地址>
cd Reader001
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

用系统自带的 `csc.exe`（.NET Framework 4.x）编译：不需要 .NET SDK、不需要 NuGet、不需要联网。

前提条件：

- Windows x64，且装了 WebView2 运行时；
- 本机能找到 WebView2 SDK（`Microsoft.Web.WebView2.Core.dll` / `.WinForms.dll`）与 x64 的 `WebView2Loader.dll`。
  构建脚本会先在常见位置查找，找不到就用 `-WebView2Dir <目录>` 指定；找到后复制到 `exe\lib\`（该目录不进仓库）。

产物：

| 产物 | 说明 |
| --- | --- |
| `dist\Reader001Setup.exe` | **唯一的交付物**：安装包（把主程序、卸载器、3 个 DLL 与全部前端文件作为 `PAY_*` 资源嵌进去，单文件分发） |
| `build\Reader001.exe` | 中间产物：主程序（GUI 子系统，双击无控制台窗口），只作为安装包载荷 |
| `build\Uninstall.exe` | 中间产物：卸载器，只作为安装包载荷 |

`build\` 里的文件不会单独发布；`build.ps1` 构建安装包成功后会自动删除旧版 `dist\` 下遗留的独立 exe。
加 `-Console` 可把主程序编译成带控制台日志的调试版本（输出仍在 `build\`）。

## 使用

- 打开 Reader001，出现标题为「Reader001」的桌面窗口（GUI 子系统，不会弹命令行黑窗）。
- 书架页点「+ 导入 TXT」选择文件，或直接把 .txt 拖进窗口；支持一次选多个文件。
  导入后可在书籍卡片上重命名、改作者、看目录、删除；导入的 txt 会同时备份一份到安装目录的 `书架\`。
- 也可以直接把 .txt 拷进安装目录的 `书架\`：安装版启动时会自动扫描导入，工具栏的「⟳ 书架」可随时重新扫描。
- 点书籍卡片进入阅读页：顶部显示书名与当前章节，底部是 目录 / 上一章 / 进度 / 下一章 / 设置。
- **左右两侧的上一章 / 下一章箭头固定在窗口两侧**，滚轮翻到任何位置都能直接点；
  箭头平时半透明，鼠标移入阅读页变清晰，打开目录或设置时自动让位隐藏。
- 阅读设置立即生效并自动保存；阅读进度自动记住，下次从上次位置继续。
- 窗口关闭即退出，不在后台留下进程（本地服务只监听 127.0.0.1，随窗口一起关闭）。

## 编码支持（乱码修复）

导入的 TXT 会自动识别编码并正确解码，不用手动转码：

| 编码 | 识别方式 |
| --- | --- |
| UTF-8（含 BOM） | 严格解码 + 文本可信度打分 |
| UTF-16 LE / BE（含无 BOM） | 零字节分布 + 打分 |
| GB18030 / GBK | 候选打分（简体优先） |
| Big5（繁体） | 候选打分，比 GB18030 明显更高才判定为 Big5 |

判定方式是把每个候选编码各解码一段样本，按「汉字 / 常用字 / 中文标点 / ASCII 的比例减去替换字符与控制字符的惩罚」
打分取最高者，而不是「UTF-8 严格探测失败就按 GBK 处理」——旧做法只要文件里有一个损坏字节就会把整篇 UTF-8
当成 GBK，从而整篇乱码；现在单个坏字节不再影响判定。

- 书架工具区的「编码」下拉可手动指定（自动识别 / UTF-8 / GB18030 / Big5 / UTF-16LE / UTF-16BE），
  指定后对后续导入生效并记住设置。
- 自动识别把握不大（分数 < 0.75）时会给出醒目提示，提示改选正确编码后重新导入。
- 书籍卡片上显示实际使用的编码标记（如 GBK/GB18030、Big5），便于确认。

## 安全说明

程序把界面放在本机回环地址上由 WebView2 显示，为此做了以下加固：

- **只监听 127.0.0.1**（`IPAddress.Loopback`），默认在 47113-47120 中挑一个可用端口，不对外网开放。
- **Host 头校验**：Host 去掉端口后必须等于 `127.0.0.1` / `localhost` / `[::1]`，否则 403（抵御 DNS rebinding）。
- **Sec-Fetch-Site 校验**：只接受 `none` 与 `same-origin`，跨站请求一律 403。
- **URI 长度上限 512 字节**，超长 414；非 GET/HEAD 返回 405。
- **固定白名单路由**：只服务 index.html、css\*.css、js\*.js、favicon 与内嵌资源，其它路径 404；
  路径先规范化并拒绝 `..`，越界请求一律 404。
- **安全响应头**：HTML 响应带 CSP
  （`default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; object-src 'none'`）、
  `X-Frame-Options: DENY`、`Referrer-Policy: no-referrer`、`Cross-Origin-Resource-Policy: same-origin`、
  `Cross-Origin-Opener-Policy: same-origin`、`X-Content-Type-Options: nosniff`、`Cache-Control: no-store`。
- **前端无注入点**：书名、作者、章节名、正文段落全部经 HTML 转义后渲染，且已移除全部内联 script/style，
  才能启用上面的严格 CSP。
- **书架文件夹读写有边界**：`/__qdr/shelf*` 只操作 `书架\` 里的直接子文件，文件名先剥掉路径分隔符与 `..`（`SafeShelfName`），读写上限 64 MB。
- **不向安装目录外写文件**：数据目录固定为 `<安装目录>\data`，日志写在 `data\` 与 exe 同目录；原先回退写 `%LOCALAPPDATA%` 的分支已删除。
- **安装程序同样如此**：安装日志只有在安装目录确定之后才写进安装目录（`InstallerLog.UseFile`），选目录之前日志只留在内存与控制台，不创建任何文件/目录；只有显式传 `/LOG=<文件>` 时才写到你指定的位置。旧版在选目录前就按默认目录建日志，会留下一个空的默认目录，已修复。
- **不联网**：程序不发起任何外部网络请求，不上传任何内容。

## 数据与命令行

书籍正文与元数据存在 IndexedDB，阅读设置存在 localStorage，WebView2 的用户数据也在同一目录，均位于：

```
<安装目录>\data
```

日志 `Reader001.log` 写在 `data\` 里，同时也写一份到 exe 同目录（都在安装文件夹内）。
卸载时 `data\` 随主程序一起删除；`书架\` 默认保留（`/PURGE` 才会删）。

命令行参数（主程序 `Reader001.exe`；安装后在安装目录里，例如 `%LOCALAPPDATA%\Programs\Reader001\Reader001.exe`）：

| 参数 | 作用 |
| --- | --- |
| `--data DIR` | 覆盖数据目录（默认 `<安装目录>\data`） |
| `--port N` / `-p N` | 指定本地服务端口（默认在 47113-47120 中挑一个可用的） |
| `--selftest` | 跑端到端自检、截图并生成报告（开发用） |
| `--diag` | 输出前端状态诊断报告 diag-report.txt（开发用） |
| `--serve` / `--no-browser` | 只启动本地服务、不建窗口 |
| `--help` | 帮助 |

> 注意：`--data` / `--out` 必须写成两个参数（`--data DIR`），写成 `--data=DIR` 会被忽略。

## 目录结构

```
Reader001\
  index.html            书架页 + 阅读页的全部结构
  css\base.css          变量、按钮、弹窗、提示
  css\shelf.css         顶栏与书架网格
  css\reader.css        阅读页（7 主题 / 5 字体 / 版式变量 / 固定翻章箭头）
  js\parser.js          编码探测与解码、章节解析、字数统计
  js\storage.js         IndexedDB + localStorage 封装（含内存兜底）
  js\app.js             书架：导入、排序、重命名、删除、路由
  js\reader.js          阅读页：渲染、目录、进度、设置、全屏
  js\boot.js            启动入口（替代内联 script，便于启用严格 CSP）
  exe\Program.cs        本地 HTTP 服务（白名单路由 + 安全头）、内嵌资源、启动参数
  exe\Desktop.cs        WinForms 窗口 + WebView2 宿主 + 自检 / 诊断
  exe\AssemblyInfo.cs   程序集信息（产品名 Reader001）
  exe\app.manifest      清单（DPI 感知、Windows 兼容性）
  exe\app.ico           应用图标（翻开的书 + 字母 R，同时作为窗口图标、安装包图标与 /favicon.ico）
  exe\lib\              构建时从本机 WebView2 SDK 复制（不进仓库）
  setup\Setup.cs        安装程序（用户级安装、快捷方式、卸载项注册）
  setup\Uninstall.cs    卸载程序（安全校验、延迟自删除）
  tools\make_icon.py    用标准库生成 app.ico（蓝色圆角方块 + 白色摊开的书 + 书页上的深蓝 R；
                        --png preview.png 可导出 PNG 预览，只有 32 像素及以上才叠加 R）
  tools\make_src_zip.ps1  打源码包（只收源码，排除 build/ dist/ 生成物与运行期文件）
  tests\                解析器 / 编码 / 完整性测试
  build.ps1             构建脚本（csc 自编译，零依赖）
  dist\                 唯一交付物 Reader001Setup.exe（不进仓库）
  build\                安装包载荷：主程序与卸载器（不进仓库）
  selftest-output\      自检截图与报告（不进仓库）
```

安装后的目录布局（默认 `%LOCALAPPDATA%\Programs\Reader001`）：

```
Reader001.exe                        主程序
Uninstall.exe                        卸载器
Microsoft.Web.WebView2.Core.dll      \
Microsoft.Web.WebView2.WinForms.dll   } WebView2 运行库
WebView2Loader.dll                   /
app\index.html                       前端为松散文件，改完刷新即可生效
app\css\*.css  app\js\*.js
书架\                                直接放 .txt 的文件夹（安装时创建，卸载默认保留）
data\                                运行期数据：IndexedDB / WebView2 配置 / 日志（首次运行自动创建）
Reader001Setup.log                   安装日志（安装目录确定后才创建；卸载时删除）
.reader001-install                   安装标记（卸载器凭它确认目录归属）
```

## 测试与自检

```
python tests\encoding-samples.py     # 先生成 GBK / Big5 / UTF-16 等真实字节样本（生成物，不进仓库）
node tests\parser.test.mjs           # 章节解析、字数、解码基础（14 项）
node tests\encoding.test.mjs         # 13 个真实字节样本的编码识别（13 项）
node tests\integrity.test.mjs        # HTML / CSS / JS 交叉检查
build\Reader001.exe --selftest --data .\selftest-output\data --out .\selftest-output
```

`--selftest` 依次验证：首次启动的空书架与空状态提示 → 导入测试书 → **txt 被复制进安装目录的 `书架\`** →
把测试 txt 放进 `书架\` 后点「⟳ 书架」能扫描导入（记录 `/__qdr/shelf` 与 `/__qdr/shelf/file` 的返回码）→
清理扫描导入的书 → 阅读页正文段落 → 夜间主题生效 →
米黄 + 楷体 + 26px 生效且重新载入后仍持久化 → 目录抽屉条目 → 点击下一章翻章 →
**滚动与滚到底时两侧翻章箭头位置固定（position: fixed 且始终在视口内）** → 再次导入与重新载入后的持久化 →
GBK 与 Big5 文件的解码（无 U+FFFD 替换字符、书架显示对应编码标记）→ 收尾删掉测试 txt；
并把 11 张截图（`01-shelf-empty` … `10-reader-scrolled`）与 `selftest-report.json` 写到 `--out` 目录。
最近一次结果：`ok=true`，23 步全部通过（箭头断言 `pos=fixed|top0=252|top1=252|sameTop=true|nextLeft=794|prevLeft=14`，
书架断言 `books=0 → 1 → 2（扫描）→ 1（清理）`，收尾 `书架\` 里剩余 txt = 0）。

安装 / 卸载也在本机实测过（`/S /D=<测试目录> /NORUN /NOELEVATE` → 跑安装版自检 → `/S /D=<测试目录> /NOELEVATE`）：
安装只往测试目录里写文件；安装版自检前后对桌面 / 开始菜单 / `%LOCALAPPDATA%` / 用户目录 / `%TEMP%` 做快照对比是
**(no change)**；卸载后测试目录只剩 `书架\`，`%LOCALAPPDATA%\Reader001`、`%LOCALAPPDATA%\Programs\Reader001` 与开始菜单目录都不存在。

服务端安全加固也用原始 TCP 客户端实测过：正常同源请求 200；恶意 Host / 缺 Host / `Sec-Fetch-Site: cross-site` → 403；
600 字节 URI → 414；路径越界 → 404；POST → 405；响应头含 CSP 与全部安全头。

## 仓库约定

仓库只提交源码与文档，以下内容由脚本生成，已在 `.gitignore` 中忽略：

| 路径 | 来源 |
| --- | --- |
| `dist\` | `build.ps1` 的唯一交付物 `Reader001Setup.exe`（发布时作为 Release 附件上传） |
| `build\` | 安装包载荷（主程序与卸载器），中间产物，不发布 |
| `exe\lib\` | 构建时从本机 WebView2 SDK 复制 |
| `selftest-output\` | `--selftest` 的截图与报告 |
| `tests\encoding-samples\` | `python tests\encoding-samples.py` 生成的字节样本 |
| `*.log` | 运行时日志 |

## 已知限制

- 本机任何进程都能访问 127.0.0.1 上的这个端口（已用 Host / Sec-Fetch-Site / CSP 缓解），程序不做鉴权。
- 不做代码签名：首次运行可能触发 SmartScreen 或安全软件提示，选择「仍要运行」并加信任即可。
- 未签名的程序可能被安全软件（例如卡巴斯基的「应用程序控制」）限制写盘：未提权运行时，安装包可能写不进默认安装目录。
  本机实测表明这类限制只作用于未提权进程；但启用「应用程序控制」后，即使**已提权**，安全软件仍可能拦住桌面快捷方式与
  `HKCU\...\Uninstall\Reader001` 卸载项的写入（报 `UnauthorizedAccessException`）。这类失败不会被吞掉：安装日志会写明完整异常链、失败项与处理建议
  （右键「以管理员身份运行」、在安全软件里放行、或手动把 `Reader001.exe` 发送到桌面快捷方式）。
  若用户拒绝 UAC 或使用 `/NOELEVATE`，安装程序会自动改选第一个可写目录（见上），并把每一步探测结果写入 `Reader001Setup.log`。
- 卸载同样逐项校验：快捷方式 / 开始菜单 / 卸载项任一没删干净，日志与提示都会列出残留路径（不会显示「卸载成功」了事），
  `data\` 随主程序删除，`书架\` 里的 txt 默认保留。
- 默认提权要求当前账户属于管理员组。标准用户若用其他管理员的凭据批准 UAC，安装会以那位管理员的身份继续，快捷方式与卸载项会写进该管理员账户，
  这种情况下请改用 `/NOELEVATE`，让安装在当前用户身份下完成。
- 只支持 .txt：不做 epub/mobi 等格式解析，不做云同步。

## 代码来源

- **本仓库的全部代码均由 AI 生成**，包括但不限于：
  `exe\Program.cs`（本地 HTTP 服务、路由白名单与安全响应头）、`exe\Desktop.cs`（WinForms + WebView2 宿主、自检与诊断）、
  `setup\Setup.cs` 与 `setup\Uninstall.cs`（用户级安装、快捷方式、卸载与自删除）、
  `index.html` 与 `css\`、`js\` 下的全部前端、`build.ps1`、`tests\` 下的测试脚本、`tools\make_icon.py`，以及本 README 与其它文档。
  编写方式是「由人提出需求与验收标准 → AI 生成与修改代码 → 在本机实际构建并跑端到端自检」。
- 人工参与的部分只有：提出需求、确定验收标准、决定设计取舍、执行构建与测试、检查运行结果（例如本 README「测试与自检」一节的断言与截图都是在本机真实跑出来的）。
- 因此请注意：**本项目不提供任何正确性、安全性或适用性保证**；上文的加固与测试只能说明「在本机实测过的行为符合预期」，不代表代码已被逐行人工审计。
  若要用于生产环境或处理敏感数据，请先自行审阅代码。发现 AI 生成代码的缺陷时，欢迎提 issue 或 PR。

## 许可证

MIT，见 [LICENSE](LICENSE)。
