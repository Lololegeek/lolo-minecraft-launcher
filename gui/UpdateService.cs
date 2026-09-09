using System.Text.Json;

namespace LoloMinecraftGui;

public sealed record LauncherRelease(Version Version, string DownloadUrl);

public sealed class UpdateService
{
    public const string CurrentVersion = "1.0.4";

    private const string LatestReleaseUrl =
        "https://api.github.com/repos/Lololegeek/lolo-minecraft-launcher/releases/latest";
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public UpdateService()
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("LoloMinecraftLauncher/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<LauncherRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await http.GetStreamAsync(LatestReleaseUrl, cancellationToken),
            cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
        if (!Version.TryParse(tag, out var version) || version <= Version.Parse(CurrentVersion))
            return null;

        var asset = root.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(item => item.GetProperty("name").GetString() == "LoloLauncherSetup.exe");
        if (asset.ValueKind == JsonValueKind.Undefined)
            return null;

        return new LauncherRelease(version, asset.GetProperty("browser_download_url").GetString()!);
    }

    public async Task<string> DownloadSetupAsync(
        LauncherRelease release,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var target = Path.Combine(Path.GetTempPath(), $"LoloLauncherSetup-{release.Version}.exe");
        using var response = await http.GetAsync(
            release.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(target);
        var buffer = new byte[128 * 1024];
        long completed = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            completed += read;
            progress?.Report(new DownloadProgress("Téléchargement de la mise à jour", completed, total));
        }

        return target;
    }
}
