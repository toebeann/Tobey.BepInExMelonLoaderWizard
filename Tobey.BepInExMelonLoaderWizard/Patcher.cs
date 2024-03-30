#nullable enable

using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tobey.BepInExMelonLoaderWizard;

public static class Patcher
{
    // Without the contents of this region, the patcher will not be loaded by BepInEx - do not remove!
    #region BepInEx Patcher Contract
    public static IEnumerable<string> TargetDLLs { get; } = [];
    public static void Patch(AssemblyDefinition _) { }
    #endregion

    private static HWND? activeWindow;
    private static HWND ActiveWindow => activeWindow ??= PInvoke.GetActiveWindow();

    private static bool IsMLPlugin(string path) => FileVersionInfo.GetVersionInfo(path).FileDescription == "Tobey.BepInExMelonLoaderWizard.MLPlugin";

    private static bool IsMLDisabler(string path) => FileVersionInfo.GetVersionInfo(path).FileDescription == "Tobey.BepInExMelonLoaderWizard.MLDisabler";

    // entry point - do not rename!
    public static void Initialize()
    {
        using var logger = Logger.CreateLogSource("Tobey's BMW");

        if (!PlatformHelper.Is(Platform.Windows))
        {
            logger.LogWarning("Incompatible platform detected, bailing out.");
            return;
        }

        var melonloaderModFoldersWithFiles =
            GetMelonLoaderModFolders(Paths.GameRootPath)
                .Where(path =>
                    DirectoryHelper.EnumerateFiles(DirectoryHelper.ResolveLinkTargetOrPath(path), "*", SearchOption.AllDirectories)
                        .Where(file => !IsMLPlugin(file) && !IsMLDisabler(file))
                        .Any());

        if (melonloaderModFoldersWithFiles.Any())
        {
            // melonloader mod files have been detected, we should offer to migrate them for the user

            logger.LogInfo("MelonLoader mod files were detected.");

            var migrationDialogResult = PInvoke.MessageBox(
                ActiveWindow,
                "MelonLoader mod files which require migration have been detected.\r\n\r\n" +
                "Would you like to automatically migrate them?",
                "MelonLoader migration required",
                MESSAGEBOX_STYLE.MB_YESNO |
                MESSAGEBOX_STYLE.MB_TASKMODAL |
                MESSAGEBOX_STYLE.MB_ICONQUESTION |
                MESSAGEBOX_STYLE.MB_TOPMOST);

            if (migrationDialogResult == MESSAGEBOX_RESULT.IDYES)
            {
                logger.LogMessage("Migrating MelonLoader mod files...");

                try
                {
                    MigrateMelonLoaderModFiles(melonloaderModFoldersWithFiles);
                    logger.LogInfo("Migration complete.");
                }
                catch (Exception e)
                {
                    logger.LogError(e);
                    logger.LogWarning("Failed to migrate MelonLoader mod files!");
                }
            }
            else
            {
                logger.LogDebug("User chose not to migrate MelonLoader mod files.");
            }
        }

        var melonLoaderFolder =
            DirectoryHelper.EnumerateFileSystemEntries(Paths.GameRootPath)
                .SingleOrDefault(entry =>
                    Path.GetFileName(entry).ToLowerInvariant() == "melonloader" &&
                    (Directory.Exists(entry) || DirectoryHelper.IsSymbolicLink(entry)));

        var emptyMelonloaderModFolders =
            GetMelonLoaderModFolders(Paths.GameRootPath)
                .Where(path => !DirectoryHelper.EnumerateFiles(DirectoryHelper.ResolveLinkTargetOrPath(path)).Any());

        var melonLoaderFiles = DirectoryHelper.EnumerateFiles(Paths.GameRootPath)
            .Where(file => MELONLOADER_FILE_NAMES.Contains(Path.GetFileName(file).ToLowerInvariant()));

        if (melonLoaderFolder is not null ||
            emptyMelonloaderModFolders.Any() ||
            melonLoaderFiles.Any())
        {
            // old melonloader files and folders are left behind, offer to purge them

            logger.LogInfo("Old MelonLoader installation detected.");

            var folders = ((IEnumerable<string?>)[melonLoaderFolder, .. emptyMelonloaderModFolders])
                .Where(folder => folder is not null)
                .Cast<string>();

            var folderStrings = folders.Select(path => $"  • {Path.GetFileName(path)}");

            var fileStrings = melonLoaderFiles.Select(path => $"  • {Path.GetFileName(path)}");

            var tree = string.Join("\r\n", [.. folderStrings, .. fileStrings]);

            var purgeMelonLoader = PInvoke.MessageBox(
                ActiveWindow,
                "The following files and folders were left behind by a previous MelonLoader installation:\r\n\r\n" +
                $"{tree}\r\n\r\n" +
                "Would you like to permanently delete them?",
                "Purge MelonLoader installation?",
                MESSAGEBOX_STYLE.MB_YESNO |
                MESSAGEBOX_STYLE.MB_TASKMODAL |
                MESSAGEBOX_STYLE.MB_ICONWARNING |
                MESSAGEBOX_STYLE.MB_TOPMOST);

            if (purgeMelonLoader == MESSAGEBOX_RESULT.IDYES)
            {
                logger.LogMessage("Purging MelonLoader installation...");

                try
                {
                    foreach (var folder in folders)
                    {
                        logger.LogDebug($"Deleting \"{folder}\"...");
                        Directory.Delete(folder, true);
                    }

                    foreach (var file in melonLoaderFiles)
                    {
                        logger.LogDebug($"Deleting \"{file}\"...");
                        File.Delete(file);
                    }

                    logger.LogInfo("MelonLoader purge complete.");
                }
                catch (Exception e)
                {
                    logger.LogError(e);
                    logger.LogWarning("Failed to purge MelonLoader installation!");
                }
            }
            else
            {
                logger.LogDebug("User chose not to purge MelonLoader.");
            }
        }
    }

