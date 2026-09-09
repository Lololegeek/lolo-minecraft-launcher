using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LoloMinecraftGui;

public sealed record CatalogVersion(string Id, string Type, DateTimeOffset ReleaseTime, string Url)
{
    public override string ToString() => $"{Id}  ·  {Type}";
}

public sealed record FabricLoader(string Version, bool Stable)
{
    public override string ToString() => $"{Version}{(Stable ? "  ·  stable" : "")}";
}

public sealed record InstalledGame(string VersionId, string ProfileJsonPath, string? FabricLoaderVersion);

public sealed class VersionCatalogService
{
    private const string MojangManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string FabricLoadersUrl = "https://meta.fabricmc.net/v2/versions/loader";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public VersionCatalogService()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LoloMinecraftLauncher/1.0");
    }

    public async Task<IReadOnlyList<CatalogVersion>> GetMinecraftVersionsAsync(CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await http.GetStreamAsync(MojangManifestUrl, cancellationToken),
            cancellationToken: cancellationToken);

        var versions = new List<CatalogVersion>();
        foreach (var version in document.RootElement.GetProperty("versions").EnumerateArray())
        {
            versions.Add(new CatalogVersion(
                version.GetProperty("id").GetString()!,
                version.GetProperty("type").GetString() ?? "unknown",
                version.GetProperty("releaseTime").GetDateTimeOffset(),
                version.GetProperty("url").GetString()!));
        }

        return versions;
    }

    public async Task<IReadOnlyList<FabricLoader>> GetFabricLoadersAsync(CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await http.GetStreamAsync(FabricLoadersUrl, cancellationToken),
            cancellationToken: cancellationToken);

        return document.RootElement.EnumerateArray()
            .Select(loader => new FabricLoader(
                loader.GetProperty("version").GetString()!,
                loader.GetProperty("stable").GetBoolean()))
            .ToList();
    }

    public async Task<JsonDocument> DownloadJsonAsync(string url, CancellationToken cancellationToken)
    {
        await using var stream = await http.GetStreamAsync(url, cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }
}

