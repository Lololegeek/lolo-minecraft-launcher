using System.Diagnostics;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LoloMinecraftGui;

public sealed partial class MainWindow : Window
{
    private static readonly string GameDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".lolo-mc");
    private readonly VersionCatalogService catalog = new();
    private readonly GameInstaller installer;
    private readonly GameLauncher gameLauncher;
    private readonly ModCatalogService modCatalog = new();
    private List<CatalogVersion> catalogVersions = [];
    private List<FabricLoader> fabricLoaders = [];
    private List<string> forgeVersions = [];
    private List<string> neoForgeVersions = [];

    private TextBox usernameBox = null!;
    private ComboBox ramBox = null!;
    private ComboBox versionBox = null!;
    private ComboBox loaderBox = null!;
    private ComboBox loaderVersionBox = null!;
    private Button launchButton = null!;
    private Button skinButton = null!;
    private TextBlock launchProgress = null!;
    private Grid downloadProgressContainer = null!;
    private Border downloadProgressFill = null!;
    private TextBlock statusText = null!;
    private TextBlock skinStatus = null!;
    private TextBox modSearchBox = null!;
    private ListView modResults = null!;
    private Button modSearchButton = null!;
    private Button modInstallButton = null!;

    public MainWindow()
    {
        installer = new GameInstaller(GameDataDirectory, catalog);
        gameLauncher = new GameLauncher(GameDataDirectory);
        BuildInterface();
        _ = LoadCatalogAsync();
    }

    private void BuildInterface()
    {
        var titleBar = new Grid
        {
            Height = 52,
            Padding = new Thickness(22, 0, 18, 0),
            Background = Brush("#15121E")
        };
        var titleContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 10
        };
        titleContent.Children.Add(new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(7),
            Background = Brush("#8B5CF6"),
            Child = new TextBlock
            {
                Text = "L",
                Foreground = Brush("#FFFFFF"),
                FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        });
        titleContent.Children.Add(Text("LOLO", 13, true, "#FFFFFF"));
        titleContent.Children.Add(Text("/", 13, false, "#A8A3B7"));
        titleContent.Children.Add(Text("Minecraft launcher", 13, false, "#A8A3B7"));
        titleBar.Children.Add(titleContent);

        var root = new Grid { Background = Brush("#0F0D14") };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(titleBar);

        var content = new StackPanel
        {
            MaxWidth = 650,
            Spacing = 0,
            Padding = new Thickness(46, 32, 46, 40),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(content, 1);

        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(0, 0, 0, 22) };
        var headingLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        headingLine.Children.Add(Text("Minecraft 26.2", 32, true, "#FFFFFF"));
        headingLine.Children.Add(new Border
        {
            Background = Brush("#2A203C"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 4, 9, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Child = Text("VANILLA", 11, true, "#C4B5FD")
        });
        heading.Children.Add(headingLine);
        heading.Children.Add(Text("Une session propre, hors-ligne, prête à jouer.", 15, false, "#A8A3B7"));
        content.Children.Add(heading);

        var panel = new Border
        {
            Background = Brush("#171421"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            BorderBrush = Brush("#2C263B"),
            BorderThickness = new Thickness(1)
        };
        var form = new StackPanel { Spacing = 18 };
        form.Children.Add(Text("SESSION", 11, true, "#C4B5FD"));

        versionBox = new ComboBox
        {
            Header = "Version Minecraft",
            PlaceholderText = "Chargement du catalogue…",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        versionBox.Items.Add("Chargement du catalogue…");
        versionBox.SelectedIndex = 0;
        versionBox.SelectionChanged += VersionBox_Changed;
        form.Children.Add(versionBox);

        loaderBox = new ComboBox
        {
            Header = "Loader",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var loader in new[] { "Vanilla", "Fabric", "Forge", "NeoForge" })
            loaderBox.Items.Add(loader);
        loaderBox.SelectedIndex = 0;
        loaderBox.SelectionChanged += LoaderBox_Changed;
        form.Children.Add(loaderBox);

        loaderVersionBox = new ComboBox
        {
            Header = "Version du loader",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        form.Children.Add(loaderVersionBox);

        usernameBox = new TextBox
        {
            Header = "Pseudo hors-ligne",
            PlaceholderText = "Entre ton pseudo",
            MaxLength = 16
        };
        form.Children.Add(usernameBox);

        var ramGrid = new Grid();
        ramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ramText = new StackPanel { Spacing = 4 };
        ramText.Children.Add(Text("Mémoire allouée", 14, false, "#FFFFFF"));
        ramText.Children.Add(Text("Réserve-la selon la mémoire disponible sur ton PC.", 12, false, "#A8A3B7", true));
        ramGrid.Children.Add(ramText);

        ramBox = new ComboBox { Width = 108, HorizontalAlignment = HorizontalAlignment.Right, SelectedIndex = 1 };
        foreach (var value in new[] { "2 Go", "4 Go", "6 Go", "8 Go", "12 Go" })
            ramBox.Items.Add(value);
        Grid.SetColumn(ramBox, 1);
        ramGrid.Children.Add(ramBox);
        form.Children.Add(ramGrid);

        launchButton = new Button
        {
            Content = "Lancer Minecraft",
            Height = 46,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brush("#8B5CF6"),
            Foreground = Brush("#FFFFFF")
        };
        launchButton.Click += LaunchButton_Click;
        form.Children.Add(launchButton);

        launchProgress = new TextBlock
        {
            Text = "Préparation…",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Brush("#C4B5FD")
        };
        form.Children.Add(launchProgress);

        downloadProgressContainer = new Grid
        {
            Height = 6,
            Visibility = Visibility.Collapsed
        };
        downloadProgressContainer.Children.Add(new Border
        {
            Background = Brush("#2C263B"),
            CornerRadius = new CornerRadius(3)
        });
        downloadProgressFill = new Border
        {
            Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush("#8B5CF6"),
            CornerRadius = new CornerRadius(3)
        };
        downloadProgressContainer.Children.Add(downloadProgressFill);
        downloadProgressContainer.SizeChanged += (_, _) => UpdateDownloadProgress(0, null);
        form.Children.Add(downloadProgressContainer);

        skinButton = new Button
        {
            Content = "Choisir un skin PNG local",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        skinButton.Click += SkinButton_Click;
        form.Children.Add(skinButton);
        skinStatus = Text("Aucun skin local sélectionné.", 12, false, "#A8A3B7", true);
        form.Children.Add(skinStatus);

        statusText = Text(string.Empty, 12, false, "#C4B5FD", true);
        statusText.Visibility = Visibility.Collapsed;
        form.Children.Add(statusText);
        panel.Child = form;
        content.Children.Add(panel);

        var modsPanel = new Border
        {
            Background = Brush("#171421"),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(24),
            BorderBrush = Brush("#2C263B"),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 18, 0, 0)
        };
        var modsForm = new StackPanel { Spacing = 12 };
        modsForm.Children.Add(Text("BIBLIOTHÈQUE DE MODS", 11, true, "#C4B5FD"));
        modsForm.Children.Add(Text("Recherche des mods compatibles avec la version et le loader choisis.", 12, false, "#A8A3B7", true));
        var modSearchLine = new Grid();
        modSearchLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modSearchLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modSearchBox = new TextBox { PlaceholderText = "Rechercher un mod…" };
        modSearchLine.Children.Add(modSearchBox);
        modSearchButton = new Button { Content = "Rechercher", Margin = new Thickness(10, 0, 0, 0) };
        modSearchButton.Click += ModSearchButton_Click;
        Grid.SetColumn(modSearchButton, 1);
        modSearchLine.Children.Add(modSearchButton);
        modsForm.Children.Add(modSearchLine);

        modsForm.Children.Add(Text("Source publique · Modrinth · aucune clé requise.", 12, false, "#86EFAC", true));

        modResults = new ListView { Height = 170, SelectionMode = ListViewSelectionMode.Single };
        modsForm.Children.Add(modResults);
        modInstallButton = new Button { Content = "Installer le mod sélectionné", HorizontalAlignment = HorizontalAlignment.Stretch };
        modInstallButton.Click += ModInstallButton_Click;
        modsForm.Children.Add(modInstallButton);
        modsPanel.Child = modsForm;
        content.Children.Add(modsPanel);

        var footer = new StackPanel { Margin = new Thickness(4, 18, 4, 0), Spacing = 4 };
        footer.Children.Add(Text("Installation isolée", 13, true, "#FFFFFF"));
        footer.Children.Add(Text("Les fichiers et réglages de cette app restent dans %APPDATA%\\.lolo-mc. Aucun mod ni configuration existante n'est importé.", 12, false, "#A8A3B7", true));
        var bundlePath = FindBundle();
        footer.Children.Add(Text(
            bundlePath is null ? "Bundle Minecraft introuvable à côté de l'application" : $"Bundle détecté · {bundlePath}",
            11,
            false,
            "#A8A3B7",
            true));
        content.Children.Add(footer);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;

        var savedUsernamePath = Path.Combine(GameDataDirectory, "launcher-username.txt");
        if (File.Exists(savedUsernamePath))
            usernameBox.Text = File.ReadAllText(savedUsernamePath).Trim();
    }

    private async Task LoadCatalogAsync()
    {
        try
        {
            var versionsTask = catalog.GetMinecraftVersionsAsync(CancellationToken.None);
            var loadersTask = catalog.GetFabricLoadersAsync(CancellationToken.None);
            var forgeTask = catalog.GetForgeVersionsAsync(CancellationToken.None);
            var neoForgeTask = catalog.GetNeoForgeVersionsAsync(CancellationToken.None);
            await Task.WhenAll(versionsTask, loadersTask, forgeTask, neoForgeTask);
            catalogVersions = versionsTask.Result
                .OrderByDescending(version => version.Type == "release")
                .ThenByDescending(version => version.ReleaseTime)
                .ToList();
            fabricLoaders = loadersTask.Result.ToList();
            forgeVersions = forgeTask.Result.ToList();
            neoForgeVersions = neoForgeTask.Result.ToList();

            versionBox.Items.Clear();
            foreach (var version in catalogVersions)
                versionBox.Items.Add(version);
            var preferredVersion = catalogVersions.FindIndex(version => version.Id == "26.2");
            versionBox.SelectedIndex = preferredVersion >= 0 ? preferredVersion : 0;

            RefreshLoaderVersions();

            ShowStatus($"Catalogue prêt · {catalogVersions.Count} versions disponibles.", "#86EFAC");
        }
        catch (Exception ex)
        {
            versionBox.Items.Clear();
            versionBox.Items.Add("Catalogue indisponible");
            versionBox.SelectedIndex = 0;
            ShowStatus($"Impossible de charger le catalogue : {ex.Message}", "#FCA5A5");
        }
    }

    private void LoaderBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshLoaderVersions();
    }

    private void VersionBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        RefreshLoaderVersions();
    }

    private void RefreshLoaderVersions()
    {
        if (loaderBox is null || loaderVersionBox is null)
            return;

        var loader = SelectedLoader();
        loaderVersionBox.Items.Clear();
        if (loader == GameLoader.Vanilla)
        {
            loaderVersionBox.Visibility = Visibility.Collapsed;
            return;
        }

        loaderVersionBox.Visibility = Visibility.Visible;
        if (loader == GameLoader.Fabric)
        {
            foreach (var item in fabricLoaders)
                loaderVersionBox.Items.Add(item);
        }
        else
        {
            var source = loader == GameLoader.Forge ? forgeVersions : neoForgeVersions;
            foreach (var item in FilterLoaderVersions(source))
                loaderVersionBox.Items.Add(item);
        }

        if (loaderVersionBox.Items.Count > 0)
            loaderVersionBox.SelectedIndex = 0;
    }

    private IEnumerable<string> FilterLoaderVersions(IEnumerable<string> source)
    {
        if (versionBox.SelectedItem is not CatalogVersion gameVersion)
            return source;

        if (SelectedLoader() == GameLoader.Forge)
            return source.Where(version => version.StartsWith(gameVersion.Id + "-", StringComparison.OrdinalIgnoreCase));

        var neoPrefix = gameVersion.Id.StartsWith("1.", StringComparison.Ordinal)
            ? gameVersion.Id[2..]
            : gameVersion.Id;
        var compatible = source.Where(version => version.StartsWith(neoPrefix + ".", StringComparison.OrdinalIgnoreCase));
        return compatible.Any() ? compatible : source;
    }

    private async void SkinButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, windowHandle);
            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var skinDirectory = Path.Combine(GameDataDirectory, "profiles", "offline");
            Directory.CreateDirectory(skinDirectory);
            File.Copy(file.Path, Path.Combine(skinDirectory, "skin.png"), true);
            skinStatus.Text = $"Skin local sélectionné · {file.Name}";
        }
        catch (Exception ex)
        {
            skinStatus.Text = $"Skin non sélectionné : {ex.Message}";
        }
    }

    private async void ModSearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (versionBox.SelectedItem is not CatalogVersion version)
        {
            ShowStatus("Sélectionne une version Minecraft avant de chercher un mod.", "#FCA5A5");
            return;
        }

        modSearchButton.IsEnabled = false;
        ShowStatus("Recherche des mods compatibles…", "#C4B5FD");
        try
        {
            var results = await modCatalog.SearchAsync(
                modSearchBox.Text,
                version.Id,
                SelectedLoader(),
                ModSource.Modrinth,
                null,
                CancellationToken.None);
            modResults.Items.Clear();
            foreach (var result in results)
                modResults.Items.Add(result);
            ShowStatus($"{results.Count} résultat(s) trouvé(s).", "#86EFAC");
        }
        catch (Exception ex)
        {
            ShowStatus($"Recherche impossible : {ex.Message}", "#FCA5A5");
        }
        finally
        {
            modSearchButton.IsEnabled = true;
        }
    }

    private async void ModInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (modResults.SelectedItem is not ModSearchResult mod || versionBox.SelectedItem is not CatalogVersion version)
        {
            ShowStatus("Sélectionne un mod dans les résultats.", "#FCA5A5");
            return;
        }

        var loader = SelectedLoader();
        if (loader == GameLoader.Vanilla)
        {
            ShowStatus("Un mod nécessite Fabric, Forge ou NeoForge.", "#FCA5A5");
            return;
        }

        modInstallButton.IsEnabled = false;
        downloadProgressContainer.Visibility = Visibility.Visible;
        UpdateDownloadProgress(0, null);
        try
        {
            var progress = new Progress<DownloadProgress>(download =>
            {
                if (download.TotalBytes is > 0)
                {
                    UpdateDownloadProgress(download.CompletedBytes, download.TotalBytes.Value);
                }
                else UpdateDownloadProgress(0, null);
                ShowStatus(download.Label, "#C4B5FD");
            });
            await modCatalog.InstallAsync(
                mod,
                version.Id,
                loader,
                null,
                Path.Combine(GameDataDirectory, "mods"),
                progress,
                CancellationToken.None);
            ShowStatus($"{mod.Name} installé dans .lolo-mc\\mods.", "#86EFAC");
        }
        catch (Exception ex)
        {
            ShowStatus($"Installation impossible : {ex.Message}", "#FCA5A5");
        }
        finally
        {
            modInstallButton.IsEnabled = true;
            downloadProgressContainer.Visibility = Visibility.Collapsed;
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var username = usernameBox.Text.Trim();
        if (!IsValidUsername(username))
        {
            ShowStatus("Le pseudo doit contenir 3 à 16 caractères : lettres, chiffres ou _.", "#FCA5A5");
            usernameBox.Focus(FocusState.Programmatic);
            return;
        }

        if (versionBox.SelectedItem is not CatalogVersion version)
        {
            ShowStatus("Sélectionne une version Minecraft valide.", "#FCA5A5");
            return;
        }

        var ram = SelectedRam();
        Directory.CreateDirectory(GameDataDirectory);
        File.WriteAllText(Path.Combine(GameDataDirectory, "launcher-username.txt"), username, Encoding.UTF8);

        launchButton.IsEnabled = false;
        launchProgress.Visibility = Visibility.Visible;
        downloadProgressContainer.Visibility = Visibility.Visible;
        UpdateDownloadProgress(0, null);
        ShowStatus($"Préparation de Minecraft {version.Id}…", "#C4B5FD");

        try
        {
            await Task.Delay(120);
            var loaderType = SelectedLoader();
            var loader = loaderVersionBox.SelectedItem switch
            {
                FabricLoader selectedFabric => selectedFabric.Version,
                string selectedVersion => selectedVersion,
                _ => null
            };
            var bundle = FindBundle();
            if (loaderType == GameLoader.Vanilla && version.Id == "26.2" && bundle is not null)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = bundle,
                    WorkingDirectory = Path.GetDirectoryName(bundle)!,
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add("--username");
                startInfo.ArgumentList.Add(username);
                startInfo.ArgumentList.Add("--ram");
                startInfo.ArgumentList.Add(ram.ToString());
                Process.Start(startInfo);
            }
            else
            {
                var progress = new Progress<DownloadProgress>(download =>
                {
                    if (download.TotalBytes is > 0)
                    {
                        UpdateDownloadProgress(download.CompletedBytes, download.TotalBytes.Value);
                    }
                    else
                    {
                        UpdateDownloadProgress(0, null);
                    }
                    ShowStatus(download.Label, "#C4B5FD");
                });
                var installed = await installer.InstallAsync(version, loaderType, loader, progress, CancellationToken.None);
                gameLauncher.Start(installed, username, ram);
            }
            Close();
        }
        catch (Exception ex)
        {
            launchButton.IsEnabled = true;
            launchProgress.Visibility = Visibility.Collapsed;
            downloadProgressContainer.Visibility = Visibility.Collapsed;
            ShowStatus($"Lancement impossible : {ex.Message}", "#FCA5A5");
        }
    }

    private int SelectedRam()
    {
        if (ramBox.SelectedItem is string value && int.TryParse(value.Split(' ')[0], out var ram))
            return ram;

        return 4;
    }

    private GameLoader SelectedLoader()
    {
        return loaderBox.SelectedItem?.ToString() switch
        {
            "Fabric" => GameLoader.Fabric,
            "Forge" => GameLoader.Forge,
            "NeoForge" => GameLoader.NeoForge,
            _ => GameLoader.Vanilla
        };
    }

    private void ShowStatus(string message, string color)
    {
        statusText.Text = message;
        statusText.Foreground = Brush(color);
        statusText.Visibility = Visibility.Visible;
    }

    private void UpdateDownloadProgress(long completedBytes, long? totalBytes)
    {
        if (downloadProgressContainer.ActualWidth <= 0)
            return;

        var ratio = totalBytes is > 0
            ? Math.Clamp((double)completedBytes / totalBytes.Value, 0.02, 1)
            : 0.28;
        downloadProgressFill.Width = downloadProgressContainer.ActualWidth * ratio;
    }

    private static TextBlock Text(string value, double size, bool semiBold, string color, bool wrap = false)
    {
        return new TextBlock
        {
            Text = value,
            FontSize = size,
            FontWeight = semiBold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = Brush(color),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap
        };
    }

    private static SolidColorBrush Brush(string hex)
    {
        var value = hex.TrimStart('#');
        var bytes = Convert.FromHexString(value.Length == 6 ? "FF" + value : value);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(bytes[0], bytes[1], bytes[2], bytes[3]));
    }

    private static string? FindBundle()
    {
        var appDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var candidates = new[]
        {
            Path.Combine(appDirectory.FullName, "LoloMinecraft26.2.exe"),
            Path.Combine(appDirectory.Parent?.FullName ?? string.Empty, "LoloMinecraft26.2.exe"),
            Path.Combine(appDirectory.Parent?.Parent?.FullName ?? string.Empty, "LoloMinecraft26.2.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool IsValidUsername(string value)
    {
        return value.Length is >= 3 and <= 16 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }
}
