$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoDir = Split-Path -Parent $projectDir
$source = Join-Path $repoDir 'src\CodexPort.cs'
$outputDir = Join-Path $repoDir 'dist'
$outputDir = [System.IO.Path]::GetFullPath($outputDir)
$output = Join-Path $outputDir 'CodexPort.exe'
$temporaryOutput = Join-Path $outputDir ("CodexPort.build-{0}.exe" -f [Guid]::NewGuid().ToString('N'))
$backupOutput = Join-Path $outputDir ("CodexPort.previous-{0}.exe" -f [Guid]::NewGuid().ToString('N'))

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

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

try {
    Add-Type -TypeDefinition $sourceCode `
        -ReferencedAssemblies $references `
        -OutputAssembly $temporaryOutput `
        -OutputType WindowsApplication

    if (Test-Path -LiteralPath $output) {
        [System.IO.File]::Replace($temporaryOutput, $output, $backupOutput)
        Remove-Item -LiteralPath $backupOutput -Force
    }
    else {
        [System.IO.File]::Move($temporaryOutput, $output)
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryOutput) {
        Remove-Item -LiteralPath $temporaryOutput -Force
    }
    if (Test-Path -LiteralPath $backupOutput) {
        Remove-Item -LiteralPath $backupOutput -Force
    }
}

Write-Output $output
