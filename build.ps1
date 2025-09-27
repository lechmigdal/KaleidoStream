# Get version from the .csproj file
$csproj = Get-Content ./KaleidoStream.csproj
$version = ($csproj | Select-String -Pattern '<AssemblyVersion>(.*?)</AssemblyVersion>').Matches.Groups[1].Value
if (-not $version) {
    Write-Host "Could not find <AssemblyVersion> in KaleidoStream.csproj. Using 1.0.0.0 as fallback."
    $version = "1.0.0.0"
}

# Publish framework-dependent
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o releases\publish-fw

# Publish self-contained
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o releases\publish-sc

# Zip the outputs
$fwZip = "releases\KaleidoStream-$version-win-x64-framework.zip"
$scZip = "releases\KaleidoStream-$version-win-x64-selfcontained.zip"

if (Test-Path $fwZip) { Remove-Item $fwZip }
if (Test-Path $scZip) { Remove-Item $scZip }

Compress-Archive -Path releases\publish-fw\* -DestinationPath $fwZip
Compress-Archive -Path releases\publish-sc\* -DestinationPath $scZip

Write-Host "Created $fwZip and $scZip"