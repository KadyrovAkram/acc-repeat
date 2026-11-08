param([switch]$Test)

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The 64-bit .NET Framework 4.x C# compiler was not found.'
}

$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$exe = Join-Path $bin 'ACC Repeat Corner.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File |
    ForEach-Object { $_.FullName })

$target = $exe
$entryPoint = 'RepeatCorner'
if ($Test) {
    $target = Join-Path $bin 'PracticeDrillTests.exe'
    $entryPoint = 'PracticeDrillTests'
    $sources += Join-Path $PSScriptRoot 'tests/PracticeDrillTests.cs'
}

& $compiler /nologo /optimize+ /platform:x64 /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/out:$target" "/main:$entryPoint" @sources
if ($LASTEXITCODE -ne 0) {
    throw "C# compiler exited with code $LASTEXITCODE."
}

if ($Test) {
    & $target
    if ($LASTEXITCODE -ne 0) { throw 'Practice drill checks failed.' }
} else {
    Write-Output $exe
}
