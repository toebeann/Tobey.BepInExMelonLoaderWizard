using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tobey.BepInExMelonLoaderWizard;

public class Patcher
{
    // Without the contents of this region, the patcher will not be loaded by BepInEx - do not remove!
    #region BepInEx Patcher Contract
    public static IEnumerable<string> TargetDLLs { get; } = [];
    public static void Patch(AssemblyDefinition _) { }
    #endregion

    private const string ENUMERATE_FILE_SYSTEM_ENTRIES = "EnumerateFileSystemEntries";
    private const string ENUMERATE_FILES = "EnumerateFiles";
    private const string RESOLVE_LINK_TARGET = "ResolveLinkTarget";

    private static Traverse directoryTraversal;
    private static Traverse DirectoryTraversal => directoryTraversal ??= Traverse.Create(typeof(Directory));

    private static HWND? activeWindow;
    private static HWND ActiveWindow => activeWindow ??= PInvoke.GetActiveWindow();

    private static readonly ManualLogSource logger = Logger.CreateLogSource("Tobey's BMW");

    public static string ResolveLinkTarget(string path)
    {
        var resolveLinkTarget = DirectoryTraversal.Method(RESOLVE_LINK_TARGET, [typeof(string), typeof(bool)]);
        if (resolveLinkTarget.MethodExists())
        {
            return resolveLinkTarget.GetValue<FileSystemInfo>(path, true)?.FullName;
        }

        if (!File.Exists(path))
        {
            throw new IOException();
        }

        using var h = PInvoke.CreateFile(
            path,
            0x08,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE | FILE_SHARE_MODE.FILE_SHARE_DELETE,
            null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_FLAG_BACKUP_SEMANTICS,
            null);

        if (h.IsInvalid)
        {
            throw new IOException();
        }

        PWSTR targetPath = new();
        if (PInvoke.GetFinalPathNameByHandle(h, targetPath, 1024, 0) == 0)
        {
            throw new IOException();
        }

        return targetPath.ToString();
    }

    public static bool TryResolveLinkTarget(string path, out string resolved)
    {
        try
        {
            resolved = ResolveLinkTarget(path);
            return true;
        }
        catch
        {
            resolved = null;
            return false;
        }
    }

    public static string ResolveLinkTargetOrPath(string path) => TryResolveLinkTarget(path, out string resolved) switch
    {
        true => resolved,
        false => path
    };

