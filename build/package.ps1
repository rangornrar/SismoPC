# Construit l'installeur SismoPC : publication des trois programmes puis Inno Setup.
# Usage : pwsh build/package.ps1 -Version 0.1.0
param(
    [Parameter(Mandatory = $true)][string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publish = Join-Path $root 'publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

$common = @('--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true', "-p:Version=$Version")

# Service et Notifier partagent un dossier (mêmes dépendances de base) ; l'interface a le sien (Windows App SDK).
dotnet publish src/SismoPC.Service/SismoPC.Service.csproj @common --output publish/service
if ($LASTEXITCODE -ne 0) { throw 'Échec de la publication du service' }
dotnet publish src/SismoPC.Notifier/SismoPC.Notifier.csproj @common --output publish/service
if ($LASTEXITCODE -ne 0) { throw 'Échec de la publication du Notifier' }
dotnet publish src/SismoPC.App/SismoPC.App.csproj @common --output publish/app
if ($LASTEXITCODE -ne 0) { throw 'Échec de la publication de l''interface' }

# Texte de la licence MPL-2.0 de LibreHardwareMonitorLib, joint à l'installeur.
Invoke-WebRequest -Uri 'https://www.mozilla.org/media/MPL/2.0/index.txt' -OutFile (Join-Path $publish 'MPL-2.0.txt')

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    choco install innosetup -y --no-progress
}

& $iscc "/DAppVersion=$Version" '/DPublishDir=..\publish' 'installer\SismoPC.iss'
if ($LASTEXITCODE -ne 0) { throw 'Échec de la compilation de l''installeur' }

$setup = Get-ChildItem artifacts -Filter "SismoPC-$Version-setup-x64.exe" | Select-Object -First 1
$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $($setup.Name)" | Out-File -FilePath (Join-Path 'artifacts' 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Installeur : $($setup.FullName) ($([math]::Round($setup.Length / 1MB, 1)) Mo)"
Write-Host "SHA-256 : $hash"
