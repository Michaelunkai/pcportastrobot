# Page-view reuse regression: compiles REAL MainWindow.xaml.cs (branch and upstream main)
# against astro-check.dll + net48 WPF reference assemblies, runs identity harness on both.
$ErrorActionPreference = 'Continue'
$pk = 'F:\InstinctTemp\pkgs'
$ref = "$pk\microsoft.netframework.referenceassemblies.net48\build\.NETFramework\v4.8"
$csc = "$pk\microsoft.net.compilers.toolset\tasks\net472\csc.exe"
$pv = 'F:\InstinctTemp\pvtest'
New-Item -ItemType Directory -Force $pv | Out-Null

cd F:\InstinctTemp\astro-build
git fetch origin
git checkout -B fix/page-view-recreation-leak origin/fix/page-view-recreation-leak
git show origin/build/compile-check:compile-check.ps1 | Set-Content F:\InstinctTemp\cc.ps1 -Encoding ASCII
powershell -ExecutionPolicy Bypass -File F:\InstinctTemp\cc.ps1

Invoke-WebRequest 'https://raw.githubusercontent.com/Michaelunkai/pcportastrobot/72d70a94bb9c4d392fc3936245096e92da86df49/page-view-test.cs' -OutFile "$pv\page-view-test.cs" -UseBasicParsing
Copy-Item -Force F:\InstinctTemp\astro-check.dll $pv\
foreach ($d in @("$pk\system.text.json\lib\net462\System.Text.Json.dll","$pk\system.text.encodings.web\lib\net462\System.Text.Encodings.Web.dll","$pk\system.buffers\lib\net461\System.Buffers.dll","$pk\system.memory\lib\net461\System.Memory.dll","$pk\system.runtime.compilerservices.unsafe\lib\net461\System.Runtime.CompilerServices.Unsafe.dll","$pk\system.threading.tasks.extensions\lib\net461\System.Threading.Tasks.Extensions.dll","$pk\microsoft.bcl.asyncinterfaces\lib\net462\Microsoft.Bcl.AsyncInterfaces.dll","$pk\system.numerics.vectors\lib\net46\System.Numerics.Vectors.dll")) { Copy-Item -Force $d $pv\ }

function Build-PV($mwPath, $out) {
  $rsp = @('/nologo','/nostdlib+','/noconfig','/t:exe','/langversion:latest','/nullable:enable','/nowarn:1701,1702,8632','/utf8output',"/out:$out")
  foreach ($a in @('mscorlib','System','System.Core','System.Xaml','PresentationCore','PresentationFramework','WindowsBase')) { $rsp += "/r:$ref\$a.dll" }
  $rsp += '/r:F:\InstinctTemp\astro-check.dll'
  $rsp += "$pv\page-view-test.cs"
  $rsp += "`"$mwPath`""
  $rsp | Set-Content "$pv\pv.rsp" -Encoding ASCII
  & $csc "@$pv\pv.rsp" 2>&1 | Out-File "$out.build.log"
  $errs = Select-String -Path "$out.build.log" -Pattern ': error' -ErrorAction SilentlyContinue
  Write-Output "build $out exit=$LASTEXITCODE errors=$($errs.Count)"
  $errs | Select-Object -First 10 | ForEach-Object { $_.Line }
}

Write-Output '=== AFTER (fix branch) ==='
Build-PV 'F:\InstinctTemp\astro-build\AstroHybrid.App\Views\MainWindow.xaml.cs' "$pv\pv.exe"
& "$pv\pv.exe"
Write-Output "pv.exe exit=$LASTEXITCODE"

Write-Output '=== BEFORE (upstream main) ==='
git show origin/main:AstroHybrid.App/Views/MainWindow.xaml.cs | Set-Content "$pv\MainWindow-main.cs" -Encoding ASCII
Build-PV "$pv\MainWindow-main.cs" "$pv\pv-main.exe"
& "$pv\pv-main.exe"
Write-Output "pv-main.exe exit=$LASTEXITCODE"
Write-Output '=== DONE ==='
