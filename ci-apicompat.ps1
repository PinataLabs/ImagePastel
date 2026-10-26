# Verifies that the public API matches the baseline commit exactly, apart from the
# intended breaks listed in apicompat-suppressions.xml.
# The baseline is the library built from $baselineRef, the first commit under the
# PinataLabs.ImagePastel namespace. Move $baselineRef to a release tag once there is one.
# It needs the full git history, so check out with fetch-depth 0 in CI.
$ErrorActionPreference = 'Stop'

$baselineRef = 'd169f03d9'
$output = "$PSScriptRoot/artifacts/apicompat"
$baselineSource = "$output/baseline-source"
$baselineOutput = "$output/baseline"
$currentOutput = "$output/current"

if (Test-Path $baselineSource) { Remove-Item -Recurse -Force $baselineSource }
New-Item -ItemType Directory -Force $baselineSource | Out-Null
$archive = "$output/baseline-source.tar"
git -C $PSScriptRoot archive --format=tar -o $archive $baselineRef
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
tar -xf $archive -C $baselineSource
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# BaseArtifactsPath moves the output of every project in the build, the library and the
# source generator it references, while each keeps its own obj and bin folders.
# Version only needs to match between the two builds.
foreach ($build in @(
    @{ Source = $baselineSource; Artifacts = $baselineOutput },
    @{ Source = $PSScriptRoot; Artifacts = $currentOutput })) {
    dotnet build "$($build.Source)/src/ImagePastel/ImagePastel.csproj" -c Release `
        -p:Version=0.0.1 `
        -p:BaseArtifactsPath="$($build.Artifacts)/"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

foreach ($tfm in @('netstandard2.1', 'net10.0')) {
    Write-Host "Comparing $tfm"
    dotnet apicompat --strict-mode `
        --suppression-file "$PSScriptRoot/apicompat-suppressions.xml" `
        -l "$baselineOutput/bin/src/ImagePastel/Release/$tfm/PinataLabs.ImagePastel.dll" `
        -r "$currentOutput/bin/src/ImagePastel/Release/$tfm/PinataLabs.ImagePastel.dll"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
