$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$guiPublish = Join-Path (Split-Path -Parent $projectRoot) 'LoloMinecraftGUI'
$package = Join-Path (Split-Path -Parent $projectRoot) 'LoloLauncher-Windows-v1.0.4.zip'
$setupPublish = Join-Path $projectRoot 'setup-publish'
$output = Join-Path (Split-Path -Parent $projectRoot) 'LoloLauncherSetup.exe'

foreach ($path in @($guiPublish)) {
    if (!(Test-Path -LiteralPath $path)) { throw "Fichier de release manquant : $path" }
}

if (Test-Path -LiteralPath $package) { Remove-Item -LiteralPath $package -Force }
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }

$stage = Join-Path ([IO.Path]::GetTempPath()) 'lolo-launcher-package'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    Get-ChildItem -LiteralPath $guiPublish -Force | Copy-Item -Destination $stage -Recurse -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $stage,
        $package,
        [IO.Compression.CompressionLevel]::Fastest,
        $false)

    dotnet publish (Join-Path $projectRoot 'Setup.csproj') -c Release -r win-x64 --self-contained true -o $setupPublish
    $stub = Join-Path $setupPublish 'LoloLauncherSetup.exe'
    if (!(Test-Path -LiteralPath $stub)) { throw 'Publication du setup échouée.' }

    $magic = [Text.Encoding]::ASCII.GetBytes('LOLOLAUNCHERSETUP')
    $packageInfo = Get-Item -LiteralPath $package
    $stubStream = [IO.File]::OpenRead($stub)
    $outputStream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stubStream.CopyTo($outputStream)
        $packageStream = [IO.File]::OpenRead($package)
        try { $packageStream.CopyTo($outputStream) } finally { $packageStream.Dispose() }
        $outputStream.Write($magic, 0, $magic.Length)
        $lengthBytes = [BitConverter]::GetBytes([Int64]$packageInfo.Length)
        $outputStream.Write($lengthBytes, 0, $lengthBytes.Length)
    } finally {
        $stubStream.Dispose()
        $outputStream.Dispose()
    }
} finally {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
}

$size = (Get-Item -LiteralPath $output).Length
Write-Host ("Setup créé : {0} ({1:N0} Mo)" -f $output, ($size / 1MB))
