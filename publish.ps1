param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'JustDjvu\JustDjvu.csproj'
$output = Join-Path $PSScriptRoot "dist\JustDjVu-$Runtime"
$archive = "$output.zip"
$temporaryArchive = "$output.tmp.zip"

$arguments = @(
    'publish',
    $project,
    '-c', 'Release',
    '-r', $Runtime,
    '-o', $output,
    "-p:SelfContained=$(!$FrameworkDependent)",
    '-p:PublishSingleFile=false',
    '-p:DebugType=none',
    '-p:DebugSymbols=false'
)

dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$distributionDocuments = @(
    'README.md',
    'LICENSE',
    'THIRD-PARTY-NOTICES.md'
)

foreach ($document in $distributionDocuments) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $document) -Destination $output -Force
}

try {
    if (Test-Path -LiteralPath $temporaryArchive) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }

    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $temporaryArchive
    Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryArchive) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }
}

Write-Host "JustDjVu published to: $output"
Write-Host "Archive created at: $archive"
