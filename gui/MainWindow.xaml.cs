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
    private List<CatalogVersion> catalogVersions = [];
    private List<FabricLoader> fabricLoaders = [];

    private TextBox usernameBox = null!;
    private ComboBox ramBox = null!;
    private ComboBox versionBox = null!;
    private CheckBox fabricBox = null!;
    private ComboBox fabricLoaderBox = null!;
    private Button launchButton = null!;
    private Button skinButton = null!;
    private TextBlock launchProgress = null!;
    private TextBlock statusText = null!;
    private TextBlock skinStatus = null!;

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
        form.Children.Add(versionBox);

        fabricBox = new CheckBox
        {
            Content = "Utiliser Fabric (loader uniquement, aucun mod inclus)",
            Foreground = Brush("#FFFFFF")
        };
        fabricBox.Checked += FabricBox_Changed;
        fabricBox.Unchecked += FabricBox_Changed;
        form.Children.Add(fabricBox);

        fabricLoaderBox = new ComboBox
        {
            Header = "Loader Fabric",
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        form.Children.Add(fabricLoaderBox);

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
            await Task.WhenAll(versionsTask, loadersTask);
            catalogVersions = versionsTask.Result
                .OrderByDescending(version => version.Type == "release")
                .ThenByDescending(version => version.ReleaseTime)
                .ToList();
            fabricLoaders = loadersTask.Result.ToList();

            versionBox.Items.Clear();
            foreach (var version in catalogVersions)
                versionBox.Items.Add(version);
            var preferredVersion = catalogVersions.FindIndex(version => version.Id == "26.2");
            versionBox.SelectedIndex = preferredVersion >= 0 ? preferredVersion : 0;

            fabricLoaderBox.Items.Clear();
            foreach (var loader in fabricLoaders)
                fabricLoaderBox.Items.Add(loader);
            if (fabricLoaderBox.Items.Count > 0)
                fabricLoaderBox.SelectedIndex = 0;

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

    private void FabricBox_Changed(object sender, RoutedEventArgs e)
    {
        fabricLoaderBox.Visibility = fabricBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
        ShowStatus($"Préparation de Minecraft {version.Id}…", "#C4B5FD");

        try
        {
            await Task.Delay(120);
            var fabric = fabricBox.IsChecked == true;
            var loader = fabricLoaderBox.SelectedItem is FabricLoader selectedLoader ? selectedLoader.Version : null;
            var bundle = FindBundle();
            if (!fabric && version.Id == "26.2" && bundle is not null)
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
                var progress = new Progress<string>(message => ShowStatus(message, "#C4B5FD"));
                var installed = await installer.InstallAsync(version, fabric, loader, progress, CancellationToken.None);
                gameLauncher.Start(installed, username, ram);
            }
            Close();
        }
        catch (Exception ex)
        {
            launchButton.IsEnabled = true;
            launchProgress.Visibility = Visibility.Collapsed;
            ShowStatus($"Lancement impossible : {ex.Message}", "#FCA5A5");
        }
    }

    private int SelectedRam()
    {
        if (ramBox.SelectedItem is string value && int.TryParse(value.Split(' ')[0], out var ram))
            return ram;

        return 4;
    }

    private void ShowStatus(string message, string color)
    {
        statusText.Text = message;
        statusText.Foreground = Brush(color);
        statusText.Visibility = Visibility.Visible;
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
