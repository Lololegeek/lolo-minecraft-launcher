$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$minecraftRoot = Join-Path $env:APPDATA '.minecraft'
$staging = Join-Path $projectRoot 'staging'
$publish = Join-Path $projectRoot 'publish'
$archive = Join-Path $projectRoot 'minecraft-26.2-vanilla.zip'
$output = Join-Path (Split-Path -Parent $projectRoot) 'LoloMinecraft26.2.exe'

function Assert-UnderProject([string]$path) {
    $full = [IO.Path]::GetFullPath($path)
    $root = [IO.Path]::GetFullPath($projectRoot) + [IO.Path]::DirectorySeparatorChar
    if (!$full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Chemin de build hors du dossier de travail : $full"
    }
}

Assert-UnderProject $staging
Assert-UnderProject $publish
Assert-UnderProject $archive

if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

function Copy-IntoStage([string]$source, [string]$relative) {
    $destination = Join-Path $staging $relative
    $parent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force $parent | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

function Copy-TreeIntoStage([string]$source, [string]$relative) {
    $destination = Join-Path $staging $relative
    New-Item -ItemType Directory -Force $destination | Out-Null
    Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
}

function Get-LibraryPath($library) {
    if ($library.downloads.artifact.path) { return [string]$library.downloads.artifact.path }
    $parts = ([string]$library.name).Split(':')
    if ($parts.Length -lt 3) { throw "Coordonnées Maven invalides : $($library.name)" }
    $classifier = if ($parts.Length -ge 4) { '-' + $parts[3] } else { '' }
    return (($parts[0] -replace '\.', '/') + '/' + $parts[1] + '/' + $parts[2] + '/' + $parts[1] + '-' + $parts[2] + $classifier + '.jar')
}

function Is-WindowsX64Allowed($library) {
    if (!$library.rules) { return $true }
    $allowed = $false
    foreach ($rule in @($library.rules)) {
        $matches = $true
        if ($rule.os.name -and $rule.os.name -ne 'windows') { $matches = $false }
        if ($rule.os.arch -and $rule.os.arch -eq 'x86') { $matches = $false }
        if ($matches) {
            if ($rule.action -eq 'allow') { $allowed = $true }
            if ($rule.action -eq 'disallow') { $allowed = $false }
        }
    }
    return $allowed
}

$versionJsonPath = Join-Path $minecraftRoot 'versions\26.2\26.2.json'
$version = Get-Content -LiteralPath $versionJsonPath -Raw | ConvertFrom-Json
if ($version.id -ne '26.2' -or $version.javaVersion.component -ne 'java-runtime-epsilon') {
    throw 'Le fichier de version 26.2 attendu est absent ou différent.'
}

Copy-IntoStage (Join-Path $minecraftRoot 'versions\26.2\26.2.jar') 'versions\26.2\26.2.jar'
Copy-IntoStage $versionJsonPath 'versions\26.2\26.2.json'
Copy-IntoStage (Join-Path $minecraftRoot 'assets\indexes\32.json') 'assets\indexes\32.json'

$assetIndex = Get-Content -LiteralPath (Join-Path $minecraftRoot 'assets\indexes\32.json') -Raw | ConvertFrom-Json
foreach ($asset in @($assetIndex.objects.PSObject.Properties)) {
    $hash = [string]$asset.Value.hash
    $source = Join-Path $minecraftRoot ('assets\objects\' + $hash.Substring(0, 2) + '\' + $hash)
    if (!(Test-Path -LiteralPath $source)) { throw "Asset manquant : $hash" }
    Copy-IntoStage $source ('assets\objects\' + $hash.Substring(0, 2) + '\' + $hash)
}

$allLibraries = @($version.libraries)
$classpathLines = [Collections.Generic.List[string]]::new()
foreach ($library in $allLibraries) {
    if (!(Is-WindowsX64Allowed $library)) { continue }
    $path = Get-LibraryPath $library
    $isNative = $path -match '-natives-windows\.jar$'
    $source = Join-Path $minecraftRoot ('libraries\' + ($path -replace '/', '\'))
    if (!(Test-Path -LiteralPath $source)) { throw "Bibliothèque manquante : $path" }
    Copy-IntoStage $source ('libraries\' + ($path -replace '/', '\'))
    if (!$isNative) { $classpathLines.Add(('libraries/' + $path)) }
}
$classpathLines.Add('versions/26.2/26.2.jar')
[IO.File]::WriteAllLines((Join-Path $staging 'classpath.txt'), $classpathLines, [Text.UTF8Encoding]::new($false))

$java = Get-ChildItem 'C:\Users\Loévan\AppData\Local\Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime\java-runtime-epsilon\windows-x64\java-runtime-epsilon\bin\java.exe' -ErrorAction SilentlyContinue
if (!$java) { throw 'Le runtime Java 25 du launcher Minecraft est introuvable.' }
$javaRoot = Split-Path (Split-Path $java.FullName -Parent) -Parent
Copy-TreeIntoStage $javaRoot 'runtime'

if (Test-Path -LiteralPath (Join-Path $projectRoot 'publish')) { Remove-Item -LiteralPath $publish -Recurse -Force }
dotnet publish (Join-Path $projectRoot 'Launcher.csproj') -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $publish
$stub = Join-Path $publish 'LoloMinecraftLauncher.exe'
if (!(Test-Path -LiteralPath $stub)) { throw 'Publication du launcher échouée.' }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $staging,
    $archive,
    [IO.Compression.CompressionLevel]::Fastest,
    $false)
if (!(Test-Path -LiteralPath $archive)) { throw 'Compression du bundle échouée.' }

$magic = [Text.Encoding]::ASCII.GetBytes('VALORIA26PAYLOAD')
$archiveInfo = Get-Item -LiteralPath $archive
$stubStream = [IO.File]::Open($stub, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
$outputStream = [IO.File]::Open($output, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $stubStream.CopyTo($outputStream)
    $archiveStream = [IO.File]::OpenRead($archive)
    try { $archiveStream.CopyTo($outputStream) } finally { $archiveStream.Dispose() }
    $outputStream.Write($magic, 0, $magic.Length)
    $lengthBytes = [BitConverter]::GetBytes([Int64]$archiveInfo.Length)
    $outputStream.Write($lengthBytes, 0, $lengthBytes.Length)
} finally {
    $stubStream.Dispose()
    $outputStream.Dispose()
}

$size = (Get-Item -LiteralPath $output).Length
Write-Host ("Launcher créé : {0} ({1:N0} Mo)" -f $output, ($size / 1MB))