public sealed class GameInstaller
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly string gameDirectory;
    private readonly VersionCatalogService catalog;

    public GameInstaller(string gameDirectory, VersionCatalogService catalog)
    {
        this.gameDirectory = gameDirectory;
        this.catalog = catalog;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LoloMinecraftLauncher/1.0");
    }

    public async Task<InstalledGame> InstallAsync(
        CatalogVersion version,
        bool fabric,
        string? fabricLoader,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(gameDirectory);
        var baseDirectory = Path.Combine(gameDirectory, "versions", version.Id);
        Directory.CreateDirectory(baseDirectory);
        var baseJsonPath = Path.Combine(baseDirectory, $"{version.Id}.json");

        progress?.Report($"Lecture des fichiers de {version.Id}…");
        using var baseJson = await catalog.DownloadJsonAsync(version.Url, cancellationToken);
        await File.WriteAllTextAsync(baseJsonPath, baseJson.RootElement.GetRawText(), cancellationToken);
        await DownloadVersionFilesAsync(version.Id, baseJson.RootElement, progress, cancellationToken);

        if (fabric)
        {
            if (string.IsNullOrWhiteSpace(fabricLoader))
                throw new InvalidOperationException("Aucun loader Fabric n'est sélectionné.");

            var fabricId = $"fabric-loader-{fabricLoader}-{version.Id}";
            var fabricDirectory = Path.Combine(gameDirectory, "versions", fabricId);
            Directory.CreateDirectory(fabricDirectory);
            var fabricJsonPath = Path.Combine(fabricDirectory, $"{fabricId}.json");
            var profileUrl = $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(version.Id)}/{Uri.EscapeDataString(fabricLoader)}/profile/json";
            progress?.Report($"Préparation de Fabric {fabricLoader}…");
            using var fabricJson = await catalog.DownloadJsonAsync(profileUrl, cancellationToken);
            await File.WriteAllTextAsync(fabricJsonPath, fabricJson.RootElement.GetRawText(), cancellationToken);
            await DownloadVersionFilesAsync(fabricId, fabricJson.RootElement, progress, cancellationToken);
            return new InstalledGame(fabricId, fabricJsonPath, fabricLoader);
        }

        return new InstalledGame(version.Id, baseJsonPath, null);
    }

    private async Task DownloadVersionFilesAsync(
        string versionId,
        JsonElement json,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (json.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("client", out var client))
        {
            await DownloadDescriptorAsync(
                client,
                Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.jar"),
                "Client",
                progress,
                cancellationToken);
        }

        if (json.TryGetProperty("assetIndex", out var assetIndex))
        {
            var assetId = assetIndex.GetProperty("id").GetString()!;
            var indexPath = Path.Combine(gameDirectory, "assets", "indexes", $"{assetId}.json");
            await DownloadDescriptorAsync(assetIndex, indexPath, "Index des ressources", progress, cancellationToken);
            await DownloadAssetsAsync(indexPath, progress, cancellationToken);
        }

        if (!json.TryGetProperty("libraries", out var libraries))
            return;

        foreach (var library in libraries.EnumerateArray())
        {
            if (!IsAllowedOnWindows(library))
                continue;

            if (library.TryGetProperty("downloads", out var libraryDownloads) && libraryDownloads.TryGetProperty("artifact", out var artifact))
            {
                var path = artifact.GetProperty("path").GetString()!;
                await DownloadDescriptorAsync(
                    artifact,
                    Path.Combine(gameDirectory, "libraries", path),
                    "Bibliothèque",
                    progress,
                    cancellationToken);
            }
            else if (library.TryGetProperty("name", out var mavenName))
            {
                await DownloadMavenLibraryAsync(library, mavenName.GetString()!, progress, cancellationToken);
            }

            if (library.TryGetProperty("downloads", out libraryDownloads) && libraryDownloads.TryGetProperty("classifiers", out var classifiers) &&
                classifiers.TryGetProperty("natives-windows", out var native))
            {
                var path = native.GetProperty("path").GetString()!;
                await DownloadDescriptorAsync(
                    native,
                    Path.Combine(gameDirectory, "libraries", path),
                    "Natif Windows",
                    progress,
                    cancellationToken);
            }
        }
    }

    private async Task DownloadMavenLibraryAsync(
        JsonElement library,
        string mavenName,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var parts = mavenName.Split(':');
        if (parts.Length < 3)
            return;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var fileName = $"{artifact}-{version}.jar";
        var relativePath = $"{group}/{artifact}/{version}/{fileName}";
        var baseUrl = library.TryGetProperty("url", out var url) ? url.GetString() : "https://libraries.minecraft.net/";
        var target = Path.Combine(gameDirectory, "libraries", relativePath.Replace('/', Path.DirectorySeparatorChar));
        await DownloadFileAsync(
            $"{baseUrl!.TrimEnd('/')}/{relativePath}",
            target,
            library.TryGetProperty("sha1", out var sha1) ? sha1.GetString() : null,
            "Bibliothèque Fabric",
            progress,
            cancellationToken);
    }

    private async Task DownloadAssetsAsync(string indexPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var index = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, cancellationToken));
        if (!index.RootElement.TryGetProperty("objects", out var objects))
            return;

        var total = objects.EnumerateObject().Count();
        var current = 0;
        foreach (var item in objects.EnumerateObject())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = item.Value.GetProperty("hash").GetString()!;
            var target = Path.Combine(gameDirectory, "assets", "objects", hash[..2], hash);
            var url = $"https://resources.download.minecraft.net/{hash[..2]}/{hash}";
            await DownloadFileAsync(url, target, hash, $"Ressources {++current}/{total}", progress, cancellationToken);
        }
    }

    private async Task DownloadDescriptorAsync(
        JsonElement descriptor,
        string target,
        string label,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var url = descriptor.GetProperty("url").GetString()!;
        var sha1 = descriptor.TryGetProperty("sha1", out var hash) ? hash.GetString() : null;
        await DownloadFileAsync(url, target, sha1, label, progress, cancellationToken);
    }

    private async Task DownloadFileAsync(
        string url,
        string target,
        string? expectedSha1,
        string label,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (expectedSha1 is not null && File.Exists(target) && await Sha1Async(target, cancellationToken) == expectedSha1)
            return;

        progress?.Report($"Téléchargement · {label}");
        var temporary = target + ".download";
        await using (var input = await http.GetStreamAsync(url, cancellationToken))
        await using (var output = File.Create(temporary))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (expectedSha1 is not null && await Sha1Async(temporary, cancellationToken) != expectedSha1)
        {
            File.Delete(temporary);
            throw new InvalidDataException($"Hash invalide pour {url}.");
        }

        File.Move(temporary, target, true);
    }

    private static async Task<string> Sha1Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsAllowedOnWindows(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules))
            return true;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var matches = !rule.TryGetProperty("os", out var os) ||
                (!os.TryGetProperty("name", out var name) || name.GetString() == "windows");
            if (!matches)
                continue;

            var action = rule.GetProperty("action").GetString();
            if (action == "allow") allowed = true;
            if (action == "disallow") allowed = false;
        }

        return allowed;
    }
}

