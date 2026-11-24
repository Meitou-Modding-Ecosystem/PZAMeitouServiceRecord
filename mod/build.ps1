# PZA's Service Record for Meitou: build the code mod and stage its install folder. Author: PZA. Licence: GPLv3 or later.
#
#   .\build.ps1            build Release, stage out\PZAServiceRecord\ (PZAServiceRecord.Meitou.dll, meitou.json)
#   .\build.ps1 -Check     also the offline check: the differential replay (random event traces through the RE_Kenshi plugin's own C++ core,
#                          built with its VS2010 compiler by tools\diff\build_diff.bat, and through this C# port; results compared byte for
#                          byte), on a copy of the .NET 11 runtime inside the Meitou release. It never starts the game.
#   .\build.ps1 -Fuzz      also run the fuzz harness (tests\fuzz); -Check runs it too (2026-11-18)
#   -Seeds <n>             how many traces (default 12, 4000 events each, the last third with hostile readings)
#   -MeitouDir <dir>       the unpacked Meitou release (default: MeitouDir in mod\local.props, git-ignored)
#   -RunOn <dir>           run the -Check on ANOTHER Meitou release's runtime and assemblies, with the mod still built against -MeitouDir
#                          (0.20.1, what ships): a 0.20.1 build on 0.21 / 0.22 is how players run it (2026-11-19: Meitou 0.21 changed
#                          UiListItem's constructor and every 0.20.1-built PZA window list threw there; a same-version check never saw it)
#
# It installs nothing. out\ is git-ignored.
param(
    [switch]$Check,
    [switch]$Fuzz,
    [int]$Seeds = 12,
    [string]$MeitouDir = "",
    [string]$RunOn = ""
)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$localProps = Join-Path $here "local.props"
if ($MeitouDir -eq "" -and (Test-Path $localProps)) { $MeitouDir = ([xml](Get-Content $localProps -Raw)).Project.PropertyGroup.MeitouDir }
if (-not $MeitouDir) { throw "Set -MeitouDir <unpacked Meitou release> or create mod\local.props (see the comment in the csproj)" }
$MeitouDir = [System.IO.Path]::GetFullPath($MeitouDir)
if (-not (Test-Path (Join-Path $MeitouDir "MeitouClient.dll"))) { throw "MeitouClient.dll not found in $MeitouDir" }

& dotnet build (Join-Path $here "PZAServiceRecord.Meitou.csproj") -c Release -nologo "-p:MeitouDir=$MeitouDir"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

# the DLL just built: the newest DLL file, not the newest folder (2026-11-21: an old net10.0 folder won by its folder date and a stale DLL was staged)
$bin = (Get-ChildItem (Join-Path $here "bin\Release") -Recurse -Filter "PZAServiceRecord.Meitou.dll" | Sort-Object LastWriteTime -Descending | Select-Object -First 1).Directory
$stage = Join-Path $here "out\PZAServiceRecord"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $bin.FullName "PZAServiceRecord.Meitou.dll") $stage -Force
Copy-Item (Join-Path $here "meitou.json") $stage -Force
# the Meitou version's own lines (lang\meitou.<code>.txt); the shared lang\<code>.txt are the RE_Kenshi plugin's, already in its folder
New-Item -ItemType Directory -Force (Join-Path $stage "lang") | Out-Null
Copy-Item (Join-Path $here "lang\meitou.*.txt") (Join-Path $stage "lang") -Force
Write-Host "staged $stage"

