# make_src_zip.ps1 -- build the source zip (source + docs only).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\make_src_zip.ps1 [-OutDir <dir>]
# Excludes everything generated at build/run time: build\, dist\, exe\lib\, selftest-output\,
# tests\encoding-samples\, *.log, obj/bin.
param([string]$OutDir = "")

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$name = 'Reader001'
$version = '1.0.0'
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Split-Path -Parent $root }
$zip = Join-Path $OutDir ('{0}-src-v{1}.zip' -f $name, $version)

$files = @(
  '.gitattributes', '.gitignore', 'LICENSE', 'README.md', 'build.ps1', 'index.html',
  'css/base.css', 'css/reader.css', 'css/shelf.css',
  'js/app.js', 'js/boot.js', 'js/parser.js', 'js/reader.js', 'js/storage.js',
  'exe/app.ico', 'exe/app.manifest', 'exe/AssemblyInfo.cs', 'exe/Desktop.cs', 'exe/Program.cs',
  'setup/Setup.cs', 'setup/Uninstall.cs',
  'tests/encoding-samples.py', 'tests/encoding-scores.mjs', 'tests/encoding.test.mjs',
  'tests/integrity.test.mjs', 'tests/parser.test.mjs',
  'tools/make_icon.py', 'tools/make_src_zip.ps1'
)

$stage = Join-Path $root ('.zipstage-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$dest = Join-Path $stage $name
New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($rel in $files) {
  $src = Join-Path $root ($rel -replace '/', '\')
  if (-not (Test-Path -LiteralPath $src)) { throw ('missing source file: ' + $rel) }
  $dstFile = Join-Path $dest ($rel -replace '/', '\')
  $dstDir = Split-Path -Parent $dstFile
  if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
  Copy-Item -LiteralPath $src -Destination $dstFile -Force
}
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path $dest -DestinationPath $zip -CompressionLevel Optimal

$count = (Get-ChildItem -LiteralPath $dest -Recurse -File | Measure-Object).Count
$len = (Get-Item -LiteralPath $zip).Length
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
Remove-Item -LiteralPath $stage -Recurse -Force

'source zip : ' + $zip
'file count : ' + $count
'bytes      : ' + $len
'sha256     : ' + $hash
