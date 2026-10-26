# Verifies that the public API matches SixLabors.ImageSharp 2.1.13 exactly, apart from the
# intended breaks listed in apicompat-suppressions.xml.
# ApiCompat pairs assemblies by name, so the library is built again under the baseline
# assembly name. Only the strong-name key differs from the baseline (CP0003).
$ErrorActionPreference = 'Stop'

$baselineVersion = '2.1.13'
$output = "$PSScriptRoot/artifacts/apicompat"
$baseline = "$output/baseline"

if (-not (Test-Path "$baseline/lib")) {
    New-Item -ItemType Directory -Force $baseline | Out-Null
    $nupkg = "$output/sixlabors.imagesharp.$baselineVersion.nupkg"
    Invoke-WebRequest "https://api.nuget.org/v3-flatcontainer/sixlabors.imagesharp/$baselineVersion/sixlabors.imagesharp.$baselineVersion.nupkg" -OutFile $nupkg
    Expand-Archive -Path $nupkg -DestinationPath $baseline -Force
}

# Version only needs the same major as the baseline so the assembly version matches (2.0.0.0).
# BaseArtifactsPath moves the output of every project in the build, the library and the
# source generator it references, while each keeps its own obj and bin folders.
dotnet build "$PSScriptRoot/src/ImagePastel/ImagePastel.csproj" -c Release `
    -p:AssemblyName=SixLabors.ImageSharp `
    -p:Version=2.2.0 `
    -p:BaseArtifactsPath="$output/"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$pairs = @(
    @{ Baseline = 'netstandard2.1'; Current = 'netstandard2.1' },
    @{ Baseline = 'netcoreapp3.1'; Current = 'net10.0' }
)

foreach ($pair in $pairs) {
    Write-Host "Comparing $($pair.Baseline) (baseline) with $($pair.Current)"
    dotnet apicompat --noWarn CP0003 --strict-mode `
        --suppression-file "$PSScriptRoot/apicompat-suppressions.xml" `
        -l "$baseline/lib/$($pair.Baseline)/SixLabors.ImageSharp.dll" `
        -r "$output/bin/src/ImagePastel/Release/$($pair.Current)/SixLabors.ImageSharp.dll"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
