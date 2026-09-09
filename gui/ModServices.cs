using System.Text.Json;

namespace LoloMinecraftGui;

public enum ModSource
{
    Modrinth,
    CurseForge
}

public sealed record ModSearchResult(
    ModSource Source,
    string ProjectId,
    string Name,
    string Description,
    string? Slug,
    long Downloads)
{
    public override string ToString() => $"{Name}  ·  {Downloads:N0} téléchargements\n{Description}";
}

public sealed class ModCatalogService
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public ModCatalogService()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LoloMinecraftLauncher/1.0");
    }

    public async Task<IReadOnlyList<ModSearchResult>> SearchAsync(
        string query,
        string gameVersion,
        GameLoader loader,
        ModSource source,
        string? curseForgeApiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];
        if (loader == GameLoader.Vanilla)
            throw new InvalidOperationException("Choisis Fabric, Forge ou NeoForge pour rechercher des mods.");

        return source == ModSource.Modrinth
            ? await SearchModrinthAsync(query, gameVersion, loader, cancellationToken)
            : await SearchCurseForgeAsync(query, gameVersion, loader, curseForgeApiKey, cancellationToken);
    }

    public async Task InstallAsync(
        ModSearchResult result,
        string gameVersion,
        GameLoader loader,
        string? curseForgeApiKey,
        string targetDirectory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var download = result.Source == ModSource.Modrinth
            ? await GetModrinthDownloadAsync(result.ProjectId, gameVersion, loader, cancellationToken)
            : await GetCurseForgeDownloadAsync(result.ProjectId, gameVersion, loader, result.Slug, curseForgeApiKey, cancellationToken);
        var fileName = SanitizeFileName(download.FileName);
        Directory.CreateDirectory(targetDirectory);
        await DownloadAsync(download.Url, Path.Combine(targetDirectory, fileName), progress, cancellationToken);
    }

    private async Task<IReadOnlyList<ModSearchResult>> SearchModrinthAsync(string query, string gameVersion, GameLoader loader, CancellationToken cancellationToken)
    {
        var strict = await SearchModrinthEndpointAsync(query, gameVersion, loader, true, cancellationToken);
        return strict.Count > 0
            ? strict
            : await SearchModrinthEndpointAsync(query, gameVersion, loader, false, cancellationToken);
    }

    private async Task<IReadOnlyList<ModSearchResult>> SearchModrinthEndpointAsync(
        string query,
        string gameVersion,
        GameLoader loader,
        bool restrictCompatibility,
        CancellationToken cancellationToken)
    {
        var facetGroups = new List<string[]> { new[] { "project_type:mod" } };
        if (restrictCompatibility)
        {
            facetGroups.Add(new[] { $"versions:{gameVersion}" });
            facetGroups.Add(new[] { $"categories:{LoaderFacet(loader)}" });
        }

        var facets = JsonSerializer.Serialize(facetGroups);
        var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}&limit=20&index=downloads";
        using var document = await JsonDocument.ParseAsync(await http.GetStreamAsync(url, cancellationToken), cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("hits").EnumerateArray().Select(hit => new ModSearchResult(
            ModSource.Modrinth,
            hit.GetProperty("project_id").GetString()!,
            hit.GetProperty("title").GetString()!,
            hit.TryGetProperty("description", out var description) ? description.GetString() ?? "" : "",
            hit.TryGetProperty("slug", out var slug) ? slug.GetString() : null,
            hit.TryGetProperty("downloads", out var downloads) ? downloads.GetInt64() : 0)).ToList();
    }

    private async Task<IReadOnlyList<ModSearchResult>> SearchCurseForgeAsync(
        string query,
        string gameVersion,
        GameLoader loader,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("CurseForge demande une clé API saisie dans le champ prévu.");
        var url = $"https://api.curseforge.com/v1/mods/search?gameId=432&searchFilter={Uri.EscapeDataString(query)}&gameVersion={Uri.EscapeDataString(gameVersion)}&modLoaderType={CurseForgeLoader(loader)}&pageSize=20";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.GetProperty("data").EnumerateArray().Select(hit => new ModSearchResult(
            ModSource.CurseForge,
            hit.GetProperty("id").GetInt32().ToString(),
            hit.GetProperty("name").GetString()!,
            hit.GetProperty("summary").GetString() ?? "",
            hit.TryGetProperty("slug", out var slug) ? slug.GetString() : null,
            hit.GetProperty("downloadCount").GetInt32())).ToList();
    }

    private async Task<(string Url, string FileName)> GetModrinthDownloadAsync(string projectId, string gameVersion, GameLoader loader, CancellationToken cancellationToken)
    {
        var gameVersions = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }));
        var loaders = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { LoaderFacet(loader) }));
        var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?game_versions={gameVersions}&loaders={loaders}";
        using var document = await JsonDocument.ParseAsync(await http.GetStreamAsync(url, cancellationToken), cancellationToken: cancellationToken);
        var version = document.RootElement.EnumerateArray().FirstOrDefault();
        if (version.ValueKind == JsonValueKind.Undefined || !version.TryGetProperty("files", out var files))
            throw new InvalidDataException("Aucun fichier compatible trouvé sur Modrinth.");
        var file = files.EnumerateArray().FirstOrDefault(item => item.TryGetProperty("primary", out var primary) && primary.GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined) file = files.EnumerateArray().First();
        return (file.GetProperty("url").GetString()!, file.GetProperty("filename").GetString()!);
    }

    private async Task<(string Url, string FileName)> GetCurseForgeDownloadAsync(
        string projectId,
        string gameVersion,
        GameLoader loader,
        string? slug,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("CurseForge demande une clé API saisie dans le champ prévu.");
        var url = $"https://api.curseforge.com/v1/mods/{Uri.EscapeDataString(projectId)}/files?gameVersion={Uri.EscapeDataString(gameVersion)}&modLoaderType={CurseForgeLoader(loader)}&pageSize=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var file = document.RootElement.GetProperty("data").EnumerateArray().FirstOrDefault();
        if (file.ValueKind == JsonValueKind.Undefined)
            throw new InvalidDataException("Aucun fichier compatible trouvé sur CurseForge.");
        var downloadUrl = file.TryGetProperty("downloadUrl", out var download) && download.ValueKind != JsonValueKind.Null
            ? download.GetString()
            : $"https://www.curseforge.com/minecraft/mc-mods/{slug}/download/{file.GetProperty("id").GetInt32()}/file";
        return (downloadUrl!, file.GetProperty("fileName").GetString()!);
    }

    private async Task DownloadAsync(string url, string target, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        var temporary = target + ".download";
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(temporary);
        var buffer = new byte[128 * 1024];
        long completed = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            completed += read;
            progress?.Report(new DownloadProgress($"Téléchargement du mod · {Path.GetFileName(target)}", completed, total));
        }
        File.Move(temporary, target, true);
    }

    private static string LoaderFacet(GameLoader loader) => loader switch
    {
        GameLoader.Fabric => "fabric",
        GameLoader.Forge => "forge",
        GameLoader.NeoForge => "neoforge",
        _ => "fabric"
    };

    private static int CurseForgeLoader(GameLoader loader) => loader switch
    {
        GameLoader.Forge => 1,
        GameLoader.Fabric => 4,
        GameLoader.NeoForge => 6,
        _ => 0
    };

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character));
    }
}
