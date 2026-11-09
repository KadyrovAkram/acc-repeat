param([switch]$Test, [switch]$Console)

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The 64-bit .NET Framework 4.x C# compiler was not found.'
}
$speech = Join-Path $env:WINDIR 'Microsoft.NET/assembly/GAC_MSIL/System.Speech/v4.0_4.0.0.0__31bf3856ad364e35/System.Speech.dll'

$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$exe = Join-Path $bin 'ACC Repeat.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File |
    ForEach-Object { $_.FullName })

$target = $exe
$entryPoint = 'PracticeDeskApp'
$targetKind = 'winexe'
if ($Console) {
    $target = Join-Path $bin 'ACC Repeat - Console.exe'
    $entryPoint = 'RepeatCorner'
    $targetKind = 'exe'
}

& $compiler /nologo /optimize+ /platform:x64 "/target:$targetKind" /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/reference:$speech" "/out:$target" "/main:$entryPoint" @sources
if ($LASTEXITCODE -ne 0) {
    throw "C# compiler exited with code $LASTEXITCODE."
}

$buildMaps = Join-Path $bin 'Maps'
New-Item -ItemType Directory -Path $buildMaps -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Maps') -File |
    Copy-Item -Destination $buildMaps -Force

Write-Output $target

if ($Test) {
    # The xUnit tests compile the same sources against .NET Framework 4.8 and need the .NET SDK.
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Running the tests needs the .NET SDK (https://dotnet.microsoft.com/download).'
    }
    & dotnet test (Join-Path $PSScriptRoot 'tests/ACCRepeat.Tests.csproj') --configuration Release --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
