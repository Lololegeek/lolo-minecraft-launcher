using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace LoloLauncherSetup;

internal static class SetupProgram
{
    private const string PayloadMagic = "LOLOLAUNCHERSETUP";
    private const string InstallDirectoryName = "LoloLauncher";

    [STAThread]
    private static int Main(string[] args)
    {
        var installDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            InstallDirectoryName);
        var launchAfterInstall = !args.Any(static arg => arg.Equals("--no-launch", StringComparison.OrdinalIgnoreCase));

        try
        {
            Directory.CreateDirectory(installDirectory);
            using var executable = File.OpenRead(Environment.ProcessPath!);
            var payload = OpenPayload(executable);
            Extract(payload, installDirectory);
            CreateShortcut("Lolo Launcher", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Lolo Launcher.lnk"), installDirectory);
            CreateShortcut("Lolo Launcher", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "Lolo Launcher.lnk"), installDirectory);

            var gui = Path.Combine(installDirectory, "LoloMinecraftGui.exe");
            if (!File.Exists(gui))
                throw new FileNotFoundException("Le GUI n'est pas présent dans le package.", gui);

            if (launchAfterInstall)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = gui,
                    WorkingDirectory = installDirectory,
                    UseShellExecute = true
                });
            }

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero,
                $"Installation impossible :\n\n{ex.Message}",
                "Lolo Launcher",
                0x10);
            return 1;
        }
    }

    private static Stream OpenPayload(FileStream executable)
    {
        var magic = Encoding.ASCII.GetBytes(PayloadMagic);
        if (executable.Length < magic.Length + sizeof(long))
            throw new InvalidDataException("Le setup ne contient aucun package.");

        executable.Seek(-sizeof(long), SeekOrigin.End);
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        executable.ReadExactly(lengthBytes);
        var payloadLength = BitConverter.ToInt64(lengthBytes);
        var magicPosition = executable.Length - sizeof(long) - magic.Length;
        if (payloadLength <= 0 || magicPosition < payloadLength)
            throw new InvalidDataException("Le package du setup est invalide.");

        executable.Seek(magicPosition, SeekOrigin.Begin);
        Span<byte> actualMagic = stackalloc byte[magic.Length];
        executable.ReadExactly(actualMagic);
        if (!actualMagic.SequenceEqual(magic))
            throw new InvalidDataException("Le package du setup est corrompu.");

        executable.Seek(magicPosition - payloadLength, SeekOrigin.Begin);
        return new BoundedStream(executable, payloadLength);
    }

    private static void Extract(Stream payload, string installDirectory)
    {
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
        var root = Path.GetFullPath(installDirectory) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(installDirectory, entry.FullName));
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Le package contient un chemin invalide.");

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

    private static void CreateShortcut(string name, string path, string installDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host est indisponible.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = Path.Combine(installDirectory, "LoloMinecraftGui.exe");
        shortcut.WorkingDirectory = installDirectory;
        shortcut.Description = "Lolo Minecraft Launcher";
        shortcut.IconLocation = Path.Combine(installDirectory, "LoloMinecraftGui.exe");
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private sealed class BoundedStream : Stream
    {
        private readonly Stream inner;
        private readonly long length;
        private readonly long payloadOrigin;
        private long position;

        public BoundedStream(Stream inner, long length)
        {
            this.inner = inner;
            this.length = length;
            payloadOrigin = inner.Position;
        }

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = length - position;
            if (remaining <= 0)
                return 0;
            var read = inner.Read(buffer, offset, (int)Math.Min(count, remaining));
            position += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var remaining = length - position;
            if (remaining <= 0)
                return 0;
            var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (target < 0 || target > length)
                throw new IOException("Position de package invalide.");
            inner.Seek(payloadOrigin + target, SeekOrigin.Begin);
            position = target;
            return position;
        }

        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
    }
}
