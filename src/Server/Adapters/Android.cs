using System.Globalization;
using Snail.MCP.CleanUp.Configuration;

namespace Snail.MCP.CleanUp.Adapters;

/// <summary>The Android SDK of this Mac: its programs, its system images, its virtual devices and the emulators running them.</summary>
public sealed class Android(ServerConfig config, IPrograms programs)
{
    /// <summary>The port adb's server listens on; it belongs to every Android tool of the machine, never to one agent.</summary>
    public const int AdbPort = 5037;

    /// <summary>The emulator program.</summary>
    public string Emulator => Path.Combine(config.AndroidSdk, "emulator", "emulator");

    /// <summary>The adb program.</summary>
    public string Adb => Path.Combine(config.AndroidSdk, "platform-tools", "adb");

    /// <summary>Where virtual devices are defined.</summary>
    public string Devices => config.AvdDirectory;

    /// <summary>The directory emulators write their per-port files into, beside the device definitions.</summary>
    public string Home => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(config.AvdDirectory))!;

    /// <summary>One system image an emulator can run.</summary>
    public sealed record Image(string RelativePath, string Api, string Tag, string Abi);

    /// <summary>Whether adb's server is already running, which is the only time it may be asked anything.</summary>
    /// <remarks>
    /// Every adb command starts the server when it is not running, and the server then stays: asking
    /// <c>adb devices</c> just to look leaves a daemon on port 5037 that nobody asked for. Measured on
    /// 2026-10-11, when exactly that happened during the first inventory of this machine.
    /// </remarks>
    public async Task<bool> IsAdbRunningAsync(CancellationToken cancellationToken) =>
        (await Listeners.ReadAsync(programs, cancellationToken)).Values.Any(ports => ports.Contains(AdbPort));

    /// <summary>The names of the virtual devices defined here.</summary>
    public IReadOnlyList<string> DeviceNames() =>
        Directory.Exists(Devices) ? [.. Directory.EnumerateFiles(Devices, "*.ini").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order(StringComparer.Ordinal)] : [];

    /// <summary>The system images installed, newest API first, images without the Play Store before the ones with it.</summary>
    public IReadOnlyList<Image> Images()
    {
        var root = Path.Combine(config.AndroidSdk, "system-images");

        if (!Directory.Exists(root))
            return [];

        var abis = Directory.EnumerateDirectories(root).SelectMany(Directory.EnumerateDirectories).SelectMany(Directory.EnumerateDirectories);
        var installed = abis.Where(path => File.Exists(Path.Combine(path, "system.img")));
        var images = installed
            .Select(path => Path.GetRelativePath(config.AndroidSdk, path).Split('/'))
            .Select(parts => new Image($"{string.Join('/', parts)}/", parts[1], parts[2], parts[3]));

        return [.. images.OrderByDescending(image => Api(image.Api)).ThenBy(image => image.Tag.Contains("playstore", StringComparison.Ordinal))];
    }

    /// <summary>Writes the definition of a virtual device the way <c>avdmanager</c> would: an ini that points at a directory with its config.</summary>
    /// <remarks>
    /// <c>avdmanager</c> needs a Java runtime and the command-line tools, which a Mac with only the emulator
    /// installed does not have. The definition is two small files; the gigabytes of its data are written by
    /// the emulator on first boot, inside the device's own directory, and leave with it.
    /// </remarks>
    public string Define(string name, Image image)
    {
        var directory = Path.Combine(Devices, $"{name}.avd");

        Directory.CreateDirectory(directory);

        File.WriteAllText(Path.Combine(Devices, $"{name}.ini"), $"""
            avd.ini.encoding=UTF-8
            path={directory}
            path.rel=avd/{name}.avd
            target={image.Api}

            """);

        File.WriteAllText(Path.Combine(directory, "config.ini"), $"""
            AvdId={name}
            avd.ini.displayname={name}
            abi.type={image.Abi}
            hw.cpu.arch={(image.Abi.StartsWith("arm64", StringComparison.Ordinal) ? "arm64" : "x86_64")}
            image.sysdir.1={image.RelativePath}
            tag.id={image.Tag}
            hw.lcd.width=1080
            hw.lcd.height=2400
            hw.lcd.density=420
            hw.ramSize=2048
            disk.dataPartition.size=4G
            hw.keyboard=yes
            fastboot.forceColdBoot=yes
            PlayStore.enabled=false

            """);

        return directory;
    }

    /// <summary>The serial adb knows an emulator by, from its console port.</summary>
    public static string Serial(int port) => $"emulator-{port}";

    /// <summary>The console port in an emulator's command line, when it was given one.</summary>
    public static int? PortOf(string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(words, "-port");

        return at >= 0 && at + 1 < words.Length && int.TryParse(words[at + 1], CultureInfo.InvariantCulture, out var port) ? port : null;
    }

    /// <summary>The virtual device in an emulator's command line; null for any other program.</summary>
    public static string? DeviceOf(string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var at = Array.IndexOf(words, "-avd");

        return at >= 0 && at + 1 < words.Length ? words[at + 1] : null;
    }

    private static double Api(string api) =>
        double.TryParse(api.Replace("android-", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var level) ? level : 0;
}
