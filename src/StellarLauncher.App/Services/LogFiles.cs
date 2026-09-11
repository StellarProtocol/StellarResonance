using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using StellarLauncher.Core.Clients;

namespace StellarLauncher.App.Services;

/// <summary>Log files a client can show: the BepInEx log, then any Unity Player.log under the (prefix's) LocalLow.</summary>
public static class LogFiles
{
    public static IReadOnlyList<string> Candidates(IFileSystem fs, ClientProfile c, bool isWindows)
    {
        var list = new List<string>();
        var bep = fs.Path.Combine(c.GameMiniDir, "BepInEx", "LogOutput.log");
        if (fs.File.Exists(bep)) list.Add(bep);
        try
        {
            var lowRoots = isWindows
                ? new[] { fs.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow") }
                : (c.Linux?.WinePrefix is { } p && fs.Directory.Exists(fs.Path.Combine(p, "drive_c", "users"))
                    ? fs.Directory.GetDirectories(fs.Path.Combine(p, "drive_c", "users")).Select(u => fs.Path.Combine(u, "AppData", "LocalLow")).ToArray()
                    : Array.Empty<string>());
            foreach (var low in lowRoots.Where(fs.Directory.Exists))
                list.AddRange(fs.Directory.GetFiles(low, "Player.log", System.IO.SearchOption.AllDirectories));
        }
        catch (Exception) { /* best effort — an unreadable LocalLow just yields no Player.log */ }
        return list;
    }
}
