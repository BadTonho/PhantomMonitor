<#
.SYNOPSIS
    Gera releases locais do Phantom Monitor (o projeto não publica via CI).

.DESCRIPTION
    Publica o app único self-contained single-file (win-x64 e linux-x64)
    com a versão compilada casando com a tag, monta os pacotes na convenção
    que o updater espera (docs/funções/atualizações.md) e imprime o próximo
    passo: criar a tag v<versão> e subir os arquivos manualmente no GitHub.
    Nenhum binário deste processo é commitado (releases/ está no .gitignore).

.EXAMPLE
    powershell -File scripts/release.ps1 -Version 1.2.3
    powershell -File scripts/release.ps1 -Version 1.2.3 -PackWindowsSetup -LayerSoPath build\PhantomVulkanLayer-linux.so
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,        # SemVer simples (1.2.3); a tag v<versão> NÃO é criada aqui

    [switch]$SkipWindows,
    [switch]$SkipLinux,

    # Empacota o instalador per-user (Inno Setup 6) quando o ISCC.exe existir.
    [switch]$PackWindowsSetup,
    [string]$IsccPath,

    # .so já compilada da camada Vulkan; entra como PhantomVulkanLayer-<versão>-linux.so.
    [string]$LayerSoPath,

    # Saída dos pacotes (padrão: <repo>\releases\<versão>\).
    [string]$OutDirectory
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.\-]+)?$') {
    throw "Versão inválida '$Version' (use SemVer simples: 1.2.3 ou 1.2.3-beta1)."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutDirectory)) {
    $OutDirectory = Join-Path $repoRoot (Join-Path "releases" $Version)
}
New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("phantom-release-" + [guid]::NewGuid().ToString("N").Substring(0, 8))

function Write-Step($text) {
    Write-Host ""
    Write-Host "== $text" -ForegroundColor Cyan
}

