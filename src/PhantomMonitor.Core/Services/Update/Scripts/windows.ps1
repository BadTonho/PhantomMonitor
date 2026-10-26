# Embedded one-shot updater. Dot-source to test the transaction with isolated directories.
function Invoke-PhantomUpdateTransaction {
    param(
        [Parameter(Mandatory = $true)][string]$Payload,
        [Parameter(Mandatory = $true)][string]$Target,
        [scriptblock]$CopyFile = { param($Source, $Destination) [System.IO.File]::Copy($Source, $Destination, $true) }
    )
    $ErrorActionPreference = 'Stop'
    $payloadRoot = [System.IO.Path]::GetFullPath($Payload).TrimEnd('\') + '\'
    $targetRoot = [System.IO.Path]::GetFullPath($Target).TrimEnd('\') + '\'
    if ($payloadRoot.StartsWith($targetRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $targetRoot.StartsWith($payloadRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Payload and installation must be separate.' }
    $files = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse -Force)
    if ($files.Count -eq 0) { throw 'The update payload is empty.' }
    # Reject links before recursion/copy, including directories inside the target.
    foreach ($entry in @(Get-Item -LiteralPath $payloadRoot, $targetRoot) + @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -Force)) {
        if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Links are not allowed in the update payload.' }
    }
    $names = @($files | ForEach-Object { $_.FullName.Substring($payloadRoot.Length) })
    foreach ($name in $names) {
        if ($name.Split('\') | Where-Object { $_ -like '.phantom-update-*' }) { throw 'Reserved update path.' }
        $destination = [System.IO.Path]::GetFullPath((Join-Path $targetRoot $name))
        if (-not $destination.StartsWith($targetRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid destination.' }
        $ancestor = $destination
        while ($ancestor.Length -ge $targetRoot.TrimEnd('\').Length) {
            if (Test-Path -LiteralPath $ancestor) {
                $item = Get-Item -LiteralPath $ancestor -Force
                if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Linked destination is not supported.' }
            }
            $ancestor = Split-Path -Parent $ancestor
            if ([string]::IsNullOrEmpty($ancestor)) { break }
        }
        if (Test-Path -LiteralPath $destination -PathType Container) { throw 'A directory conflicts with a payload file.' }
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            $handle = [System.IO.File]::Open($destination, 'Open', 'ReadWrite', 'None')
            $handle.Dispose()
        }
    }
    $backup = Join-Path $targetRoot ('.phantom-update-backup-' + [guid]::NewGuid().ToString('N'))
    $incoming = Join-Path $backup 'incoming'
    New-Item -ItemType Directory -Path $incoming -Force | Out-Null
    $journal = [System.Collections.Generic.List[object]]::new()
    try {
        # All copies finish before touching the installed files.
        foreach ($name in $names) {
            $prepared = Join-Path $incoming $name
            New-Item -ItemType Directory -Path (Split-Path -Parent $prepared) -Force | Out-Null
            & $CopyFile (Join-Path $payloadRoot $name) $prepared
        }
        foreach ($name in $names) {
            $destination = Join-Path $targetRoot $name
            $entry = [pscustomobject]@{ Name = $name; BackedUp = $false; Installed = $false; Installing = $false }
            $journal.Add($entry)
            if (Test-Path -LiteralPath $destination) {
                $old = Join-Path (Join-Path $backup 'original') $name
                New-Item -ItemType Directory -Path (Split-Path -Parent $old) -Force | Out-Null
                Move-Item -LiteralPath $destination -Destination $old
                $entry.BackedUp = $true
            }
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            # CopyFile is injectable so the regression test can model disk failure
            # after a preceding file has already been installed.
            $entry.Installing = $true
            & $CopyFile (Join-Path $incoming $name) $destination
            $entry.Installed = $true
            $journal | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $backup 'journal.json') -Encoding UTF8
        }
        'committed' | Set-Content -LiteralPath (Join-Path $backup 'status')
        return $backup
    }
    catch {
        $failure = $_
        $rollbackErrors = @()
        for ($index = $journal.Count - 1; $index -ge 0; $index--) {
            $entry = $journal[$index]
            $destination = Join-Path $targetRoot $entry.Name
            try {
                # A failed copy can have created a partial file even before Installed=true.
                if ($entry.Installing -and (Test-Path -LiteralPath $destination -PathType Leaf)) { Remove-Item -LiteralPath $destination -Force }
                if ($entry.BackedUp) {
                    Move-Item -LiteralPath (Join-Path (Join-Path $backup 'original') $entry.Name) -Destination $destination
                }
            } catch { $rollbackErrors += $_.Exception.Message }
        }
        $status = if ($rollbackErrors.Count -eq 0) { 'rolled-back' } else { 'recovery-required: ' + ($rollbackErrors -join '; ') }
        $status | Set-Content -LiteralPath (Join-Path $backup 'status')
        throw "Update failed: $($failure.Exception.Message). $status. Backup: $backup"
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $ErrorActionPreference = 'Stop'
    $parentId = [int]$env:PHANTOM_UPDATE_PID
    if ($parentId -gt 0) {
        $parent = Get-Process -Id $parentId -ErrorAction SilentlyContinue
        if ($parent -and -not $parent.WaitForExit(60000)) { throw 'Application did not exit; installation was not changed.' }
    }
    try {
        Invoke-PhantomUpdateTransaction -Payload $env:PHANTOM_UPDATE_PAYLOAD -Target $env:PHANTOM_UPDATE_TARGET | Out-Null
        $executable = Join-Path $env:PHANTOM_UPDATE_TARGET $env:PHANTOM_UPDATE_EXE
        Start-Process -FilePath $executable -WorkingDirectory $env:PHANTOM_UPDATE_TARGET
    } catch {
        $_ | Out-String | Set-Content -LiteralPath (Join-Path $env:PHANTOM_UPDATE_PAYLOAD 'update-failure.txt')
        exit 1
    }
}
