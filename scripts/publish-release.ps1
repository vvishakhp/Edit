param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet("true", "false")]
    [string]$SelfContained,

    [switch]$SkipNatives,

    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) {
    $OutputDir = Join-Path $Root "artifacts/publish-$Rid"
}
$ArtifactsDir = Join-Path $Root "artifacts"
New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null

function Build-Natives {
    if ($SkipNatives) {
        Write-Host "Skipping Tree-sitter native build."
        return
    }

    if ($Rid -eq "linux-x64") {
        $env:RID = $Rid
        & (Join-Path $Root "scripts/build-tree-sitter-linux.sh")
    }
    else {
        & (Join-Path $Root "scripts/build-tree-sitter.sh") --rid $Rid
    }
}

Build-Natives

$publishArgs = @(
    "publish",
    (Join-Path $Root "src/Edit.App/Edit.App.csproj"),
    "-c", "Release",
    "-r", $Rid,
    "-p:Version=$Version",
    "-p:PublishSingleFile=false",
    "-p:PublishTrimmed=false",
    "-o", $OutputDir
)

if ($SelfContained -eq "true") {
    $publishArgs += @("-p:SelfContained=true", "-p:IncludeNativeLibrariesForSelfExtract=true")
}
else {
    $publishArgs += @("-p:SelfContained=false")
}

Write-Host "Publishing Edit $Version for $Rid (self-contained=$SelfContained)..."
& dotnet @publishArgs

$Mode = if ($SelfContained -eq "true") { "selfcontained" } else { "fxdependent" }
$ArtifactName = "Edit-$Version-$Rid-$Mode.zip"
$ArtifactPath = Join-Path $ArtifactsDir $ArtifactName

if (Test-Path $ArtifactPath) {
    Remove-Item $ArtifactPath -Force
}

Compress-Archive -Path (Join-Path $OutputDir "*") -DestinationPath $ArtifactPath -Force

Write-Host "Release artifact: $ArtifactPath"
Write-Host "ARTIFACT_PATH=$ArtifactPath"
