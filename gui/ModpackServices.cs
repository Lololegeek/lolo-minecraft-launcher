using System.IO.Compression;
using System.Text.Json;

namespace LoloMinecraftGui;

public sealed record ImportedModpack(
    string Name,
    string Directory,
    string MinecraftVersion,
    GameLoader Loader,
    string LoaderVersion,
    int ModCount);

public sealed class ModpackInstaller
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public ModpackInstaller()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LoloMinecraftLauncher/1.0");
    }

    public async Task<ImportedModpack?> LoadExistingAsync(
        string profileDirectory,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(profileDirectory, "lolo-modpack.json");
        if (!File.Exists(manifestPath))
            return null;

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        var minecraft = document.RootElement.GetProperty("minecraft");
        var minecraftVersion = minecraft.GetProperty("version").GetString()!;
        var loaderId = minecraft.GetProperty("modLoaders").EnumerateArray()
            .First(loader => !loader.TryGetProperty("primary", out var primary) || primary.GetBoolean())
            .GetProperty("id").GetString()!;
        var (loader, loaderVersion) = ParseLoader(loaderId);
        var name = document.RootElement.TryGetProperty("name", out var nameProperty)
            ? nameProperty.GetString() ?? Path.GetFileName(profileDirectory)
            : Path.GetFileName(profileDirectory);
        var modCount = Directory.Exists(Path.Combine(profileDirectory, "mods"))
            ? Directory.EnumerateFiles(Path.Combine(profileDirectory, "mods"), "*.jar").Count()
            : 0;
        return new ImportedModpack(name, profileDirectory, minecraftVersion, loader, loaderVersion, modCount);
    }

    public async Task<ImportedModpack> ImportAsync(
        string zipPath,
        string profilesDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Le ZIP du modpack est introuvable.", zipPath);

        using var archive = ZipFile.OpenRead(zipPath);
        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Ce ZIP ne contient pas de manifest.json CurseForge.");
        using var manifestDocument = await JsonDocument.ParseAsync(
            manifestEntry.Open(),
            cancellationToken: cancellationToken);
        var manifest = manifestDocument.RootElement;
        if (!manifest.TryGetProperty("minecraft", out var minecraft))
            throw new InvalidDataException("Le manifest du modpack est invalide.");

        var minecraftVersion = minecraft.GetProperty("version").GetString()
            ?? throw new InvalidDataException("La version Minecraft du modpack est absente.");
        var loaderId = minecraft.GetProperty("modLoaders").EnumerateArray()
            .FirstOrDefault(loader => loader.TryGetProperty("primary", out var primary) && primary.GetBoolean())
            .GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(loaderId))
            throw new InvalidDataException("Le loader principal du modpack est absent.");

        var (loader, loaderVersion) = ParseLoader(loaderId);
        var name = manifest.TryGetProperty("name", out var nameProperty)
            ? nameProperty.GetString() ?? Path.GetFileNameWithoutExtension(zipPath)
            : Path.GetFileNameWithoutExtension(zipPath);
        var profileDirectory = Path.Combine(profilesDirectory, SanitizeDirectoryName(name));
        Directory.CreateDirectory(profileDirectory);
        progress?.Report($"Import de {name} · Minecraft {minecraftVersion} · {loader}…");

        var overrides = manifest.TryGetProperty("overrides", out var overridesProperty)
            ? overridesProperty.GetString() ?? "overrides"
            : "overrides";
        ExtractOverrides(archive, overrides, profileDirectory);

        var modCount = 0;
        if (manifest.TryGetProperty("files", out var files))
        {
            foreach (var file in files.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.TryGetProperty("required", out var required) && !required.GetBoolean())
                    continue;

                var projectId = file.GetProperty("projectID").GetInt32();
                var fileId = file.GetProperty("fileID").GetInt32();
                var metadata = await GetFileMetadataAsync(projectId, fileId, cancellationToken);
                var targetDirectory = metadata.FileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(profileDirectory, "mods")
                    : Path.Combine(profileDirectory, "resourcepacks");
                await DownloadFileAsync(metadata, targetDirectory, progress, cancellationToken);
                modCount++;
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(profileDirectory, "lolo-modpack.json"),
            manifest.GetRawText(),
            cancellationToken);
        return new ImportedModpack(name, profileDirectory, minecraftVersion, loader, loaderVersion, modCount);
    }

    private async Task<CurseForgeFile> GetFileMetadataAsync(
        int projectId,
        int fileId,
        CancellationToken cancellationToken)
    {
        var url = $"https://www.curseforge.com/api/v1/mods/{projectId}/files/{fileId}";
        using var document = await JsonDocument.ParseAsync(
            await http.GetStreamAsync(url, cancellationToken),
            cancellationToken: cancellationToken);
        var data = document.RootElement.GetProperty("data");
        var fileName = data.GetProperty("fileName").GetString()
            ?? throw new InvalidDataException($"Le nom du fichier CurseForge {fileId} est absent.");
        var fileLength = data.TryGetProperty("fileLength", out var length) ? length.GetInt64() : 0;
        var downloadUrl = data.TryGetProperty("downloadUrl", out var directUrl) && directUrl.ValueKind != JsonValueKind.Null
            ? directUrl.GetString()
            : null;
        downloadUrl ??= $"https://edge.forgecdn.net/files/{fileId / 1000}/{fileId % 1000:D3}/{Uri.EscapeDataString(fileName)}";
        return new CurseForgeFile(fileName, fileLength, downloadUrl);
    }

    private async Task DownloadFileAsync(
        CurseForgeFile file,
        string targetDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(targetDirectory);
        var target = Path.Combine(targetDirectory, SanitizeFileName(file.FileName));
        if (file.Length > 0 && File.Exists(target) && new FileInfo(target).Length == file.Length)
            return;

        var temporary = target + ".download";
        using var response = await http.GetAsync(file.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? file.Length;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(temporary);
        var buffer = new byte[128 * 1024];
        long completed = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            completed += read;
            progress?.Report(new DownloadProgress($"Modpack · {file.FileName}", completed, total > 0 ? total : null));
        }

        File.Move(temporary, target, true);
    }

    private static void ExtractOverrides(ZipArchive archive, string overrides, string profileDirectory)
    {
        var prefix = overrides.Trim('/').Replace('\\', '/') + "/";
        var root = Path.GetFullPath(profileDirectory) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            var relative = entry.FullName[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relative))
                continue;
            var destination = Path.GetFullPath(Path.Combine(profileDirectory, relative));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Le modpack contient un chemin d'override invalide.");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = File.Create(destination);
            source.CopyTo(target);
        }
    }

    private static (GameLoader Loader, string Version) ParseLoader(string loaderId)
    {
        var separator = loaderId.IndexOf('-');
        var type = separator > 0 ? loaderId[..separator] : loaderId;
        var version = separator > 0 ? loaderId[(separator + 1)..] : "";
        return type.ToLowerInvariant() switch
        {
            "fabric" => (GameLoader.Fabric, version),
            "fabric_loader" => (GameLoader.Fabric, version),
            "neoforge" => (GameLoader.NeoForge, version),
            "forge" => (GameLoader.Forge, version),
            _ => throw new NotSupportedException($"Loader de modpack non supporté : {loaderId}")
        };
    }

    private static string SanitizeDirectoryName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "modpack" : sanitized;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }

    private sealed record CurseForgeFile(string FileName, long Length, string DownloadUrl);
}