    private static void MigrateMelonLoaderModFiles(IEnumerable<string> melonloaderModFolders)
    {
        using var logger = Logger.CreateLogSource("Tobey's BMW");
        var mlLoaderPath = Path.Combine(Paths.GameRootPath, "MLLoader");

        Directory.CreateDirectory(mlLoaderPath);

        foreach (var folder in melonloaderModFolders)
        {
            var isDirectory = Directory.Exists(folder);

            var absoluteFolderPath = Path.GetFullPath(DirectoryHelper.ResolveLinkTargetOrPath(Path.GetFullPath(folder)));
            var index = absoluteFolderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - (isDirectory ? 1 : 2);

            var filesToMigrate = DirectoryHelper.EnumerateFiles(absoluteFolderPath, "*", SearchOption.AllDirectories)
                .Where(file => !IsMLPlugin(file) && !IsMLDisabler(file));

            foreach (var file in filesToMigrate)
            {
                var absoluteFilePath = Path.GetFullPath(file);

                var trailingPath = absoluteFilePath
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Skip(index)
                    .Aggregate(Path.Combine);

                var destinationPath = Path.Combine(
                    mlLoaderPath,
                    isDirectory
                        ? trailingPath
                        : Path.Combine(Path.GetFileName(folder), trailingPath));

                logger.LogDebug($"Migrating \"{absoluteFilePath}\" to \"{destinationPath}\"...");

                if (!File.Exists(destinationPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.Move(absoluteFilePath, destinationPath);
                    continue;
                }

                var sourceTime = File.GetLastWriteTimeUtc(absoluteFilePath);
                var destTime = File.GetLastWriteTimeUtc(destinationPath);

                if (destTime > sourceTime)
                {
                    logger.LogInfo($"A newer file already exists at \"{destinationPath}\"");

                    var overwriteDialogResult = PInvoke.MessageBox(
                        ActiveWindow,
                        "A newer file already exists at path:\r\n\r\n" +
                        $"\"{destinationPath}\"\r\n\r\n" +
                        "Would you like to overwrite it?",
                        "A newer file already exists at destination",
                        MESSAGEBOX_STYLE.MB_YESNO |
                        MESSAGEBOX_STYLE.MB_TASKMODAL |
                        MESSAGEBOX_STYLE.MB_ICONWARNING |
                        MESSAGEBOX_STYLE.MB_TOPMOST);

                    if (overwriteDialogResult == MESSAGEBOX_RESULT.IDYES)
                    {
                        logger.LogMessage($"Overwriting \"{destinationPath}\"...");
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        File.Copy(absoluteFilePath, destinationPath, true);
                        File.Delete(absoluteFilePath);
                    }
                    else
                    {
                        logger.LogDebug("User chose not to overwrite.");
                    }
                }
                else
                {
                    logger.LogMessage($"Overwriting \"{destinationPath}\"...");
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.Copy(absoluteFilePath, destinationPath, true);
                    File.Delete(absoluteFilePath);
                }
            }

            if (!DirectoryHelper.EnumerateFiles(absoluteFolderPath, "*", SearchOption.AllDirectories).Any())
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder);
                }
                else if (DirectoryHelper.IsSymbolicLink(folder))
                {
                    File.Delete(folder);
                }
            }
        }
    }

    public static readonly string[] MLLOADER_FOLDER_NAMES = ["mods", "plugins", "userdata", "userlibs"];
    public static readonly string[] MELONLOADER_FILE_NAMES = ["dobby.dll", "notice.txt"];

    public static IEnumerable<string> GetMelonLoaderModFolders(string path) =>
        GetMelonLoaderModFolders(DirectoryHelper.EnumerateFileSystemEntries(DirectoryHelper.ResolveLinkTargetOrPath(path)));

    public static IEnumerable<string> GetMelonLoaderModFolders(IEnumerable<string> entries) =>
        entries
            .Where(entry =>
                MLLOADER_FOLDER_NAMES.Contains(Path.GetFileName(entry).ToLowerInvariant()) &&
                (Directory.Exists(entry) || (File.Exists(entry) && DirectoryHelper.IsSymbolicLink(entry))));
}