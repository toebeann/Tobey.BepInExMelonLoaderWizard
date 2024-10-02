#nullable enable

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Tobey.BepInExMelonLoaderWizard;

internal static class ProxyHelper
{
    public static readonly string[] PROXY_DLL_FILE_NAMES = ["version.dll", "winhttp.dll", "winmm.dll"];

    public static IEnumerable<string> GetInstalledProxyDlls(string gameRootPath) =>
            PROXY_DLL_FILE_NAMES
                .Select(filename => Path.Combine(gameRootPath, filename))
                .Where(File.Exists);

    public static bool IsUnityDoorstopProxyDll(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);

        return
            (info.ProductName?.ToLowerInvariant() is string name && (name.Contains("neightools") || name.Contains("unitydoorstop"))) ||
            (info.FileDescription?.ToLowerInvariant() is string description && description.Contains("unitydoorstop"));
    }

    public static bool HasUnityDoorstopProxyDll(string gameRootPath) => GetInstalledProxyDlls(gameRootPath).Any(IsUnityDoorstopProxyDll);

    public static IEnumerable<string> GetUnityDoorstopProxyDlls(string gameRootPath) => GetInstalledProxyDlls(gameRootPath).Where(IsUnityDoorstopProxyDll);

    public static bool IsMelonLoaderProxyDll(string path)
    {
        try
        {
            if (IsUnityDoorstopProxyDll(path)) return false;

            using var reader = File.OpenText(path);
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                if (line.ToLowerInvariant().Contains("melonloader")) return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasMelonLoaderProxyDll(string gameRootPath) => GetInstalledProxyDlls(gameRootPath).Any(IsMelonLoaderProxyDll);

    public static IEnumerable<string> GetMelonLoaderProxyDlls(string gameRootPath) => GetInstalledProxyDlls(gameRootPath).Where(IsMelonLoaderProxyDll);
}
