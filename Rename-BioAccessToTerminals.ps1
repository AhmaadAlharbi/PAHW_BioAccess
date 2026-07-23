param(
    [string]$OldName = "BioAccess",
    [string]$NewName = "Terminals",
    [string]$ProjectFolderOld = "BioAccess",
    [string]$ProjectFolderNew = "Terminals",
    [string]$ProjectFileOld = "BioAccess.csproj",
    [string]$ProjectFileNew = "Terminals.csproj"
)

$ErrorActionPreference = "Stop"

Write-Warning "Back up this project before running the script. It will rename files and folders and replace text recursively."

$root = (Get-Location).Path

function Replace-InFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$OldValue,
        [Parameter(Mandatory = $true)]
        [string]$NewValue
    )

    $content = Get-Content -LiteralPath $Path -Raw
    if ($null -eq $content -or -not $content.Contains($OldValue)) {
        return
    }

    $updated = $content.Replace($OldValue, $NewValue)
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText($Path, $updated, [System.Text.Encoding]::UTF8)
        Write-Host "Updated: $Path"
    }
}

$solutionCandidate = @(
    Join-Path $root "$OldName.sln"
    Join-Path $root "$OldName.slnx"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if (-not $solutionCandidate) {
    throw "Could not find '$OldName.sln' or '$OldName.slnx' in '$root'."
}

$solutionExtension = [System.IO.Path]::GetExtension($solutionCandidate)
$solutionTarget = Join-Path $root "$NewName$solutionExtension"

$projectFolderPath = Join-Path $root $ProjectFolderOld
if (-not (Test-Path -LiteralPath $projectFolderPath -PathType Container)) {
    throw "Could not find project directory '$projectFolderPath'. Update -ProjectFolderOld if your folder name differs."
}

$projectFilePath = Join-Path $projectFolderPath $ProjectFileOld
if (-not (Test-Path -LiteralPath $projectFilePath -PathType Leaf)) {
    throw "Could not find project file '$projectFilePath'. Update -ProjectFileOld if your project file name differs."
}

$filesToUpdate = Get-ChildItem -Path $root -Recurse -File -Include *.cs,*.csproj,*.sln,*.slnx,*.cshtml,*.json |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj)[\\/]'
    }

foreach ($file in $filesToUpdate) {
    Replace-InFile -Path $file.FullName -OldValue $OldName -NewValue $NewName
}

if ($projectFilePath -ne (Join-Path $projectFolderPath $ProjectFileNew)) {
    Rename-Item -LiteralPath $projectFilePath -NewName $ProjectFileNew
    $projectFilePath = Join-Path $projectFolderPath $ProjectFileNew
    Write-Host "Renamed project file to: $projectFilePath"
}

if ($solutionCandidate -ne $solutionTarget) {
    Rename-Item -LiteralPath $solutionCandidate -NewName ([System.IO.Path]::GetFileName($solutionTarget))
    Write-Host "Renamed solution file to: $solutionTarget"
}

$projectFolderTarget = Join-Path $root $ProjectFolderNew
if ($projectFolderPath -ne $projectFolderTarget) {
    Rename-Item -LiteralPath $projectFolderPath -NewName $ProjectFolderNew
    Write-Host "Renamed project directory to: $projectFolderTarget"
}

Write-Host "Rename completed."
