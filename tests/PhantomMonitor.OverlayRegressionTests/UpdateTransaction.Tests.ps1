param([string]$Root, [string]$UpdaterScript)
$ErrorActionPreference = 'Stop'
. $UpdaterScript
$payload = Join-Path $Root 'payload'
$target = Join-Path $Root 'installed'
New-Item -ItemType Directory -Path (Join-Path $payload 'PhantomOverlay\x64'), (Join-Path $target 'PhantomOverlay\x64') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $payload 'app.txt'), 'new')
[IO.File]::WriteAllText((Join-Path $payload 'new.txt'), 'added')
[IO.File]::WriteAllText((Join-Path $payload 'PhantomOverlay\x64\hook.dll'), 'new-native')
[IO.File]::WriteAllText((Join-Path $target 'app.txt'), 'old')
[IO.File]::WriteAllText((Join-Path $target 'PhantomOverlay\x64\hook.dll'), 'old-native')
$script:installCount = 0
$copy = {
    param($Source, $Destination)
    if (-not $Destination.Contains('.phantom-update-backup-')) {
        $script:installCount++
        if ($script:installCount -eq 3) { [IO.File]::WriteAllText($Destination, 'partial'); throw 'Simulated disk failure' }
    }
    [IO.File]::Copy($Source, $Destination, $true)
}
$failed = $false
try { Invoke-PhantomUpdateTransaction -Payload $payload -Target $target -CopyFile $copy | Out-Null }
catch { $failed = $true }
if (-not $failed -or $script:installCount -ne 3) { throw 'Failure was not injected after previous writes.' }
if ([IO.File]::ReadAllText((Join-Path $target 'app.txt')) -ne 'old') { throw 'App was not rolled back.' }
if ([IO.File]::ReadAllText((Join-Path $target 'PhantomOverlay\x64\hook.dll')) -ne 'old-native') { throw 'Native DLL was not rolled back.' }
if (Test-Path -LiteralPath (Join-Path $target 'new.txt')) { throw 'New file was not removed by rollback.' }
Invoke-PhantomUpdateTransaction -Payload $payload -Target $target | Out-Null
if ([IO.File]::ReadAllText((Join-Path $target 'app.txt')) -ne 'new') { throw 'App not updated.' }
if ([IO.File]::ReadAllText((Join-Path $target 'PhantomOverlay\x64\hook.dll')) -ne 'new-native') { throw 'Nested DLL not updated.' }
Write-Output 'PASS: transactional update and complete rollback in isolated directories'
