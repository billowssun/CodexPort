$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("CodexPort-tests-{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null

try {
    & (Join-Path $repo 'scripts\build.ps1') | Out-Null
    $fixtureJson = & python (Join-Path $repo 'tests\fixtures.py') create $fixtureRoot
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'fixture.json') -Value $fixtureJson -Encoding UTF8
    $fixture = $fixtureJson | ConvertFrom-Json

    $assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'dist\CodexPort.exe'))
    $type = $assembly.GetType('CodexPort.MigrationEngine', $true)
    $flags = [Reflection.BindingFlags]'Static,Public,NonPublic'
    $type.GetField('TestMode', $flags).SetValue($null, $true)
    $report = [Action[string]] { param($message) Write-Verbose $message }
    $package = Join-Path $fixtureRoot 'source.codexport.zip'
    $sourceHome = $fixture.source.ToString()
    $targetHome = $fixture.target.ToString()
    $exportArguments = [object[]]@($sourceHome, $package.ToString(), $report)
    $type.GetMethod('Export', $flags).Invoke($null, $exportArguments) | Out-Null

    $importArguments = [object[]]@($targetHome, $package.ToString(), $report)
    $first = $type.GetMethod('Import', $flags).Invoke($null, $importArguments)
    if ($first.AddedThreads -ne 2 -or $first.ConflictCopies -ne 1 -or $first.TotalThreads -ne 4) {
        throw "Unexpected first import result: added=$($first.AddedThreads), conflicts=$($first.ConflictCopies), total=$($first.TotalThreads)"
    }
    & python (Join-Path $repo 'tests\fixtures.py') verify $fixtureRoot
    if ($LASTEXITCODE -ne 0) { throw 'Fixture verification failed.' }

    $second = $type.GetMethod('Import', $flags).Invoke($null, $importArguments)
    if ($second.AddedThreads -ne 0 -or $second.TotalThreads -ne 4) {
        throw "Import is not idempotent: added=$($second.AddedThreads), total=$($second.TotalThreads)"
    }
    Write-Output 'CodexPort merge tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