try {
    if (-not $SkipWindows) {
        Write-Step "Publicando Windows (app único, win-x64 self-contained single-file)..."
        dotnet publish (Join-Path $repoRoot "src\PhantomMonitor.App\PhantomMonitor.App.csproj") `
            -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:Version=$Version -o (Join-Path $stage "win")
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish do Windows falhou." }

        Write-Step "Compilando renderers nativos Windows (x86/x64; requer Visual Studio 2022 e Vulkan SDK)..."
        & (Join-Path $repoRoot "scripts\build-windows-overlay.ps1") -OutDirectory (Join-Path $stage "win\PhantomOverlay")
        if ($LASTEXITCODE -ne 0) { throw "Build dos hooks nativos Windows falhou." }

        # O bundler single-file classifica o PresentMon.exe (binário PE nativo)
        # como biblioteca nativa e o embute (reaparece em %TEMP%\.net no
        # arranque — caminho que o FpsMonitor não usa). O contrato do payload
        # (docs/funções/atualizações.md) é tê-lo SOLTO na pasta do app:
        # garantimos a cópia e limpamos os *.pdb (símbolos não entram no
        # payload).
        Copy-Item (Join-Path $repoRoot "tools\PresentMon.exe") `
            (Join-Path $stage "win\PresentMon.exe") -Force
        Get-ChildItem (Join-Path $stage "win") -Filter "*.pdb" -ErrorAction SilentlyContinue |
            Remove-Item -Force

        Write-Step "Empacotando zip do Windows..."
        if (Test-Path -LiteralPath (Join-Path $stage "win\config.json")) { throw "O pacote não pode conter config.json pessoal." }
        [System.IO.File]::WriteAllText((Join-Path $stage "win\phantom.portable"), "portable")
        $zip = Join-Path $OutDirectory ("PhantomMonitor-{0}-win-x64.zip" -f $Version)
        Compress-Archive -Path (Join-Path $stage "win\*") -DestinationPath $zip -Force
    }

    if (-not $SkipLinux) {
        Write-Step "Publicando Linux (app único, linux-x64 self-contained single-file)..."
        dotnet publish (Join-Path $repoRoot "src\PhantomMonitor.App\PhantomMonitor.App.csproj") `
            -c Release -r linux-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:Version=$Version -o (Join-Path $stage "linux")
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish do Linux falhou." }

        # Hardlink transicional: installs portáteis antigos relançam pelo nome
        # legado "PhantomMonitor.App" no apply_update.sh gerado POR ELES; o nome
        # extra aponta para o mesmo binário ao extraer e custa zero bytes no
        # tar (entrada hardlink). A partir da próxima atualização o nome
        # canônico é "PhantomMonitor" e o shim pode ser aposentado.
        New-Item -ItemType HardLink `
            -Path (Join-Path $stage "linux\PhantomMonitor.App") `
            -Target (Join-Path $stage "linux\PhantomMonitor") | Out-Null

        # Payload limpo no Linux: sem *.pdb (símbolos) e sem PresentMon.exe
        # (ferramenta Windows/ETW que o Include do csproj atravessa aqui).
        Get-ChildItem (Join-Path $stage "linux") -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like "*.pdb" -or $_.Name -eq "PresentMon.exe" } |
            Remove-Item -Force

        Write-Step "Empacotando tarball do Linux..."
        if (Test-Path -LiteralPath (Join-Path $stage "linux\config.json")) { throw "O pacote não pode conter config.json pessoal." }
        [System.IO.File]::WriteAllText((Join-Path $stage "linux\phantom.portable"), "portable")
        $tarball = Join-Path $OutDirectory ("PhantomMonitor-{0}-linux-x64.tar.gz" -f $Version)
        dotnet run --project (Join-Path $repoRoot "tools\ReleasePackager") -c Release -- `
            (Join-Path $stage "linux") $tarball
        if ($LASTEXITCODE -ne 0) { throw "ReleasePackager falhou ao montar o tarball Linux." }

        Write-Host ""
        Write-Host "Opcional (rodar em Linux): AppImage e .deb com os empacotadores" -ForegroundColor DarkGray
        Write-Host "  packaging/linux/appimage.sh <stage-linux> <saida>.AppImage" -ForegroundColor DarkGray
        Write-Host "  packaging/linux/deb.sh <stage-linux> $Version <dir-de-saida>" -ForegroundColor DarkGray
    }

    if ($PackWindowsSetup) {
        Write-Step "Instalador Windows (Inno Setup, per-user)..."
        $candidates = @()
        if (-not [string]::IsNullOrWhiteSpace($IsccPath)) { $candidates += $IsccPath }
        if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe") }
        if ($env:ProgramFiles) { $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe") }
        $iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) {
            Write-Host "ISCC.exe não encontrado; use -IsccPath ou instale o Inno Setup 6. Setup não gerado." -ForegroundColor Yellow
        }
        else {
            & $iscc "/DVersionName=$Version" "/DReleaseDir=$(Join-Path $stage 'win')" "/O$OutDirectory" (Join-Path $repoRoot "packaging\windows\phantom-monitor.iss")
            if ($LASTEXITCODE -ne 0) { throw "ISCC falhou." }
        }
    }

    if ($LayerSoPath) {
        Copy-Item $LayerSoPath (Join-Path $OutDirectory ("PhantomVulkanLayer-{0}-linux.so" -f $Version)) -Force
    }
    else {
        Write-Host ""
        Write-Host "Sem -LayerSoPath: a camada Vulkan não foi anexada. Ela atualiza MANUALMENTE" -ForegroundColor Yellow
        Write-Host "(o usuário roda install_layer.sh da versão nova após atualizar o app)." -ForegroundColor Yellow
    }

    Write-Step "Pacotes gerados"
    $assets = @()
    Get-ChildItem $OutDirectory -File |
        Sort-Object Name |
        ForEach-Object {
            Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
            $assets += $_.Name
        }

    if ($assets.Count -eq 0) {
        throw "Nenhum pacote foi gerado. Verifique as mensagens acima."
    }

    Write-Host ""
    Write-Host "Próximo passo (publicação MANUAL — sem CI):" -ForegroundColor Green
    Write-Host "  1. Revise os pacotes acima (nada disso é commitado)."
    Write-Host "  2. git tag v$Version ; git push --tags"
    Write-Host "  3. De $OutDirectory, rode:"
    $assetList = ($assets | ForEach-Object { '"{0}"' -f $_ }) -join ' '
    Write-Host ("     gh release create v{0} {1} --title ""Phantom Monitor {0}""" -f $Version, $assetList)
    Write-Host "     (ou suba os arquivos em https://github.com/BadTonho/PhantomMonitor/releases/new)"
    Write-Host ""
    Write-Host "  Notas: rascunho (draft) não é visto pelo app; sem o asset no formato esperado," -ForegroundColor DarkGray
    Write-Host "  o updater só notifica com link. A versão compilada já recebeu -p:Version=$Version." -ForegroundColor DarkGray
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
