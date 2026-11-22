# Self-update regression v2: scenarios A B C D G F per build (branch vs upstream main),
# one process per scenario. Harness runs AS AstroHybrid.exe from separate app dirs.
$ErrorActionPreference = 'Continue'
$pk = 'F:\InstinctTemp\pkgs'
$ref = "$pk\microsoft.netframework.referenceassemblies.net48\build\.NETFramework\v4.8"
$csc = "$pk\microsoft.net.compilers.toolset\tasks\net472\csc.exe"
$ut = 'F:\InstinctTemp\updatetest2'
New-Item -ItemType Directory -Force $ut | Out-Null

cd F:\InstinctTemp\astro-build
git fetch origin
git show origin/build/compile-check:compile-check.ps1 | Set-Content F:\InstinctTemp\cc.ps1 -Encoding ASCII
Invoke-WebRequest 'https://raw.githubusercontent.com/Michaelunkai/pcportastrobot/848a556108caddbdd70ce6be5bbca12eaaff986a/update-selfreplace-test2.cs' -OutFile "$ut\update-selfreplace-test2.cs" -UseBasicParsing

function Build-Harness($appDir) {
  New-Item -ItemType Directory -Force $appDir | Out-Null
  Copy-Item -Force F:\InstinctTemp\astro-check.dll $appDir\
  foreach ($d in @("$pk\system.text.json\lib\net462\System.Text.Json.dll","$pk\system.text.encodings.web\lib\net462\System.Text.Encodings.Web.dll","$pk\system.buffers\lib\net461\System.Buffers.dll","$pk\system.memory\lib\net461\System.Memory.dll","$pk\system.runtime.compilerservices.unsafe\lib\net461\System.Runtime.CompilerServices.Unsafe.dll","$pk\system.threading.tasks.extensions\lib\net461\System.Threading.Tasks.Extensions.dll","$pk\microsoft.bcl.asyncinterfaces\lib\net462\Microsoft.Bcl.AsyncInterfaces.dll","$pk\system.numerics.vectors\lib\net46\System.Numerics.Vectors.dll")) { Copy-Item -Force $d $appDir\ }
  $rsp = @('/nologo','/nostdlib+','/noconfig','/t:exe','/langversion:latest','/nullable:enable','/nowarn:1701,1702,8632','/utf8output',"/out:$appDir\AstroHybrid.exe")
  foreach ($a in @('mscorlib','System','System.Core','System.Drawing','System.Management','System.Net.Http','System.Xaml','System.Windows.Forms','System.IO.Compression.FileSystem','System.ComponentModel.DataAnnotations','PresentationCore','PresentationFramework','WindowsBase','System.IO.Compression')) { $rsp += "/r:$ref\$a.dll" }
  $rsp += '/r:F:\InstinctTemp\astro-check.dll'
  $rsp += "$ut\update-selfreplace-test2.cs"
  $rsp | Set-Content "$ut\upd2.rsp" -Encoding ASCII
  & $csc "@$ut\upd2.rsp" 2>&1 | Out-File "$appDir\build.log"
  $errs = Select-String -Path "$appDir\build.log" -Pattern ': error' -ErrorAction SilentlyContinue
  Write-Output "build $appDir exit=$LASTEXITCODE errors=$($errs.Count)"
  $errs | Select-Object -First 10 | ForEach-Object { $_.Line }
}

function Run-Scenarios($appDir, $tag) {
  $total = 0
  foreach ($s in @('A','B','C','D','G','F')) {
    Write-Output "--- $tag scenario $s ---"
    & "$appDir\AstroHybrid.exe" $s
    $total += $LASTEXITCODE
  }
  Write-Output "$tag scenario-failures total: $total"
}

Write-Output '=== AFTER (fix branch) ==='
git checkout -B fix/update-replace-running-files origin/fix/update-replace-running-files
powershell -ExecutionPolicy Bypass -File F:\InstinctTemp\cc.ps1
Build-Harness "$ut\app-after"
Run-Scenarios "$ut\app-after" 'AFTER'

Write-Output '=== BEFORE (upstream main) ==='
git checkout -B upd-baseline origin/main
powershell -ExecutionPolicy Bypass -File F:\InstinctTemp\cc.ps1
Build-Harness "$ut\app-before"
Run-Scenarios "$ut\app-before" 'BEFORE'

git checkout main
Write-Output '=== DONE ==='
