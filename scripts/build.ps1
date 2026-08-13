$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir = Split-Path -Parent $projectDir
$source = Join-Path $repoDir 'src\CodexPort.cs'
$outputDir = Join-Path $repoDir 'dist'
$outputDir = [System.IO.Path]::GetFullPath($outputDir)
$output = Join-Path $outputDir 'CodexPort.exe'

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Force
}

$references = @(
    'System.dll',
    'System.Core.dll',
    'System.Drawing.dll',
    'System.Windows.Forms.dll',
    'System.IO.Compression.dll',
    'System.IO.Compression.FileSystem.dll',
    'System.Web.Extensions.dll'
)

$sourceCode = Get-Content -LiteralPath $source -Raw -Encoding UTF8

Add-Type -TypeDefinition $sourceCode `
    -ReferencedAssemblies $references `
    -OutputAssembly $output `
    -OutputType WindowsApplication

Write-Output $output
