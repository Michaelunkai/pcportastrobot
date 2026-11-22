# Settings-save regression: scenarios S1 S2 S3 per build (branch vs upstream main).
# Harness runs as a plain exe against the real compiled SettingsManager (astro-check.dll).
$ErrorActionPreference = 'Continue'
$pk = 'F:\InstinctTemp\pkgs'
$ref = "$pk\microsoft.netframework.referenceassemblies.net48\build\.NETFramework\v4.8"
$csc = "$pk\microsoft.net.compilers.toolset\tasks\net472\csc.exe"
$st = 'F:\InstinctTemp\settingstest'
New-Item -ItemType Directory -Force $st | Out-Null

cd F:\InstinctTemp\astro-build
git fetch origin
git show origin/build/compile-check:compile-check.ps1 | Set-Content F:\InstinctTemp\cc.ps1 -Encoding ASCII
Invoke-WebRequest 'https://raw.githubusercontent.com/Michaelunkai/pcportastrobot/dc995b6343f225dea07b84b48481eac809fd6587/settings-atomic-test.cs' -OutFile "$st\settings-atomic-test.cs" -UseBasicParsing

$libs = @("$pk\system.text.json\lib\net462\System.Text.Json.dll","$pk\system.text.encodings.web\lib\net462\System.Text.Encodings.Web.dll","$pk\system.buffers\lib\net461\System.Buffers.dll","$pk\system.memory\lib\net461\System.Memory.dll","$pk\system.runtime.compilerservices.unsafe\lib\net461\System.Runtime.CompilerServices.Unsafe.dll","$pk\system.threading.tasks.extensions\lib\net461\System.Threading.Tasks.Extensions.dll","$pk\microsoft.bcl.asyncinterfaces\lib\net462\Microsoft.Bcl.AsyncInterfaces.dll","$pk\system.numerics.vectors\lib\net46\System.Numerics.Vectors.dll")

function Build-Harness($appDir) {
  New-Item -ItemType Directory -Force $appDir | Out-Null
  Copy-Item -Force F:\InstinctTemp\astro-check.dll $appDir\
  foreach ($d in $libs) { Copy-Item -Force $d $appDir\ }
  # Generate binding redirects from the ACTUAL copied assembly versions: net48 + System.Text.Json
  # otherwise fails at runtime with FileLoadException (Unsafe 4.0.4.1 vs located 6.0.0.0 etc).
  $bindings = foreach ($f in Get-ChildItem "$appDir\*.dll") {
    $an = [Reflection.AssemblyName]::GetAssemblyName($f.FullName)
    $pkt = ([BitConverter]::ToString($an.GetPublicKeyToken())).Replace('-','').ToLower()
    if (-not $pkt) { $pkt = 'null' }
    "    <dependentAssembly><assemblyIdentity name=`"$($an.Name)`" publicKeyToken=`"$pkt`" culture=`"neutral`" /><bindingRedirect oldVersion=`"0.0.0.0-$($an.Version)`" newVersion=`"$($an.Version)`" /></dependentAssembly>"
  }
  $cfg = "<?xml version=`"1.0`" encoding=`"utf-8`" ?><configuration><runtime><assemblyBinding xmlns=`"urn:schemas-microsoft-com:asm.v1`">" + "`n" + ($bindings -join "`n") + "`n</assemblyBinding></runtime></configuration>"
  $cfg | Set-Content "$appDir\set-test.exe.config" -Encoding ASCII
  $rsp = @('/nologo','/nostdlib+','/noconfig','/t:exe','/langversion:latest','/nullable:enable','/nowarn:1701,1702,8632','/utf8output',"/out:$appDir\set-test.exe")
  foreach ($a in @('mscorlib','System','System.Core','System.Drawing','System.Management','System.Net.Http','System.Xaml','System.Windows.Forms','System.IO.Compression.FileSystem','System.ComponentModel.DataAnnotations','PresentationCore','PresentationFramework','WindowsBase','System.IO.Compression')) { $rsp += "/r:$ref\$a.dll" }
  $rsp += '/r:F:\InstinctTemp\astro-check.dll'
  foreach ($d in $libs) { $rsp += "/r:$d" }
  $rsp += "$st\settings-atomic-test.cs"
  $rsp | Set-Content "$st\set.rsp" -Encoding ASCII
  & $csc "@$st\set.rsp" 2>&1 | Out-File "$appDir\build.log"
  $errs = Select-String -Path "$appDir\build.log" -Pattern ': error' -ErrorAction SilentlyContinue
  Write-Output "build $appDir exit=$LASTEXITCODE errors=$($errs.Count)"
  $errs | Select-Object -First 10 | ForEach-Object { $_.Line }
}

function Run-Scenarios($appDir, $tag) {
  $total = 0
  foreach ($s in @('S1','S2','S3')) {
    Write-Output "--- $tag scenario $s ---"
    & "$appDir\set-test.exe" $s
    $total += $LASTEXITCODE
  }
  Write-Output "$tag scenario-failures total: $total"
}

Write-Output '=== AFTER (fix branch) ==='
git checkout -B fix/settings-concurrent-save origin/fix/settings-concurrent-save
powershell -ExecutionPolicy Bypass -File F:\InstinctTemp\cc.ps1
Build-Harness "$st\app-after"
Run-Scenarios "$st\app-after" 'AFTER'

Write-Output '=== BEFORE (upstream main) ==='
git checkout -B set-baseline origin/main
powershell -ExecutionPolicy Bypass -File F:\InstinctTemp\cc.ps1
Build-Harness "$st\app-before"
Run-Scenarios "$st\app-before" 'BEFORE'

git checkout main
Write-Output '=== DONE ==='
