# NyangTalk build script. Uses only the C# compiler that ships with .NET Framework 4.8 (see docs/decisions.md D1).
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1            # layered debug build + tests
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -TestFilter Settings   # only checks whose name contains "Settings"
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Release   # single-file NyangTalk.exe
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Plugin    # BepInEx plugin (needs game folder)
param(
    [switch]$Release,
    [switch]$Plugin,
    [switch]$NoTests,
    [string]$TestFilter = "",
    [string]$GameDir = ""
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$fw = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$out = Join-Path $root "build"
New-Item -ItemType Directory -Force $out | Out-Null

function Get-Sources([string[]]$dirs) {
    $files = @()
    foreach ($d in $dirs) {
        $p = Join-Path $root $d
        if (Test-Path $p) { $files += Get-ChildItem -Path $p -Recurse -Filter *.cs | ForEach-Object { $_.FullName } }
    }
    return $files
}

function Invoke-Csc([string]$target, [string]$outFile, [string[]]$sources, [string[]]$refs, [string[]]$extra) {
    $rsp = Join-Path $out ([IO.Path]::GetFileNameWithoutExtension($outFile) + ".rsp")
    $lines = @("/nologo", "/target:$target", "/out:`"$outFile`"", "/debug:pdbonly", "/optimize+", "/warn:4", "/nowarn:1591", "/utf8output", "/codepage:65001")
    foreach ($r in $refs) { $lines += "/reference:`"$r`"" }
    foreach ($e in $extra) { $lines += $e }
    if ([IO.Path]::GetFileName($outFile) -in @("NyangTalk.exe", "NyangTalk.Layered.exe")) {
        $lines += "/win32manifest:`"$(Join-Path $root 'assets\app.manifest')`""
    }
    foreach ($s in $sources) { $lines += "`"$s`"" }
    [IO.File]::WriteAllLines($rsp, $lines, (New-Object Text.UTF8Encoding($false)))
    & $csc /noconfig "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "csc failed for $outFile" }
}

$fwRefs = @("$fw\mscorlib.dll", "$fw\System.dll", "$fw\System.Core.dll")
$uiRefs = @("$fw\System.Drawing.dll", "$fw\System.Windows.Forms.dll")
$platform = "/platform:x64"

function Build-Plugin {
    $gd = $GameDir
    if ($gd -eq "") { $gd = "C:\Program Files (x86)\Steam\steamapps\common\BongoCat" }
    $managed = Join-Path $gd "BongoCat_Data\Managed"
    $bep = Join-Path $gd "BepInEx\core\BepInEx.dll"
    if (-not (Test-Path $bep)) { $bep = Join-Path $root "third_party\BepInEx\payload\BepInEx\core\BepInEx.dll" }
    # The game's mscorlib is stripped (the C# 5 compiler cannot emit against it), so the plugin compiles against the
    # .NET Framework 4.x reference set; tests check every member the plugin uses exists in the game's own libraries.
    # Game and BepInEx DLLs are compile references only and are never copied into the build output.
    $prefs = @("$fw\mscorlib.dll", "$fw\System.dll", "$fw\System.Core.dll", "$managed\UnityEngine.dll", "$managed\UnityEngine.CoreModule.dll", "$managed\UnityEngine.SharedInternalsModule.dll", "$managed\UnityEngine.ImageConversionModule.dll", $bep)
    foreach ($p in $prefs) { if (-not (Test-Path $p)) { Write-Host "plugin skipped (missing compile reference: $p)"; return $false } }
    $src = Get-Sources @("src\Shared", "src\Plugin")
    Invoke-Csc "library" (Join-Path $out "NyangTalk.GameLink.dll") $src $prefs @("/platform:anycpu")
    Copy-Item (Join-Path $root "src\Shared\contract.json") (Join-Path $out "contract.json") -Force
    Write-Host "plugin built: build\NyangTalk.GameLink.dll"
    return $true
}

if ($Plugin) { if (Build-Plugin) { exit 0 } else { exit 1 } }

if ($Release) {
    $hasPlugin = Build-Plugin
    $src = Get-Sources @("src\Shared", "src\Domain", "src\Application", "src\Adapters", "src\App")
    $icon = Join-Path $root "assets\app.ico"
    $extra = @($platform, "/define:RELEASE")
    if (Test-Path $icon) { $extra += "/win32icon:`"$icon`""; $extra += "/resource:`"$icon`",app.ico" }
    # connector payload embedded in the exe (40-SPEC 5장: no download at install time)
    if ($hasPlugin) {
        $extra += "/resource:`"$(Join-Path $out 'NyangTalk.GameLink.dll')`",payload/BepInEx/plugins/NyangTalk/NyangTalk.GameLink.dll"
        $extra += "/resource:`"$(Join-Path $root 'src\Shared\contract.json')`",payload/BepInEx/plugins/NyangTalk/contract.json"
    }
    $extra += "/resource:`"$(Join-Path $root 'src\Shared\contract.json')`",contract.json"
    # official BepInEx 5.4.23.6 (third_party/SOURCES.md) installed with the connector, plus its license texts
    $bepRoot = Join-Path $root "third_party\BepInEx\payload"
    if ($hasPlugin -and (Test-Path $bepRoot)) {
        foreach ($f in Get-ChildItem $bepRoot -Recurse -File) {
            $rel = $f.FullName.Substring($bepRoot.Length + 1).Replace('\', '/')
            $extra += "/resource:`"$($f.FullName)`",payload/$rel"
        }
        foreach ($l in @("BepInEx-LGPL-2.1.txt", "UnityDoorstop-LGPL-2.1.txt", "HarmonyX-MIT.txt", "MonoMod-MIT.txt", "Mono.Cecil-MIT.txt", "Mono-class-libraries-LICENSE.txt", "Newtonsoft.Json-MIT.txt")) {
            $extra += "/resource:`"$(Join-Path $root "third_party\licenses\$l")`",payload/BepInEx/plugins/NyangTalk/licenses/$l"
        }
    }
    foreach ($f in Get-ChildItem (Join-Path $root "third_party\fonts") -File) { $extra += "/resource:`"$($f.FullName)`",fonts/$($f.Name)" }
    foreach ($f in Get-ChildItem (Join-Path $root "third_party\licenses") -File) { $extra += "/resource:`"$($f.FullName)`",licenses/$($f.Name)" }
    $extra += "/resource:`"$(Join-Path $root 'LICENSE')`",LICENSE"
    $extra += "/resource:`"$(Join-Path $root 'THIRD-PARTY-NOTICES.md')`",THIRD-PARTY-NOTICES.md"
    $refs = $fwRefs + $uiRefs + @("$fw\System.Security.dll")
    $releaseExe = Join-Path $out "NyangTalk.exe"
    if (Test-Path $releaseExe) {
        try { [IO.File]::OpenWrite($releaseExe).Close() }
        catch {
            $oldExe = Join-Path $out "NyangTalk.running.exe"
            if (Test-Path $oldExe) { try { Remove-Item -Force $oldExe } catch {} }
            Move-Item -Force $releaseExe $oldExe
        }
    }
    Invoke-Csc "winexe" $releaseExe $src $refs $extra
    Write-Host "release built: build\NyangTalk.exe (connector payload embedded: $hasPlugin)"
    exit 0
}

# Layered build: each layer is its own assembly so the compiler enforces dependency direction.
[void](Build-Plugin)
$domainDll = Join-Path $out "NyangTalk.Domain.dll"
$appDll = Join-Path $out "NyangTalk.Application.dll"
$adDll = Join-Path $out "NyangTalk.Adapters.dll"
$sharedDll = Join-Path $out "NyangTalk.Shared.dll"
Invoke-Csc "library" $domainDll (Get-Sources @("src\Domain")) $fwRefs @()
Invoke-Csc "library" $sharedDll (Get-Sources @("src\Shared")) $fwRefs @()
Invoke-Csc "library" $appDll (Get-Sources @("src\Application")) ($fwRefs + @($domainDll)) @()
Invoke-Csc "library" $adDll (Get-Sources @("src\Adapters")) ($fwRefs + $uiRefs + @("$fw\System.Security.dll", $domainDll, $appDll, $sharedDll)) @()
Invoke-Csc "winexe" (Join-Path $out "NyangTalk.Layered.exe") (Get-Sources @("src\App")) ($fwRefs + $uiRefs + @($domainDll, $appDll, $adDll, $sharedDll)) @($platform)
Invoke-Csc "exe" (Join-Path $out "nyangtalk-contract.exe") (Get-Sources @("tools\ContractTool")) ($fwRefs + @($domainDll, $adDll, $sharedDll)) @($platform)
Invoke-Csc "exe" (Join-Path $out "nyangtalk-release.exe") (Get-Sources @("tools\ReleaseTool")) ($fwRefs + @($domainDll, $adDll, $sharedDll)) @($platform)
Copy-Item (Join-Path $root "src\Shared\contract.json") (Join-Path $out "contract.json") -Force
New-Item -ItemType Directory -Force (Join-Path $out "fonts") | Out-Null
Copy-Item (Join-Path $root "third_party\fonts\*") (Join-Path $out "fonts") -Force
if (Test-Path (Join-Path $root "assets\app.ico")) { Copy-Item (Join-Path $root "assets\app.ico") (Join-Path $out "app.ico") -Force }
Write-Host "layered build ok"

if (-not $NoTests) {
    $testExe = Join-Path $out "NyangTalk.Tests.exe"
    Invoke-Csc "exe" $testExe (Get-Sources @("tests")) ($fwRefs + $uiRefs + @("$fw\System.Security.dll", $domainDll, $appDll, $adDll, $sharedDll, (Join-Path $out "NyangTalk.Layered.exe"))) @($platform)
    Push-Location $out
    try { if ($TestFilter) { & $testExe $root $TestFilter } else { & $testExe $root }; $code = $LASTEXITCODE } finally { Pop-Location }
    if ($code -ne 0) { throw "tests failed ($code)" }
}
