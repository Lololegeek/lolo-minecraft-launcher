using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace LoloMinecraftLauncher;

internal static class Program
{
    private const string PayloadMagic = "VALORIA26PAYLOAD";
    private const string GameVersion = "26.2";
    private const string AssetIndex = "32";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var username = ReadOption(args, "--username");
        var ram = ReadOption(args, "--ram");
        var maxMemoryGb = int.TryParse(ram, out var parsedRam) && parsedRam is >= 1 and <= 32
            ? parsedRam
            : 4;

        if (!LoginForm.IsValidUsername(username ?? string.Empty))
        {
            using var form = new LoginForm();
            if (form.ShowDialog() != DialogResult.OK)
                return;

            username = form.Username;
        }

        var gameDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".lolo-mc");

        try
        {
            Bootstrapper.EnsureExtracted(gameDirectory, PayloadMagic);
            var exitCode = Minecraft.Start(gameDirectory, username!, AssetIndex, GameVersion, maxMemoryGb);

            if (exitCode != 0)
            {
                MessageBox.Show(
                    $"Minecraft s'est fermé avec le code {exitCode}.\n\nLe journal est disponible ici :\n{Path.Combine(gameDirectory, "lolo-launcher.log")}",
                    "Minecraft 26.2",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossible de lancer Minecraft :\n\n{ex.Message}",
                "Launcher Lolo",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string? ReadOption(string[] args, string option)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(option, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private sealed class LoginForm : Form
    {
        private readonly TextBox usernameBox;
        private readonly Button launchButton;

        public string Username => usernameBox.Text.Trim();

        public LoginForm()
        {
            Text = "Minecraft 26.2 — Lolo Launcher";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 190);

            var title = new Label
            {
                AutoSize = false,
                Text = "Minecraft 26.2",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                Location = new Point(28, 20),
                Size = new Size(370, 38)
            };

            var subtitle = new Label
            {
                AutoSize = false,
                Text = "Vanilla · mode hors-ligne",
                ForeColor = Color.DimGray,
                Location = new Point(31, 59),
                Size = new Size(370, 24)
            };

            var usernameLabel = new Label
            {
                AutoSize = true,
                Text = "Pseudo",
                Location = new Point(31, 96)
            };

            usernameBox = new TextBox
            {
                Location = new Point(92, 92),
                Size = new Size(205, 28),
                MaxLength = 16,
                PlaceholderText = "Ton pseudo"
            };

            launchButton = new Button
            {
                Text = "Jouer",
                DialogResult = DialogResult.OK,
                Location = new Point(312, 89),
                Size = new Size(88, 34)
            };
            launchButton.Click += (_, _) =>
            {
                if (!IsValidUsername(Username))
                {
                    MessageBox.Show(
                        this,
                        "Le pseudo doit contenir entre 3 et 16 caractères : lettres, chiffres ou _.",
                        "Pseudo invalide",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                    usernameBox.Focus();
                }
            };

            var info = new Label
            {
                AutoSize = false,
                Text = "Les fichiers seront installés dans %APPDATA%\\.lolo-mc",
                ForeColor = Color.DimGray,
                Location = new Point(31, 145),
                Size = new Size(370, 24)
            };

            AcceptButton = launchButton;
            CancelButton = new Button { DialogResult = DialogResult.Cancel };
            Controls.AddRange([title, subtitle, usernameLabel, usernameBox, launchButton, info]);
        }

        internal static bool IsValidUsername(string value)
        {
            if (value.Length is < 3 or > 16)
                return false;

            return value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        }
    }

    private static class Bootstrapper
    {
        public static void EnsureExtracted(string destination, string magic)
        {
            var marker = Path.Combine(destination, ".lolo-bundle-complete");
            var java = Path.Combine(destination, "runtime", "bin", "java.exe");
            var versionJar = Path.Combine(destination, "versions", GameVersion, $"{GameVersion}.jar");
            var classpath = Path.Combine(destination, "classpath.txt");

            if (File.Exists(marker) && File.Exists(java) && File.Exists(versionJar) && File.Exists(classpath))
                return;

            Directory.CreateDirectory(destination);
            using var self = File.OpenRead(Environment.ProcessPath ?? throw new InvalidOperationException("Chemin du launcher introuvable."));
            if (self.Length < 24)
                throw new InvalidDataException("Le bundle Minecraft est absent de cet exécutable.");

            self.Seek(-24, SeekOrigin.End);
            var footerMagic = new byte[16];
            self.ReadExactly(footerMagic);
            var expectedMagic = Encoding.ASCII.GetBytes(magic);
            var payloadLengthBytes = new byte[8];
            self.ReadExactly(payloadLengthBytes);
            var payloadLength = BitConverter.ToInt64(payloadLengthBytes, 0);

            if (!footerMagic.SequenceEqual(expectedMagic) || payloadLength <= 0 || payloadLength > self.Length - 24)
                throw new InvalidDataException("Le bundle Minecraft est invalide ou incomplet.");

            var payloadStart = self.Length - 24 - payloadLength;
            self.Seek(payloadStart, SeekOrigin.Begin);

            using var payload = new PayloadStream(self, payloadStart, payloadLength);
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in archive.Entries)
            {
                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(destination, relative));
                var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

                if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Le bundle contient un chemin dangereux.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }

            ExtractNatives(destination);
            File.WriteAllText(marker, "Minecraft 26.2 vanilla\n", Encoding.UTF8);
        }

        private static void ExtractNatives(string destination)
        {
            var nativeDirectory = Path.Combine(destination, "natives", "java");
            Directory.CreateDirectory(nativeDirectory);

            foreach (var jar in Directory.EnumerateFiles(
                         Path.Combine(destination, "libraries"),
                         "*-natives-windows.jar",
                         SearchOption.AllDirectories))
            {
                using var stream = File.OpenRead(jar);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var target = Path.GetFullPath(Path.Combine(nativeDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    var root = Path.GetFullPath(nativeDirectory) + Path.DirectorySeparatorChar;
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Un fichier natif contient un chemin invalide.");

                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                }
            }
        }

        private sealed class PayloadStream(Stream source, long start, long length) : Stream
        {
            private long position;

            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position
            {
                get => position;
                set => Seek(value, SeekOrigin.Begin);
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (position >= length)
                    return 0;

                var allowed = (int)Math.Min(count, length - position);
                source.Seek(start + position, SeekOrigin.Begin);
                var read = source.Read(buffer, offset, allowed);
                position += read;
                return read;
            }

            public override int Read(Span<byte> buffer)
            {
                if (position >= length)
                    return 0;

                var allowed = buffer[..(int)Math.Min(buffer.Length, length - position)];
                source.Seek(start + position, SeekOrigin.Begin);
                var read = source.Read(allowed);
                position += read;
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                var next = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => position + offset,
                    SeekOrigin.End => length + offset,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin))
                };

                if (next < 0 || next > length)
                    throw new IOException("Position de lecture invalide.");

                position = next;
                return position;
            }

            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    private static class Minecraft
    {
        public static int Start(string gameDirectory, string username, string assetIndex, string version, int maxMemoryGb)
        {
            var java = Path.Combine(gameDirectory, "runtime", "bin", "java.exe");
            var natives = Path.Combine(gameDirectory, "natives");
            var classpathFile = Path.Combine(gameDirectory, "classpath.txt");
            var classpath = string.Join(
                Path.PathSeparator,
                File.ReadAllLines(classpathFile, Encoding.UTF8)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => Path.Combine(gameDirectory, line.Trim())));

            var uuid = OfflineUuid(username);
            var startInfo = new ProcessStartInfo
            {
                FileName = java,
                WorkingDirectory = gameDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in new[]
                     {
                         "-Xms1G", $"-Xmx{maxMemoryGb}G", "-XX:+UseZGC",
                         "--enable-native-access=ALL-UNNAMED",
                         $"-Djava.library.path={Path.Combine(natives, "java")}",
                         $"-Djna.tmpdir={Path.Combine(natives, "jna")}",
                         $"-Dorg.lwjgl.system.SharedLibraryExtractPath={Path.Combine(natives, "lwjgl")}",
                         $"-Dio.netty.native.workdir={Path.Combine(natives, "netty")}",
                         "-Dminecraft.launcher.brand=LoloLauncher",
                         "-Dminecraft.launcher.version=1.0",
                         "-cp", classpath,
                         "net.minecraft.client.main.Main",
                         "--username", username,
                         "--version", version,
                         "--gameDir", gameDirectory,
                         "--assetsDir", Path.Combine(gameDirectory, "assets"),
                         "--assetIndex", assetIndex,
                         "--uuid", uuid,
                         "--accessToken", "0",
                         "--clientId", "00000000-0000-0000-0000-000000000000",
                         "--xuid", "",
                         "--versionType", "release"
                     })
            {
                startInfo.ArgumentList.Add(arg);
            }

            Directory.CreateDirectory(Path.Combine(natives, "jna"));
            Directory.CreateDirectory(Path.Combine(natives, "lwjgl"));
            Directory.CreateDirectory(Path.Combine(natives, "netty"));

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Java n'a pas pu démarrer.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var output = outputTask.GetAwaiter().GetResult();
            var error = errorTask.GetAwaiter().GetResult();
            File.WriteAllText(
                Path.Combine(gameDirectory, "lolo-launcher.log"),
                $"Launcher Lolo — Minecraft {version} — pseudo {username}\nDémarré le {DateTime.Now:O}\n\n{output}\n{error}",
                Encoding.UTF8);
            return process.ExitCode;
        }

        private static string OfflineUuid(string username)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return Convert.ToHexString(hash[..4]).ToLowerInvariant() + "-" +
                   Convert.ToHexString(hash[4..6]).ToLowerInvariant() + "-" +
                   Convert.ToHexString(hash[6..8]).ToLowerInvariant() + "-" +
                   Convert.ToHexString(hash[8..10]).ToLowerInvariant() + "-" +
                   Convert.ToHexString(hash[10..]).ToLowerInvariant();
        }
    }
}
