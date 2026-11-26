$ErrorActionPreference = 'Continue'
'=== CODEX RUNNING ==='
Get-CimInstance Win32_Process -Filter "Name='codex.exe'" | Select-Object ProcessId,ParentProcessId,CreationDate,ExecutablePath,CommandLine | Format-List
'=== GLOBAL NPM EXECUTABLES ==='
Get-Command codex -All | Select-Object Name,Source,CommandType
'=== INSTALL PATH ==='
$prefix = Join-Path $env:APPDATA 'npm'
$native = Join-Path $prefix 'node_modules\@openai\codex\node_modules\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe'
$shim = Join-Path $prefix 'node_modules\@openai\codex\bin\codex.js'
foreach ($p in @($native,$shim,(Join-Path $prefix 'codex.cmd'))) {
  $f=Get-Item -LiteralPath $p -ErrorAction SilentlyContinue
  if ($f) { "$($f.FullName) - $($f.Length) bytes - $($f.LastWriteTime)" } else { "MISSING $p" }
}
'=== OTHER EXECUTABLES LOCKED? ==='
Get-ChildItem -Path (Join-Path $prefix 'node_modules\@openai') -Directory -ErrorAction SilentlyContinue | Select-Object Name,LastWriteTime
