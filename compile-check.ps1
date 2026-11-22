# AstroHybrid compile check: Roslyn csc.exe (Microsoft.Net.Compilers.Toolset, runs on .NET Framework 4.8) against net48 reference
# assemblies fetched from nuget.org. Compiles all C# sources except XAML codebehind
# (Views\*.xaml.cs and App.xaml.cs), whose .g.cs partials need a full msbuild WPF pass.
$ErrorActionPreference = 'Continue'
$root = 'F:\InstinctTemp\astro-build'
$pk = 'F:\InstinctTemp\pkgs'
New-Item -ItemType Directory -Force $pk | Out-Null
Remove-Item -Force "$root\global.json" -ErrorAction SilentlyContinue

$pkgs = @(
  @{id='microsoft.netframework.referenceassemblies.net48'; v='1.0.3'},
  @{id='system.text.json'; v='8.0.5'},
  @{id='system.text.encodings.web'; v='8.0.0'},
  @{id='system.buffers'; v='4.5.1'},
  @{id='system.memory'; v='4.5.5'},
  @{id='system.runtime.compilerservices.unsafe'; v='6.0.0'},
  @{id='system.threading.tasks.extensions'; v='4.5.4'},
  @{id='microsoft.bcl.asyncinterfaces'; v='8.0.0'},
  @{id='system.numerics.vectors'; v='4.5.0'},
  @{id='microsoft.net.compilers.toolset'; v='4.12.0'}
)
foreach ($p in $pkgs) {
  $dst = "$pk\$($p.id)"
  if (Test-Path $dst) { continue }
  $u = "https://www.nuget.org/api/v2/package/$($p.id)/$($p.v)"
  $z = "$pk\$($p.id).zip"
  try {
    Invoke-WebRequest $u -OutFile $z -UseBasicParsing
    Expand-Archive -Force $z $dst
    Write-Output "pkg ok $($p.id)"
  } catch { Write-Output "PKG FAIL $($p.id): $($_.Exception.Message)" }
}

$ref = "$pk\microsoft.netframework.referenceassemblies.net48\build\.NETFramework\v4.8"
$refs = @('mscorlib','System','System.Core','System.Drawing','System.Management','System.Net.Http','System.Xaml','System.Windows.Forms','System.IO.Compression.FileSystem','System.ComponentModel.DataAnnotations','PresentationCore','PresentationFramework','WindowsBase','System.IO.Compression') | ForEach-Object { "$ref\$_.dll" }
$libs = @(
  "$pk\system.text.json\lib\net462\System.Text.Json.dll",
  "$pk\system.text.encodings.web\lib\net462\System.Text.Encodings.Web.dll",
  "$pk\system.buffers\lib\net461\System.Buffers.dll",
  "$pk\system.memory\lib\net461\System.Memory.dll",
  "$pk\system.runtime.compilerservices.unsafe\lib\net461\System.Runtime.CompilerServices.Unsafe.dll",
  "$pk\system.threading.tasks.extensions\lib\net461\System.Threading.Tasks.Extensions.dll",
  "$pk\microsoft.bcl.asyncinterfaces\lib\net462\Microsoft.Bcl.AsyncInterfaces.dll",
  "$pk\system.numerics.vectors\lib\net46\System.Numerics.Vectors.dll"
)

$files = Get-ChildItem -Recurse -Filter *.cs "$root\AstroHybrid.App" |
  Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\|\\Views\\' -and $_.FullName -notmatch '\\App\.xaml\.cs$' } |
  ForEach-Object { $_.FullName }

$rsp = @('/nologo','/nostdlib+','/noconfig','/t:library','/langversion:latest','/nullable:enable','/define:TRACE','/nowarn:1701,1702,8632','/utf8output','/out:F:\InstinctTemp\astro-check.dll')
foreach ($r in ($refs + $libs)) {
  if (Test-Path $r) { $rsp += "/r:$r" } else { Write-Output "MISSING REF $r" }
}
foreach ($f in $files) { $rsp += "`"$f`"" }
$rsp | Set-Content F:\InstinctTemp\csc.rsp -Encoding ASCII

$csc = "$pk\microsoft.net.compilers.toolset\tools\net472\csc.exe"
if (Test-Path $csc) {
  & $csc '@F:\InstinctTemp\csc.rsp' 2>&1 | Out-File F:\InstinctTemp\csc.log
} else {
  Write-Output "CSC MISSING $csc"
}
$errs = Select-String -Path F:\InstinctTemp\csc.log -Pattern ': error' -ErrorAction SilentlyContinue
Write-Output "csc exit=$LASTEXITCODE errors=$($errs.Count)"
$errs | Select-Object -First 25 | ForEach-Object { $_.Line }
