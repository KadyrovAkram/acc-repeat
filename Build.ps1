$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The 64-bit .NET Framework 4.x C# compiler was not found.'
}

$bin = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $bin -Force | Out-Null
$exe = Join-Path $bin 'ACC Repeat Corner.exe'
$source = Join-Path $PSScriptRoot 'src/RepeatCorner.cs'
$masks = Join-Path $PSScriptRoot 'src/MenuSignatures.cs'

& $compiler /nologo /optimize+ /platform:x64 /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/out:$exe" $source $masks
if ($LASTEXITCODE -ne 0) {
    throw "C# compiler exited with code $LASTEXITCODE."
}

Write-Output $exe
