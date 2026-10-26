param([string]$Root, [string]$UpdaterScript)
$ErrorActionPreference = 'Stop'
. $UpdaterScript
$payload = Join-Path $Root 'payload'
$target = Join-Path $Root 'installed'
New-Item -ItemType Directory -Path (Join-Path $payload 'PhantomOverlay\x64'), (Join-Path $target 'PhantomOverlay\x64') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $payload 'app.txt'), 'new')
[IO.File]::WriteAllText((Join-Path $payload 'new.txt'), 'added')
[IO.File]::WriteAllText((Join-Path $payload 'config.json'), 'payload-must-not-replace-user')
[IO.File]::WriteAllText((Join-Path $payload 'phantom.portable'), 'portable')
[IO.File]::WriteAllText((Join-Path $payload 'PhantomOverlay\x64\hook.dll'), 'new-native')
[IO.File]::WriteAllText((Join-Path $target 'app.txt'), 'old')
[IO.File]::WriteAllText((Join-Path $target 'config.json'), 'user-settings')
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
if ([IO.File]::ReadAllText((Join-Path $target 'config.json')) -ne 'user-settings') { throw 'Rollback changed user configuration.' }
Invoke-PhantomUpdateTransaction -Payload $payload -Target $target | Out-Null
if ([IO.File]::ReadAllText((Join-Path $target 'app.txt')) -ne 'new') { throw 'App not updated.' }
if ([IO.File]::ReadAllText((Join-Path $target 'PhantomOverlay\x64\hook.dll')) -ne 'new-native') { throw 'Nested DLL not updated.' }
if ([IO.File]::ReadAllText((Join-Path $target 'config.json')) -ne 'user-settings') { throw 'Update changed user configuration.' }
if (-not (Test-Path -LiteralPath (Join-Path $target 'phantom.portable'))) { throw 'Portable marker missing.' }
$installed = Join-Path $Root 'legacy-installed'
New-Item -ItemType Directory -Path $installed -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $installed 'config.json'), 'installed-local-file')
Invoke-PhantomUpdateTransaction -Payload $payload -Target $installed -Installed $true | Out-Null
if (Test-Path -LiteralPath (Join-Path $installed 'phantom.portable')) { throw 'Installed update copied portable marker.' }
if ([IO.File]::ReadAllText((Join-Path $installed 'config.json')) -ne 'installed-local-file') { throw 'Installed update changed local config.' }
Write-Output 'PASS: transactional update and complete rollback in isolated directories'
