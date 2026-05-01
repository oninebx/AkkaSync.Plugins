param(
    [string]$OutputDir = "./output",
    [string]$ReleaseDir = "./release",
    [string]$LocalDeployDir = "",
    [string]$LocalNugetSource = "C:\Development\LocalNuget"
)

# 设置控制台为 UTF-8，避免 emoji 或中文乱码
# [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "----------------------------------------"
Write-Host "AkkaSync Plugin Builder (PowerShell)"
Write-Host "Output Dir: $OutputDir"
Write-Host "Release Dir: $ReleaseDir"
Write-Host "Local Deploy Dir: $(if ($LocalDeployDir) { $LocalDeployDir } else { '<none>' })"
Write-Host "Local NuGet Source: $LocalNugetSource"
Write-Host "----------------------------------------"

# ================================
# Prepare directories
# ================================
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
New-Item -ItemType Directory -Force -Path $ReleaseDir | Out-Null

if ($LocalDeployDir) {
    New-Item -ItemType Directory -Force -Path $LocalDeployDir | Out-Null
}

# ================================
# Build plugins
# ================================
$registry = @()
Get-ChildItem -Path "src" -Directory | ForEach-Object {

    $pluginPath = $_.FullName
    $pluginName = $_.Name
    $manifestPath = Join-Path $pluginPath "manifest.json"

    if (Test-Path $manifestPath) {

        $manifest = Get-Content $manifestPath | ConvertFrom-Json
        $enabled = if ($manifest.enabled -ne $null) { $manifest.enabled } else { $true }

        if ($enabled) {
            $id = $manifest.id
            $version = $manifest.version
            $provider = $manifest.provider

            Write-Host ""
            Write-Host "➡️ Building $id v$version"

            $pluginOutput = Join-Path $OutputDir $pluginName

            # Clean previous build
            if (Test-Path $pluginOutput) {
                Remove-Item $pluginOutput -Recurse -Force
            }

            # ================================
            # Restore from Local NuGet
            # ================================
            Write-Host "🔄 Restoring $pluginName from local NuGet source..."
            dotnet restore $pluginPath --source -c Debug $LocalNugetSource --no-cache

            # ================================
            # Publish
            # ================================
            dotnet publish $pluginPath `
                -c Debug `
                -o $pluginOutput

            # Copy manifest
            Copy-Item $manifestPath $pluginOutput

            # ================================
            # Zip package
            # ================================
            $zipFile = Join-Path $ReleaseDir "$id.zip"
            if (Test-Path $zipFile) {
                Remove-Item $zipFile -Force
            }

            Compress-Archive -Path "$pluginOutput\*" -DestinationPath $zipFile

            Write-Host "✅ Packaged: $zipFile"

            # ================================
            # Copy to local deploy dir
            # ================================
            if ($LocalDeployDir) {
                $dest = Join-Path $LocalDeployDir "$id.zip"
                Copy-Item $zipFile $dest -Force
                Write-Host "📦 Copied to local: $dest"
            }

            # ================================
            # Add to registry
            # ================================
            # Compute SHA256
            $sha256 = Get-FileHash -Algorithm SHA256 $zipFile
            $url = "file:///$($zipFile -replace '\\','/')"

            $registry += [PSCustomObject]@{
                id = $id
                version = $version
                url = $url
                provider = $provider
                checksum = "sha256:$($sha256.Hash)"
            }

        } else {
            Write-Host "⏭️ Skipping ${pluginName}: disabled"
        }

    } else {
        Write-Host "⏭️ Skipping ${pluginName}: manifest.json not found"
    }
}

# ================================
# Generate registry.json
# ================================
$registryObj = [PSCustomObject]@{
    releaseTag = (Get-Date -Format "yyyy-MM-dd-HH-mm-ss")
    plugins = $registry
}

$registryPath = Join-Path "." "registry.json"
$registryObj | ConvertTo-Json -Depth 3 -Compress | Set-Content $registryPath -Encoding UTF8

Write-Host ""
Write-Host "📜 registry.json generated at $registryPath"

Write-Host ""
Write-Host "🎉 Done building plugins."