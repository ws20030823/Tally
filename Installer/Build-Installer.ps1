[CmdletBinding()]
param([string]$IsccPath)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'TaskDesk.csproj'
[xml]$projectXml = Get-Content -LiteralPath $projectFile -Raw
$appVersion = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($appVersion)) { throw 'TaskDesk.csproj must define Version.' }
$publishDir = Join-Path $projectRoot 'Artifacts\publish\win-x64'
$installerOutputDir = Join-Path $projectRoot 'Artifacts\installer'

if (-not $IsccPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $IsccPath = $compiler.Source }
}
if (-not $IsccPath) {
    $registryRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    foreach ($entry in @(Get-ItemProperty $registryRoots -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like 'Inno Setup*' -and $_.InstallLocation })) {
        $candidate = Join-Path $entry.InstallLocation 'ISCC.exe'
        if (Test-Path -LiteralPath $candidate) { $IsccPath = $candidate; break }
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 6.3+ or pass -IsccPath with the full path to ISCC.exe.'
}

# A unique publish folder prevents obsolete files from leaking into a later installer.
# Do not recursively delete a user-supplied directory.
$publishDir = Join-Path $publishDir ([guid]::NewGuid().ToString('N'))
& dotnet publish $projectFile -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None `
    -p:DebugSymbols=false -p:NuGetAudit=false -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.txt') -Destination $publishDir
foreach ($requiredFile in @('TaskDesk.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'libSkiaSharp.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $requiredFile))) {
        throw "Incomplete self-contained publish: $requiredFile is missing."
    }
}
& $IsccPath "/DAppVersion=$appVersion" "/DPublishDir=$publishDir" `
    "/DInstallerOutputDir=$installerOutputDir" (Join-Path $PSScriptRoot 'Tally.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$installer = Join-Path $installerOutputDir "Tally-Setup-$appVersion-win-x64.exe"
Get-FileHash -LiteralPath $installer -Algorithm SHA256 |
    ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_.Path))" } |
    Set-Content -LiteralPath ($installer + '.sha256') -Encoding ascii
Write-Host "Installer: $installer"
