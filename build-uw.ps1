# Ultrawide resolution-label regression: compiles astro-check.dll from the fix branch
# and from upstream main, runs the ultrawide harness against each. HomeViewModel.cs is a
# non-XAML source, so it is inside the compiled astro-check.dll scope.
$ErrorActionPreference = 'Continue'
$pk = 'F:\InstinctTemp\pkgs'
$ref = "$pk\microsoft.netframework.referenceassemblies.net48\build\.NETFramework\v4.8"
$csc = "$pk\microsoft.net.compilers.toolset\tasks\net472\csc.exe"
$uw = 'F:\InstinctTemp\uwtest'
New-Item -ItemType Directory -Force $uw | Out-Null

cd F:\InstinctTemp\astro-build
git fetch origin
git show origin/build/compile-check:compile-check.ps1 | Set-Content F:\InstinctTemp\cc.ps1 -Encoding ASCII

# 9c6e551c8ec572525eba7826c775bdd832ffd642
Invoke-WebRequest 'https://raw.githubusercontent.com/Michaelunkai/pcportastrobot/9c6e551c8ec572525eba7826c775bdd832ffd642/ultrawide-test.cs' -OutFile "$uw\ultrawide-test.cs" -UseBasicParsing

function Build-UW($out) {
  $rsp = @('/nologo','/nostdlib+','/noconfig','/t:exe','/langversion:latest','/nullable:enable','/nowarn:1701,1702,8632','/utf8output',"/out:$out")
  foreach ($a in @('mscorlib','System','System.Core','System.Drawing','System.Management','System.Net.Http','System.Xaml','System.Windows.Forms','System.IO.Compression.FileSystem','System.ComponentModel.DataAnnotations','PresentationCore','PresentationFramework','WindowsBase','System.IO.Compression')) { $rsp += "/r:$ref\$a.dll" }
  $rsp += '/r:F:\InstinctTemp\astro-check.dll'
  $rsp += "$uw\ultrawide-test.cs"
  $rsp | Set-Content "$uw\uw.rsp" -Encoding ASCII
  & $csc "@$uw\uw.rsp" 2>&1 | Out-File "$out.build.log"
  $errs = Select-String -Path "$out.build.log" -Pattern ': error' -ErrorAction SilentlyContinue
  Write-Output "build $out exit=$LASTEXITCODE errors=$($errs.Count)"
  $errs | Select-Object -First 10 | ForEach-Object { $_.Line }
}

function Copy-RuntimeDlls {
  Copy-Item -Force F:\InstinctTemp\astro-check.dll $uw\
  foreach ($d in @("$pk\system.text.json\lib\net462\System.Text.Json.dll","$pk\system.text.encodings.web\lib\net462\System.Text.Encodings.Web.dll","$pk\system.buffers\lib\net461\System.Buffers.dll","$pk\system.memory\lib\net461\System.Memory.dll","$pk\system.runtime.compilerservices.unsafe\lib\net461\System.Runtime.CompilerServices.Unsafe.dll","$pk\system.threading.tasks.extensions\lib\net461\System.Threading.Tasks.Extensions.dll","$pk\microsoft.bcl.asyncinterfaces\lib\net462\Microsoft.Bcl.AsyncInterfaces.dll","$pk\system.numerics.vectors\lib\net46\System.Numerics.Vectors.dll")) { Copy-Item -Force $d $uw\ }
}

Write-Output '=== AFTER (fix branch) ==='
git checkout -B fix/home-ultrawide-resolution-label origin/fix/home-ultrawide-resolution-label
powershell -ExecutionPolicy Bypass -File F:\InstinctTemp\cc.ps1
Copy-RuntimeDlls
Build-UW "$uw\uw.exe"
& "$uw\uw.exe"
Write-Output "uw.exe exit=$LASTEXITCODE"

Write-Output '=== BEFORE (upstream main) ==='
git checkout -B uw-baseline origin/main
powershell -ExecutionPolicy Bypass -File F:\InstinctTemp\cc.ps1
Copy-RuntimeDlls
Build-UW "$uw\uw-main.exe"
& "$uw\uw-main.exe"
Write-Output "uw-main.exe exit=$LASTEXITCODE"

git checkout main
Write-Output '=== DONE ==='
