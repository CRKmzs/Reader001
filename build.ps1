<#
  Build Reader001.exe  (desktop edition: WinForms + WebView2)
  ---------------------------------------------------------------------
  Uses only what already ships with Windows / the installed WebView2
  runtime - no internet, no NuGet, no .NET SDK:
    * C# compiler : %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    * WebView2 SDK: Microsoft.Web.WebView2.Core.dll / .WinForms.dll taken
                    from a locally installed application (auto-detected)
    * WebView2Loader.dll (x64) : also taken from a local application
  Everything (web assets, icon, WebView2 assemblies) is embedded into the
  exe as resources.

  Output policy - the shipped product is the INSTALLER only:
    dist\Reader001Setup.exe   the only deliverable (embeds app exe +
                              uninstaller + WebView2 DLLs + web assets)
    build\                    intermediate app exe / uninstaller that are used
                              as installer payload and are NOT shipped

  Usage:
    powershell -ExecutionPolicy Bypass -File build.ps1
    powershell -ExecutionPolicy Bypass -File build.ps1 -Console
    powershell -ExecutionPolicy Bypass -File build.ps1 -WebView2Dir "C:\path\to\sdk"
#>
[CmdletBinding()]
param(
  [string]$OutDir = 'dist',
  [string]$StageDir = 'build',
  [switch]$Console,
  [string]$WebView2Dir = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Info($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow }

# ---------- 1. locate the C# compiler ----------
$cscCandidates = @(
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'),
  (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe')
)
$csc = $null
foreach ($c in $cscCandidates) { if (Test-Path $c) { $csc = $c; break } }
if (-not $csc) { throw 'csc.exe not found - .NET Framework 4.x is required (ships with Windows).' }
Info "compiler: $csc"

# ---------- 2. app icon ----------
$ico = Join-Path $root 'exe\app.ico'
if (Test-Path $ico) {
  Warn "keeping existing icon: exe\app.ico"
} else {
  $pyCandidates = @('E:\PF\python\python.exe', 'python.exe', 'python', 'py')
  foreach ($py in $pyCandidates) {
    $exe = $null
    if ($py -match '[\\/]') { if (Test-Path $py) { $exe = $py } }
    else { $cmd = Get-Command $py -ErrorAction SilentlyContinue; if ($cmd) { $exe = $cmd.Source } }
    if (-not $exe) { continue }
    try {
      & $exe (Join-Path $root 'tools\make_icon.py') $ico | Out-Null
      if (Test-Path $ico) { break }
    } catch { Warn "icon generation failed with $py : $($_.Exception.Message)" }
  }
  if (-not (Test-Path $ico)) { Warn 'no usable Python - skipping the icon (the exe gets the default icon)' }
}

# ---------- 3. WebView2 SDK (core + winforms) ----------
function Test-X64([string]$path) {
  try {
    $fs = [System.IO.File]::OpenRead($path)
    try {
      $br = New-Object System.IO.BinaryReader($fs)
      $fs.Position = 0x3C
      $peOff = $br.ReadInt32()
      $fs.Position = $peOff + 4
      $machine = $br.ReadUInt16()
      return ($machine -eq 0x8664)
    } finally { $fs.Close() }
  } catch { return $false }
}

function Find-SdkDir() {
  $cands = @()
  if ($WebView2Dir -ne '') { $cands += $WebView2Dir }
  $roots = @(
    'C:\Program Files\Microsoft OfficePLUS',
    'C:\Program Files\Microsoft Office\root\Office16\WritingAssistant',
    'C:\Program Files (x86)\Kaspersky Lab'
  )
  foreach ($r in $roots) {
    if (-not (Test-Path $r)) { continue }
    foreach ($d in (Get-ChildItem $r -Recurse -Depth 4 -Directory -ErrorAction SilentlyContinue)) {
      $cands += $d.FullName
    }
  }
  foreach ($d in $cands) {
    if ((Test-Path (Join-Path $d 'Microsoft.Web.WebView2.Core.dll')) -and (Test-Path (Join-Path $d 'Microsoft.Web.WebView2.WinForms.dll'))) { return $d }
  }
  return $null
}

function Find-Loader() {
  $exact = @(
    'C:\Program Files\Microsoft Office\root\Office16\WebView2Loader.dll',
    'C:\Program Files\Common Files\Adobe\Microsoft\EdgeWebView\WebView2Loader.dll'
  )
  foreach ($p in $exact) { if ((Test-Path $p) -and (Test-X64 $p)) { return $p } }
  $roots = @(
    'C:\Program Files\Microsoft OneDrive',
    'C:\Program Files (x86)\Lenovo\LegionZone',
    'C:\Users\' + $env:USERNAME + '\AppData\Roaming\ACLOS'
  )
  foreach ($r in $roots) {
    if (-not (Test-Path $r)) { continue }
    $hits = Get-ChildItem $r -Recurse -Depth 5 -Filter 'WebView2Loader.dll' -ErrorAction SilentlyContinue
    foreach ($h in $hits) { if (Test-X64 $h.FullName) { return $h.FullName } }
  }
  return $null
}

$libDir = Join-Path $root 'exe\lib'
New-Item -ItemType Directory -Force -Path $libDir | Out-Null

$sdkDir = Find-SdkDir
if (-not $sdkDir) { throw 'Microsoft.Web.WebView2.Core.dll / .WinForms.dll not found. Pass -WebView2Dir <folder>.' }
Info "webview2 sdk : $sdkDir"
Copy-Item (Join-Path $sdkDir 'Microsoft.Web.WebView2.Core.dll')     (Join-Path $libDir 'Microsoft.Web.WebView2.Core.dll')     -Force
Copy-Item (Join-Path $sdkDir 'Microsoft.Web.WebView2.WinForms.dll') (Join-Path $libDir 'Microsoft.Web.WebView2.WinForms.dll') -Force

$loader = Find-Loader
if (-not $loader) { throw 'x64 WebView2Loader.dll not found on this machine.' }
Info "webview2 load: $loader"
Copy-Item $loader (Join-Path $libDir 'WebView2Loader.dll') -Force

# ---------- 4. resource list (file, logical name) ----------
# NOTE: all paths handed to csc are relative to this folder, because csc
#       cannot cope with spaces inside a single argument.
$resources = @(
  @('index.html',    'QDR.index.html'),
  @('css\base.css',  'QDR.css.base.css'),
  @('css\shelf.css', 'QDR.css.shelf.css'),
  @('css\reader.css','QDR.css.reader.css'),
  @('js\parser.js',  'QDR.js.parser.js'),
  @('js\storage.js', 'QDR.js.storage.js'),
  @('js\app.js',     'QDR.js.app.js'),
  @('js\reader.js',  'QDR.js.reader.js'),
  @('js\boot.js',    'QDR.js.boot.js'),
  @('exe\lib\Microsoft.Web.WebView2.Core.dll',     'QDR.lib.core.dll'),
  @('exe\lib\Microsoft.Web.WebView2.WinForms.dll', 'QDR.lib.winforms.dll'),
  @('exe\lib\WebView2Loader.dll',                  'QDR.lib.loader.dll')
)
foreach ($r in $resources) {
  if (-not (Test-Path (Join-Path $root $r[0]))) { throw "missing file: $($r[0])" }
}

# ---------- 5. compile ----------
New-Item -ItemType Directory -Force -Path (Join-Path $root $OutDir) | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $root $StageDir) | Out-Null
$exeOut = Join-Path (Join-Path $root $StageDir) 'Reader001.exe'
$target = 'winexe'
if ($Console) { $target = 'exe' }

$cscArgs = @(
  '/nologo',
  "/target:$target",
  '/platform:x64',
  '/optimize+',
  '/warn:4',
  '/codepage:65001',
  "/out:$StageDir\Reader001.exe",
  'exe\Program.cs',
  'exe\Desktop.cs',
  'exe\AssemblyInfo.cs',
  '/r:System.dll',
  '/r:System.Core.dll',
  '/r:System.Drawing.dll',
  '/r:System.Windows.Forms.dll',
  '/r:exe\lib\Microsoft.Web.WebView2.Core.dll',
  '/r:exe\lib\Microsoft.Web.WebView2.WinForms.dll',
  '/win32manifest:exe\app.manifest'
)
if (Test-Path $ico) { $cscArgs += '/win32icon:exe\app.ico' }
foreach ($r in $resources) { $cscArgs += "/resource:$($r[0]),$($r[1])" }
if (Test-Path $ico) { $cscArgs += '/resource:exe\app.ico,QDR.app.ico' }

Info "compiling ($target) ..."
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "csc failed (exit $LASTEXITCODE)" }

