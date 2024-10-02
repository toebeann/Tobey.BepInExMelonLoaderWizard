#nullable enable

using HarmonyLib;
using MonoMod.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;

namespace Tobey.BepInExMelonLoaderWizard;
internal static class DirectoryHelper
{
    private static Traverse? directoryTraversal;
    private static Traverse DirectoryTraversal => directoryTraversal ??= Traverse.Create(typeof(Directory));

    private static T SuppressHarmonyWarnings<T>(Func<T> fn)
    {
        var initialFilter = HarmonyLib.Tools.Logger.ChannelFilter;
        HarmonyLib.Tools.Logger.ChannelFilter &= ~HarmonyLib.Tools.Logger.LogChannel.Warn;
        try
        {
            return fn();
        }
        finally
        {
            HarmonyLib.Tools.Logger.ChannelFilter = initialFilter;
        }
    }

    public static IEnumerable<string> EnumerateDirectories(string path) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateDirectories), [typeof(string)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path),
            _ => Directory.GetDirectories(path)
        };

    public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateDirectories), [typeof(string), typeof(string)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path, searchPattern),
            _ => Directory.GetDirectories(path, searchPattern)
        };

    public static IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateDirectories), [typeof(string), typeof(string), typeof(SearchOption)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path, searchPattern, searchOption),
            _ => Directory.GetDirectories(path, searchPattern, searchOption)
        };

    public static IEnumerable<string> EnumerateFiles(string path) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateFiles), [typeof(string)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path),
            _ => Directory.GetFiles(path)
        };

    public static IEnumerable<string> EnumerateFiles(string path, string searchPattern) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateFiles), [typeof(string), typeof(string)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path, searchPattern),
            _ => Directory.GetFiles(path, searchPattern)
        };

    public static IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateFiles), [typeof(string), typeof(string), typeof(SearchOption)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path, searchPattern, searchOption),
            _ => Directory.GetFiles(path, searchPattern, searchOption)
        };

    public static IEnumerable<string> EnumerateFileSystemEntries(string path) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateFileSystemEntries), [typeof(string)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path),
            _ => Directory.GetFileSystemEntries(path)
        };

    public static IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateFileSystemEntries), [typeof(string), typeof(string)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path, searchPattern),
            _ => Directory.GetFileSystemEntries(path, searchPattern)
        };

    public static IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption) =>
        SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(EnumerateFileSystemEntries), [typeof(string), typeof(string), typeof(SearchOption)])) switch
        {
            Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path),
            _ => SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(Directory.GetFileSystemEntries), [typeof(string), typeof(string), typeof(SearchOption)])) switch
            {
                Traverse t when t.MethodExists() => t.GetValue<IEnumerable<string>>(path, searchPattern, searchOption),
                _ => EnumerateDirectories(path, searchPattern, searchOption).Concat(EnumerateFiles(path, searchPattern, searchOption))
            }
        };

    public static bool IsSymbolicLink(string path) => (new FileInfo(path).Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;

    public static string ResolveLinkTarget(string path)
    {
        var resolveLinkTarget = SuppressHarmonyWarnings(() => DirectoryTraversal.Method(nameof(ResolveLinkTarget), [typeof(string), typeof(bool)]));

        if (resolveLinkTarget?.MethodExists() ?? false)
        {
            return resolveLinkTarget.GetValue<FileSystemInfo?>(path, true) switch
            {
                FileSystemInfo info => info.FullName,
                _ when Directory.Exists(path) => path,
                _ => throw new FileNotFoundException()
            };
        }

        if (!File.Exists(path))
        {
            throw new IOException();
        }

        if (!PlatformHelper.Is(Platform.Windows))
        {
            throw new NotSupportedException();
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

    public static bool TryResolveLinkTarget(string path, out string? resolved)
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

    public static string ResolveLinkTargetOrPath(string path) => TryResolveLinkTarget(path, out string? resolved) switch
    {
        true => resolved!,
        false => path
    };

    public static bool HasFiles(string path) => ResolveLinkTargetOrPath(path) switch
    {
        string dir => EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any(),
        _ => throw new DirectoryNotFoundException()
    };
}