    private static IEnumerable<string> EnumerateFiles(string path) =>
        DirectoryTraversal.Method(ENUMERATE_FILES, [typeof(string)]) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path),
            _ => Directory.GetFiles(path)
        };

    private static IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        DirectoryTraversal.Method(ENUMERATE_FILES, [typeof(string), typeof(string), typeof(SearchOption)]) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path, searchPattern, searchOption),
            _ => Directory.GetFiles(path, searchPattern, searchOption)
        };

    private static IEnumerable<string> EnumerateFileSystemEntries(string path) =>
        DirectoryTraversal.Method(ENUMERATE_FILE_SYSTEM_ENTRIES, [typeof(string)]) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path),
            _ => Directory.GetFileSystemEntries(path)
        };

    private static readonly string[] MLLOADER_FOLDER_NAMES = ["mods", "plugins", "userdata", "userlibs"];
    private static readonly string[] MELONLOADER_FILE_NAMES = ["dobby.dll", "notice.txt"];

    private static bool IsSymbolicLink(string path) => (new FileInfo(path).Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

    // entry point
    public static void Initialize()
    {
        if (!PlatformHelper.Is(Platform.Windows))
        {
            logger.LogWarning("Incompatible platform detected, bailing out.");
            return;
        }

        var entries = EnumerateFileSystemEntries(Paths.GameRootPath);

        var mlLoaderFolders = entries
            .Where(entry =>
                MLLOADER_FOLDER_NAMES.Contains(Path.GetFileName(entry).ToLowerInvariant()) &&
                (Directory.Exists(entry) || IsSymbolicLink(entry)));

        var mlLoaderFoldersWithFiles = mlLoaderFolders
            .Where(folder => folder switch
            {
                string path when Directory.Exists(path) => EnumerateFiles(path, "*", SearchOption.AllDirectories).Any(),
                _ => true, // it's a symbolic link, we'll just treat it as if it contains files
            });

        if (mlLoaderFoldersWithFiles.Any())
        {
            // melonloader mod files have been detected, we should offer to migrate them for the user

            logger.LogInfo("MelonLoader mod files were detected.");

            var mirationDialogResult = PInvoke.MessageBox(
                ActiveWindow,
                "MelonLoader mod files which require migration have been detected.\r\n\r\n" +
                "Would you like to automatically migrate them?",
                "MelonLoader migration required",
                MESSAGEBOX_STYLE.MB_YESNO |
                MESSAGEBOX_STYLE.MB_TASKMODAL |
                MESSAGEBOX_STYLE.MB_ICONQUESTION |
                MESSAGEBOX_STYLE.MB_TOPMOST);

            if (mirationDialogResult == MESSAGEBOX_RESULT.IDYES)
            {
                logger.LogMessage("Migrating MelonLoader mod files...");

                if (MigrateMelonLoaderModFiles(mlLoaderFoldersWithFiles))
                {
                    logger.LogInfo("Migration complete.");
                }
                else
                {
                    logger.LogWarning("Failed to migrate MelonLoader mod files!");
                }
            }
            else
            {
                logger.LogDebug("User chose not to migrate MelonLoader mod files.");
            }
        }

        var melonLoaderFolder = entries
            .SingleOrDefault(entry =>
                Path.GetFileName(entry).ToLowerInvariant() == "melonloader" &&
                (Directory.Exists(entry) || IsSymbolicLink(entry)));

        var melonLoaderFiles = EnumerateFiles(Paths.GameRootPath)
            .Where(file => MELONLOADER_FILE_NAMES.Contains(Path.GetFileName(file).ToLowerInvariant()));

        if (melonLoaderFolder is not null ||
            mlLoaderFolders.Any() ||
            melonLoaderFiles.Any())
        {
            // old melonloader files and folders are left behind, offer to purge them

            logger.LogInfo("Old MelonLoader installation detected.");

            var folders = ((IEnumerable<string>)[melonLoaderFolder, .. mlLoaderFolders])
                .Where(folder => folder is not null);

            var folderStrings = folders.Select(path => $"  • {Path.GetFileName(path)}");

            var fileStrings = melonLoaderFiles.Select(path => $"  • {Path.GetFileName(path)}");

            var tree = string.Join("\r\n", [.. folderStrings, .. fileStrings]);

            var purgeMelonLoader = PInvoke.MessageBox(
                ActiveWindow,
                "The following files and folders were left behind by a previous MelonLoader installation:\r\n\r\n" +
                $"{tree}\r\n\r\n" +
                "Would you like to permanently delete them?\r\n\r\n" +
                "This should be safe to do as long as you have migrated your MelonLoader mod files.",
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
                        logger.LogDebug($"Deleteing \"{file}\"...");
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

    private static bool MigrateMelonLoaderModFiles(IEnumerable<string> mlLoaderfolders)
    {
        try
        {
            var mlLoaderPath = Path.Combine(Paths.GameRootPath, "MLLoader");

            Directory.CreateDirectory(mlLoaderPath);

            foreach (var folder in mlLoaderfolders)
            {
                var isDirectory = Directory.Exists(folder);

                var absoluteFolderPath = Path.GetFullPath(
                    isDirectory
                        ? folder
                        : ResolveLinkTarget(Path.GetFullPath(folder)));
                var index = absoluteFolderPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length - (isDirectory ? 1 : 2);

                foreach (var file in EnumerateFiles(absoluteFolderPath, "*", SearchOption.AllDirectories))
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
            }

            return true;
        }
        catch (Exception e)
        {
            logger.LogError(e);
            return false;
        }
    }

    // clean up
    public static void Finish() => logger.Dispose();
}