if ($Check) {
    # the C++ side of the differential replay
    & cmd /c (Join-Path $here "tools\diff\build_diff.bat")
    if ($LASTEXITCODE -ne 0) { throw "sr_diff build failed ($LASTEXITCODE)" }
    $diff = Join-Path $here "out\diff"
    if (Test-Path $diff) { Remove-Item -Recurse -Force $diff }
    New-Item -ItemType Directory -Force $diff | Out-Null
    $exe = Join-Path $here "tools\diff\out\sr_diff.exe"
    for ($i = 1; $i -le $Seeds; $i++) {
        $seed = 1000 + 7919 * $i
        $a = @("$seed", "4000", (Join-Path $diff ("t{0:D2}.txt" -f $i)), (Join-Path $diff ("r{0:D2}.txt" -f $i)))
        if ($i -gt [math]::Floor($Seeds * 2 / 3)) { $a += "hostile" }
        & $exe @a
        if ($LASTEXITCODE -ne 0) { throw "sr_diff failed on seed $seed" }
    }

    for ($i = 1; $i -le 10; $i++) {
        & $exe aar ("{0}" -f (5000 + 104729 * $i)) 40 (Join-Path $diff ("a{0:D2}.txt" -f $i)) (Join-Path $diff ("ar{0:D2}.txt" -f $i))
        if ($LASTEXITCODE -ne 0) { throw "sr_diff aar failed" }
    }
    for ($i = 1; $i -le 8; $i++) {
        & $exe cmd ("{0}" -f (77 + 15485863 * $i)) 400 (Join-Path $diff ("c{0:D2}.txt" -f $i)) (Join-Path $diff ("cr{0:D2}.txt" -f $i))
        if ($LASTEXITCODE -ne 0) { throw "sr_diff cmd failed" }
        & $exe rr ("{0}" -f (91 + 32452843 * $i)) 300 (Join-Path $diff ("q{0:D2}.txt" -f $i)) (Join-Path $diff ("qr{0:D2}.txt" -f $i))
        & $exe pages ("{0}" -f (13 + 49979687 * $i)) 200 (Join-Path $diff ("w{0:D2}.txt" -f $i)) (Join-Path $diff ("wr{0:D2}.txt" -f $i))
        if ($LASTEXITCODE -ne 0) { throw "sr_diff rr failed" }
    }
    & $exe short (Join-Path $diff "short.txt")
    if ($LASTEXITCODE -ne 0) { throw "sr_diff short failed" }

    $runDir = if ($RunOn -ne "") { [System.IO.Path]::GetFullPath($RunOn) } else { $MeitouDir }
    if (-not (Test-Path (Join-Path $runDir "MeitouClient.dll"))) { throw "MeitouClient.dll not found in $runDir" }
    Write-Host "check: the mod built against $MeitouDir, run on $runDir"
    # one runtime folder per Meitou release, with every DLL of it (2026-11-22: 0.22.2-nightly.20261122.2's MeitouClient loads Silk.NET.Core in
    # its module initializer; a fixed list of patterns missed it). Per release, so -Check runs on different releases never overwrite each
    # other's files (the same layout as PZA's SlowMo for Meitou 3d3da06)
    $rt = Join-Path $here ("out\checkrt\" + (Split-Path $runDir -Leaf))
    $stamp = Join-Path $rt "meitou-dir.txt"
    if (-not (Test-Path $stamp) -or (Get-Content $stamp -Raw).Trim() -ne $runDir) {
        New-Item -ItemType Directory -Force $rt | Out-Null
        foreach ($p in "*.dll", "*.so") { Copy-Item (Join-Path $runDir $p) $rt -Force -ErrorAction SilentlyContinue }
        Set-Content -Path $stamp -Value $runDir -Encoding ascii
    }
    $stagedDll = Join-Path $stage "PZAServiceRecord.Meitou.dll"
    & dotnet build (Join-Path $here "tools\check\check.csproj") -c Release -nologo "-p:MeitouDir=$runDir" "-p:ModDll=$stagedDll" -o (Join-Path $here "out\checkbin")
    if ($LASTEXITCODE -ne 0) { throw "check tool build failed ($LASTEXITCODE)" }
    Copy-Item (Join-Path $here "out\checkbin\check.dll") $rt -Force
    Copy-Item $stagedDll $rt -Force
    $cfg = '{ "runtimeOptions": { "tfm": "net11.0", "includedFrameworks": [ { "name": "Microsoft.NETCore.App", "version": "11.0.0" } ] } }'
    Set-Content -Path (Join-Path $rt "check.runtimeconfig.json") -Value $cfg -Encoding ascii
    Push-Location $rt
    try { & dotnet exec --runtimeconfig check.runtimeconfig.json check.dll $diff $stage "C:\Program Files (x86)\Steam\steamapps\common\Kenshi" } finally { Pop-Location }
    if ($LASTEXITCODE -ne 0) { throw "check FAILED ($LASTEXITCODE)" }
}

# 2026-11-18: the fuzz harness (tests\fuzz, PZA's Meitou UI fuzz kit) on the same Meitou release: -Fuzz runs it, and so does every -Check
# (a release also needs the mutation check, PZAMeitouUi\fuzz\mutate.py). It never starts the game.
if ($Fuzz -or $Check) {
    $fuzzRunner = Join-Path $here "..\..\PZAMeitouUi\fuzz\run_fuzz.ps1"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $fuzzRunner -Repo (Join-Path $here "..") -MeitouDir $MeitouDir
    if ($LASTEXITCODE -ne 0) { throw "fuzz FAILED ($LASTEXITCODE): report in tests\out\fuzz\fuzz-report.txt" }
}
