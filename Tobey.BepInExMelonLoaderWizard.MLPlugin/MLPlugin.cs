#nullable enable

using MelonLoader;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

[assembly: MelonInfo(typeof(Tobey.BepInExMelonLoaderWizard.MLPlugin), "Tobey's BepInEx MelonLoader Wizard", "0.2.0", "Tobey")]

namespace Tobey.BepInExMelonLoaderWizard;

public class MLPlugin : MelonPlugin
{
    private static HWND? activeWindow;
    private static HWND ActiveWindow => activeWindow ??= PInvoke.GetActiveWindow();

    private IniFile? _doorstopConfig;
    private IniFile DoorstopConfig => _doorstopConfig ??= new IniFile(Path.Combine(MelonUtils.GameDirectory, "doorstop_config"));

    private string UnityDoorstop_TargetAssembly => DoorstopConfig.Read("target_assembly", "General") ?? DoorstopConfig.Read("targetAssembly", "UnityDoorstop");

    private bool? UnityDoorstop_Enabled => bool.TryParse(DoorstopConfig.Read("enabled", "General") ?? DoorstopConfig.Read("enabled", "UnityDoorstop"), out bool enabled) switch
    {
        true => enabled,
        _ => null
    };

    private bool IsBepinexInstalled() =>
        ProxyHelper.GetInstalledProxyDlls(MelonUtils.GameDirectory).Any(path =>
        {
            var info = FileVersionInfo.GetVersionInfo(path);

            return
                (info.ProductName?.ToLowerInvariant() is string name && (name.Contains("neightools") || name.Contains("unitydoorstop"))) ||
                (info.FileDescription?.ToLowerInvariant() is string description && description.Contains("unitydoorstop"));
        }) &&
        Path.GetFileName(UnityDoorstop_TargetAssembly).StartsWith("BepInEx");

    private bool IsBepinexEnabled() =>
        IsBepinexInstalled() &&
        UnityDoorstop_Enabled is bool enabled && enabled;

    private IEnumerable<AssemblyName>? melonloaderReferencedAssemblies;
    private bool IsMLLoaderLoaded() =>
        (melonloaderReferencedAssemblies ??= typeof(MelonPlugin).Assembly.GetReferencedAssemblies())
            .Any(assemblyName => assemblyName.Name == "BepInEx");

    private static bool IsMLDisabler(string path) => FileVersionInfo.GetVersionInfo(path).FileDescription == "Tobey.BepInExMelonLoaderWizard.MLDisabler";
    private static string? GetMLDisablerPath() =>
        DirectoryHelper.EnumerateFiles(MelonUtils.GameDirectory, "Tobey.BepInExMelonLoaderWizard.MLDisabler.exe", SearchOption.AllDirectories)
            .FirstOrDefault(IsMLDisabler);

    private bool IsMLLoaderInstalled()
    {
        try
        {
            var pluginsDir = new string[] { MelonUtils.GameDirectory, "BepInEx", "plugins" }.Aggregate(Path.Combine);
            return
                IsMLLoaderLoaded() ||
                DirectoryHelper.EnumerateFiles(pluginsDir, "BepInEx.MelonLoader.Loader*.dll", SearchOption.AllDirectories).Any();
        }
        catch
        {
            return false;
        }
    }

    public override void OnApplicationEarlyStart()
    {
        if (IsMLLoaderLoaded())
        {
            LoggerInstance.Msg("Thanks for using BepInEx.MelonLoader.Loader!");
            return;
        }

        LoggerInstance.Warning("Vanilla MelonLoader detected!");

        if (IsBepinexEnabled() && IsMLLoaderInstalled())
        {
            var disabler = GetMLDisablerPath();

            var problem = "You appear to be running vanilla MelonLoader!\r\n\r\n" +
                "You probably installed MelonLoader after installing BepInEx.\r\n\r\n" +
                "Leaving MelonLoader installed will prevent BepInEx and BepInEx mods from loading.\r\n\r\n" +
                "We recommend that you uninstall MelonLoader, which will allow BepInEx to load your BepInEx and MelonLoader mods.\r\n\r\n";

            var style =
                MESSAGEBOX_STYLE.MB_YESNO |
                MESSAGEBOX_STYLE.MB_TASKMODAL |
                MESSAGEBOX_STYLE.MB_ICONWARNING |
                MESSAGEBOX_STYLE.MB_TOPMOST;

            if (disabler is null)
            {
                var quitDialogResult = PInvoke.MessageBox(
                    ActiveWindow,
                    problem +
                    "Would you like to quit the game and open the instructions on uninstalling MelonLoader?",
                    "Quit and manually uninstall MelonLoader?",
                    style);

                if (quitDialogResult != MESSAGEBOX_RESULT.IDYES) return;

                using var _ = Process.Start("https://github.com/LavaGang/MelonLoader.Installer#how-to-un-install-melonloader");
            }
            else
            {
                var disableMelonLoaderAndRestartDialogResult = PInvoke.MessageBox(
                    ActiveWindow,
                    problem +
                    "Would you like to automatically uninstall MelonLoader and restart the game?",
                    "Uninstall MelonLoader and restart?",
                    style);

                if (disableMelonLoaderAndRestartDialogResult != MESSAGEBOX_RESULT.IDYES) return;

                IEnumerable<string> args =
                    ((string[])[Process.GetCurrentProcess().ProcessName, .. Environment.GetCommandLineArgs()])
                        .Select(arg => $"\"{Regex.Replace(arg, @"(\\+)$", @"$1$1")}\"");
                string[] cmdArgs = [
                    "/c",
                    $"start /d \"{MelonUtils.GameDirectory}\" \"\" \"{disabler}\"",
                    ..args
                ];

                try
                {
                    using var _ = Process.Start(new ProcessStartInfo("cmd", string.Join(" ", cmdArgs))
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        WorkingDirectory = MelonUtils.GameDirectory,
                    });
                }
                catch (Exception e)
                {
                    if (e is Win32Exception win32Exception && win32Exception.NativeErrorCode == 0x000004C7)
                    {
                        LoggerInstance.Warning("User cancelled, will need to manually uninstall.");
                    }
                    else
                    {
                        LoggerInstance.Error(e);
                        LoggerInstance.Warning("An unhandled exception occurred while trying to disable MelonLoader, user will need to manually uninstall.");
                    }

                    var quitDialogResult = PInvoke.MessageBox(
                        ActiveWindow,
                        "An error occurred while trying to disable MelonLoader.\r\n\r\n" +
                        "You will need to manually uninstall MelonLoader to use BepInEx and BepInEx mods.\r\n\r\n" +
                        "Would you like to quit the game and open the instructions on uninstalling MelonLoader?",
                        "Quit and manually uninstall MelonLoader?",
                        style);

                    if (quitDialogResult != MESSAGEBOX_RESULT.IDYES) return;

                    using var _ = Process.Start("https://github.com/LavaGang/MelonLoader.Installer#how-to-un-install-melonloader");
                }
            }
        }

        Environment.Exit(0);
    }
}
