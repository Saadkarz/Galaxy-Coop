using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GalaxyCoop.Launcher;

/// <summary>Finds Dolphin and the SMG2 image, installs the Riivolution patch and starts the game with it enabled.</summary>
public static class DolphinSetup
{
    static readonly string[] ImageExtensions = { ".iso", ".rvz", ".wbfs", ".gcz", ".wia", ".ciso" };

    public sealed record Environment(string? DolphinDir, string? GameImage, string? UserDir, string? PatchDir, string[] Problems)
    {
        public bool Ready => Problems.Length == 0;
    }

    /// <summary>Dolphin user folder passed with -u, so two launchers on one PC get separate Dolphins.</summary>
    public static string? UserDirOverride { get; set; }

    public static Environment Inspect(LauncherSettings s)
    {
        var problems = new List<string>();
        var dolphin = FindDolphin(s.DolphinDir);
        if (dolphin is null) problems.Add("Dolphin not found. Set the Dolphin folder in Settings.");
        var userDir = dolphin is null ? null : UserDirOverride ?? UserDirFor(dolphin);
        var image = FindGameImage(s.GameImage, dolphin, userDir);
        if (image is null) problems.Add("Super Mario Galaxy 2 (USA) image not found. Set it in Settings.");
        var patch = FindPatch();
        if (patch is null) problems.Add("The Galaxy Co-op patch is not built yet (run tools\\build-smg2.ps1).");
        return new(dolphin, image, userDir, patch, problems.ToArray());
    }

    static string? FindDolphin(string configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        if (System.Environment.GetEnvironmentVariable("GCOOP_DOLPHIN_DIR") is { Length: > 0 } env) candidates.Add(env);
        candidates.Add(@"D:\Games\Dolphin-x64");
        candidates.Add(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Dolphin"));
        candidates.Add(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "Dolphin-x64"));
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "Dolphin.exe")));
    }

    /// <summary>Dolphin keeps saves and config next to the exe when portable.txt exists, else in AppData.</summary>
    static string UserDirFor(string dolphinDir) =>
        File.Exists(Path.Combine(dolphinDir, "portable.txt"))
            ? Path.Combine(dolphinDir, "User")
            : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "Dolphin Emulator");

    static string? FindGameImage(string configured, string? dolphinDir, string? userDir)
    {
        if (File.Exists(configured)) return configured;
        var folders = new List<string>();
        if (dolphinDir is not null) folders.Add(Path.Combine(dolphinDir, "wii games"));
        if (userDir is not null)
        {
            var ini = Path.Combine(userDir, "Config", "Dolphin.ini");
            if (File.Exists(ini))
                foreach (Match m in Regex.Matches(File.ReadAllText(ini), @"^ISOPath\d+\s*=\s*(.+?)\s*$", RegexOptions.Multiline))
                    folders.Add(m.Groups[1].Value);
        }
        foreach (var folder in folders.Where(Directory.Exists))
        {
            var hit = Directory.EnumerateFiles(folder)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .FirstOrDefault(f => Regex.IsMatch(Path.GetFileName(f), @"SB4E|Galaxy 2|Galaxy2", RegexOptions.IgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>build/smg2 in the repository, which holds GalaxyCoop.xml and CustomCode/.</summary>
    static string? FindPatch()
    {
        var root = RepoRoot();
        var dir = root is null ? null : Path.Combine(root, "build", "smg2");
        return dir is not null && File.Exists(Path.Combine(dir, "GalaxyCoop.xml")) ? dir : null;
    }

    public static string? RepoRoot()
    {
        var starts = new[] { System.Environment.GetEnvironmentVariable("GCOOP_ROOT"), System.Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (var start in starts.Where(s => !string.IsNullOrEmpty(s)))
        {
            for (var d = new DirectoryInfo(start!); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "shared", "mailbox.h"))) return d.FullName;
        }
        return null;
    }

    /// <summary>Copies the patch into Dolphin's Load\Riivolution and starts the game with it enabled.</summary>
    public static Process Launch(Environment env)
    {
        if (!env.Ready) throw new InvalidOperationException(string.Join(" ", env.Problems));
        var riivRoot = Path.Combine(env.UserDir!, "Load", "Riivolution");
        var xml = Path.Combine(riivRoot, "riivolution", "GalaxyCoop.xml");
        var codeDir = Path.Combine(riivRoot, "GalaxyCoop", "CustomCode");
        Directory.CreateDirectory(Path.GetDirectoryName(xml)!);
        Directory.CreateDirectory(codeDir);
        File.Copy(Path.Combine(env.PatchDir!, "GalaxyCoop.xml"), xml, overwrite: true);
        foreach (var bin in Directory.GetFiles(Path.Combine(env.PatchDir!, "CustomCode")))
            File.Copy(bin, Path.Combine(codeDir, Path.GetFileName(bin)), overwrite: true);

        var descriptor = new Dictionary<string, object>
        {
            ["type"] = "dolphin-game-mod-descriptor",
            ["version"] = 1,
            ["base-file"] = env.GameImage!,
            ["display-name"] = "Galaxy Co-op",
            ["riivolution"] = new Dictionary<string, object>
            {
                ["patches"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["xml"] = xml,
                        ["root"] = riivRoot,
                        ["options"] = new[]
                        {
                            new Dictionary<string, object> { ["section-name"] = "Galaxy Co-op", ["option-name"] = "Galaxy Co-op", ["choice"] = 1 },
                        },
                    },
                },
            },
        };
        var json = Path.Combine(env.UserDir!, "GalaxyCoop.json");
        File.WriteAllText(json, JsonSerializer.Serialize(descriptor, new JsonSerializerOptions { WriteIndented = true }));

        var psi = new ProcessStartInfo(Path.Combine(env.DolphinDir!, "Dolphin.exe")) { UseShellExecute = false };
        if (UserDirOverride is not null)
        {
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(UserDirOverride);
        }
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(json);
        return Process.Start(psi) ?? throw new InvalidOperationException("Dolphin did not start");
    }
}