public sealed class GameLauncher
{
    private readonly string gameDirectory;

    public GameLauncher(string gameDirectory)
    {
        this.gameDirectory = gameDirectory;
    }

    public Process Start(InstalledGame game, string username, int ramGb)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(game.ProfileJsonPath));
        var profile = ResolveProfile(json.RootElement);
        var java = FindJava();
        var natives = Path.Combine(gameDirectory, "natives", Sanitize(game.VersionId));
        Directory.CreateDirectory(natives);
        ExtractNatives(profile.Libraries, natives);

        var classpath = BuildClasspath(profile.Libraries, game.VersionId);
        var startInfo = new ProcessStartInfo
        {
            FileName = java,
            WorkingDirectory = gameDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add($"-Xms1G");
        startInfo.ArgumentList.Add($"-Xmx{Math.Max(1, ramGb)}G");
        startInfo.ArgumentList.Add($"-Djava.library.path={natives}");
        startInfo.ArgumentList.Add("-cp");
        startInfo.ArgumentList.Add(classpath);
        startInfo.ArgumentList.Add(profile.MainClass);

        foreach (var argument in BuildGameArguments(profile, username))
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Java n'a pas pu démarrer.");
        _ = CaptureOutputAsync(process);
        return process;
    }

    private async Task CaptureOutputAsync(Process process)
    {
        try
        {
            await using var log = new StreamWriter(Path.Combine(gameDirectory, "lolo-launcher.log"), true, Encoding.UTF8);
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(output, error);
            await log.WriteLineAsync(output.Result);
            await log.WriteLineAsync(error.Result);
        }
        catch
        {
            // The game process remains the source of truth; logging is best effort.
        }
    }

    private string FindJava()
    {
        var candidates = new[]
        {
            Path.Combine(gameDirectory, "runtime", "bin", "java.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java", "jdk-25", "bin", "java.exe")
        };
        var local = candidates.FirstOrDefault(File.Exists);
        if (local is not null)
            return local;

        var pathJava = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator)
            .Select(path => Path.Combine(path, "java.exe"))
            .FirstOrDefault(File.Exists);
        return pathJava ?? throw new FileNotFoundException("Java est introuvable. Installe Java 17 ou plus récent.");
    }

    private void ExtractNatives(IEnumerable<JsonElement> libraries, string target)
    {
        foreach (var library in libraries)
        {
            if (!IsAllowedOnWindows(library) || !library.TryGetProperty("downloads", out var downloads) ||
                !downloads.TryGetProperty("classifiers", out var classifiers) ||
                !classifiers.TryGetProperty("natives-windows", out var native))
                continue;

            var path = native.GetProperty("path").GetString()!;
            var archive = Path.Combine(gameDirectory, "libraries", path);
            if (File.Exists(archive))
                ZipFile.ExtractToDirectory(archive, target, true);
        }
    }

    private string BuildClasspath(IEnumerable<JsonElement> libraries, string versionId)
    {
        var paths = new List<string>();
        foreach (var library in libraries)
        {
            if (!IsAllowedOnWindows(library))
                continue;

            if (library.TryGetProperty("downloads", out var downloads) && downloads.TryGetProperty("artifact", out var artifact))
            {
                paths.Add(Path.Combine(gameDirectory, "libraries", artifact.GetProperty("path").GetString()!));
            }
            else if (library.TryGetProperty("name", out var name))
            {
                var parts = name.GetString()!.Split(':');
                if (parts.Length >= 3)
                {
                    var relative = $"{parts[0].Replace('.', Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}{parts[1]}{Path.DirectorySeparatorChar}{parts[2]}{Path.DirectorySeparatorChar}{parts[1]}-{parts[2]}.jar";
                    paths.Add(Path.Combine(gameDirectory, "libraries", relative));
                }
            }
        }

        var versionJar = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.jar");
        if (!File.Exists(versionJar))
        {
            var profile = Path.Combine(gameDirectory, "versions", versionId, $"{versionId}.json");
            using var document = JsonDocument.Parse(File.ReadAllText(profile));
            if (document.RootElement.TryGetProperty("inheritsFrom", out var inherits))
            {
                var parent = inherits.GetString()!;
                versionJar = Path.Combine(gameDirectory, "versions", parent, $"{parent}.jar");
            }
        }

        paths.Add(versionJar);
        return string.Join(Path.PathSeparator, paths.Where(File.Exists));
    }

    private IEnumerable<string> BuildGameArguments(ResolvedProfile profile, string username)
    {
        var assets = profile.AssetIndex;
        var uuid = OfflineUuid(username);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${auth_player_name}"] = username,
            ["${version_name}"] = profile.Id,
            ["${game_directory}"] = gameDirectory,
            ["${assets_root}"] = Path.Combine(gameDirectory, "assets"),
            ["${assets_index_name}"] = assets,
            ["${auth_uuid}"] = uuid,
            ["${auth_access_token}"] = "0",
            ["${clientid}"] = "0",
            ["${auth_xuid}"] = "0",
            ["${user_type}"] = "legacy",
            ["${version_type}"] = "release",
            ["${natives_directory}"] = Path.Combine(gameDirectory, "natives", Sanitize(profile.Id))
        };

        if (profile.GameArguments.Count > 0)
        {
            foreach (var argument in profile.GameArguments)
                yield return values.Aggregate(argument, (current, item) => current.Replace(item.Key, item.Value, StringComparison.Ordinal));
            yield break;
        }

        yield return "--username";
        yield return username;
        yield return "--version";
        yield return profile.Id;
        yield return "--gameDir";
        yield return gameDirectory;
        yield return "--assetsDir";
        yield return Path.Combine(gameDirectory, "assets");
        yield return "--assetIndex";
        yield return assets;
        yield return "--uuid";
        yield return uuid;
        yield return "--accessToken";
        yield return "0";
        yield return "--userType";
        yield return "legacy";
        yield return "--versionType";
        yield return "release";
    }

    private ResolvedProfile ResolveProfile(JsonElement json)
    {
        var id = json.GetProperty("id").GetString() ?? "minecraft";
        var mainClass = json.TryGetProperty("mainClass", out var main) ? main.GetString()! : "net.minecraft.client.main.Main";
        var assetIndex = json.TryGetProperty("assetIndex", out var assets) ? assets.GetProperty("id").GetString()! : "legacy";
        var libraries = new List<JsonElement>();
        if (json.TryGetProperty("libraries", out var ownLibraries)) libraries.AddRange(ownLibraries.EnumerateArray().Select(item => item.Clone()));
        var args = ReadGameArguments(json);

        if (json.TryGetProperty("inheritsFrom", out var parentId))
        {
            var parent = parentId.GetString()!;
            var parentPath = Path.Combine(gameDirectory, "versions", parent, $"{parent}.json");
            if (File.Exists(parentPath))
            {
                using var parentJson = JsonDocument.Parse(File.ReadAllText(parentPath));
                var inherited = ResolveProfile(parentJson.RootElement);
                libraries.InsertRange(0, inherited.Libraries);
                if (args.Count == 0) args = inherited.GameArguments;
                if (!json.TryGetProperty("assetIndex", out _)) assetIndex = inherited.AssetIndex;
                if (!json.TryGetProperty("mainClass", out _)) mainClass = inherited.MainClass;
            }
        }

        return new ResolvedProfile(id, mainClass, assetIndex, libraries, args);
    }

    private static List<string> ReadGameArguments(JsonElement json)
    {
        if (json.TryGetProperty("arguments", out var arguments) && arguments.TryGetProperty("game", out var game))
            return game.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToList();
        if (json.TryGetProperty("minecraftArguments", out var legacy))
            return legacy.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        return [];
    }

    private static bool IsAllowedOnWindows(JsonElement library)
    {
        if (!library.TryGetProperty("rules", out var rules)) return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.TryGetProperty("os", out var os) && os.TryGetProperty("name", out var name) && name.GetString() != "windows") continue;
            if (rule.GetProperty("action").GetString() == "allow") allowed = true;
        }
        return allowed;
    }

    private static string OfflineUuid(string username)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"OfflinePlayer:{username}"));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes).ToString();
    }

    private static string Sanitize(string value) => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));

    private sealed record ResolvedProfile(string Id, string MainClass, string AssetIndex, List<JsonElement> Libraries, List<string> GameArguments);
}