# ---------- 6. installer (optional: needs setup\Setup.cs + setup\Uninstall.cs) ----------
# The installer carries the app exe, the uninstaller and every web asset as
# embedded resources (PAY_*), so dist\Reader001Setup.exe is a single file.
$setupSrc  = Join-Path $root 'setup\Setup.cs'
$uninstSrc = Join-Path $root 'setup\Uninstall.cs'
if ((Test-Path $setupSrc) -and (Test-Path $uninstSrc)) {
  $common = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll')

  $uArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4','/codepage:65001',
             "/out:$StageDir\Uninstall.exe", 'setup\Uninstall.cs') + $common
  if (Test-Path $ico) { $uArgs += '/win32icon:exe\app.ico' }
  if (Test-Path (Join-Path $root 'exe\app.manifest')) { $uArgs += '/win32manifest:exe\app.manifest' }
  Info 'compiling uninstaller ...'
  & $csc $uArgs
  if ($LASTEXITCODE -ne 0) { throw "csc failed for Uninstall.exe (exit $LASTEXITCODE)" }

  $sArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4','/codepage:65001',
             "/out:$OutDir\Reader001Setup.exe", 'setup\Setup.cs') + $common
  if (Test-Path $ico) { $sArgs += '/win32icon:exe\app.ico'; $sArgs += '/resource:exe\app.ico,QDR.app.ico' }
  if (Test-Path (Join-Path $root 'exe\app.manifest')) { $sArgs += '/win32manifest:exe\app.manifest' }

  $payload = @(
    @("$StageDir\Reader001.exe", 'PAY_app_exe'),
    @("$StageDir\Uninstall.exe",  'PAY_uninst_exe'),
    @('exe\lib\WebView2Loader.dll',                  'PAY_loader.dll'),
    @('exe\lib\Microsoft.Web.WebView2.Core.dll',     'PAY_core.dll'),
    @('exe\lib\Microsoft.Web.WebView2.WinForms.dll', 'PAY_winforms.dll'),
    @('index.html',   'PAY_index.html'),
    @('css\base.css', 'PAY_css_base.css'),
    @('css\shelf.css','PAY_css_shelf.css'),
    @('css\reader.css','PAY_css_reader.css'),
    @('js\parser.js', 'PAY_js_parser.js'),
    @('js\storage.js','PAY_js_storage.js'),
    @('js\app.js',    'PAY_js_app.js'),
    @('js\reader.js', 'PAY_js_reader.js'),
    @('js\boot.js',   'PAY_js_boot.js')
  )
  foreach ($r in $payload) {
    if (-not (Test-Path (Join-Path $root $r[0]))) { throw "missing payload file: $($r[0])" }
    $sArgs += "/resource:$($r[0]),$($r[1])"
  }
  Info 'compiling installer ...'
  & $csc $sArgs
  if ($LASTEXITCODE -ne 0) { throw "csc failed for Reader001Setup.exe (exit $LASTEXITCODE)" }
  Info ("setup: {0} ({1:N0} bytes)" -f (Join-Path (Join-Path $root $OutDir) 'Reader001Setup.exe'), (Get-Item (Join-Path (Join-Path $root $OutDir) 'Reader001Setup.exe')).Length)

  # install-only policy: never leave a standalone exe in the output folder
  # (they are payload of the installer and live in the stage folder instead)
  foreach ($legacy in @('Reader001.exe', 'Uninstall.exe')) {
    $lp = Join-Path (Join-Path $root $OutDir) $legacy
    if (Test-Path -LiteralPath $lp) { Remove-Item -LiteralPath $lp -Force; Warn "removed legacy standalone $OutDir\$legacy (install-only build)" }
  }
} else {
  Warn 'setup\Setup.cs / setup\Uninstall.cs not found - skipping the installer'
}

# ---------- 7. result ----------
$setupOut = Join-Path (Join-Path $root $OutDir) 'Reader001Setup.exe'
Write-Host ''
Write-Host 'How to run:' -ForegroundColor Green
if (Test-Path -LiteralPath $setupOut) {
  Write-Host ('  deliverable : ' + $setupOut + '  (' + (Get-Item $setupOut).Length.ToString('N0') + ' bytes, installer only)')
  Write-Host ('  install     : double-click it, or run  ' + [char]34 + $setupOut + [char]34 + ' /S /D=<folder> /NORUN')
} else {
  Warn 'no installer was produced (setup\Setup.cs missing?)'
}
Write-Host ('  dev exe     : ' + $exeOut + '  (' + (Get-Item $exeOut).Length.ToString('N0') + ' bytes, intermediate - not shipped)')
Write-Host ('  self test   : ' + [char]34 + $exeOut + [char]34 + ' --selftest --data .\selftest-output\data --out .\selftest-output')
