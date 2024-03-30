using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Tobey.BepInExMelonLoaderWizard;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

var processName = args.FirstOrDefault();
var exe = args.ElementAtOrDefault(1);
var launchArgs = args.Skip(2);

var log = new StringBuilder();
int exitCode = -1;


void Log<T>(T message) => log.AppendLine(message?.ToString() ?? $"null {typeof(T)}");

AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

try
{
    using var tokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

    Log($"Launch arguments:{Environment.NewLine}{string.Join(Environment.NewLine, args.Select(arg => $"- \"{arg}\""))}");

    if (processName is null)
    {
        Log("Process not specified.");
        return exitCode = 1;
    }

    var gameRootPath = Directory.GetCurrentDirectory();

    var proxyDlls = ProxyHelper.GetMelonLoaderProxyDlls(gameRootPath);

    if (!proxyDlls.Any())
    {
        Log($"No MelonLoader proxy dlls found at \"{gameRootPath}\"");
        return exitCode = 0;
    }

    var me = Process.GetCurrentProcess();
    var processes =
        ((Process[])[
            .. Process.GetProcessesByName(processName),
            .. string.IsNullOrEmpty(exe)
                ? Enumerable.Empty<Process>()
                : Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))])
            .DistinctBy(process => process.Id)
            .Where(process => process.Id != me.Id); // rule out the possibility of waiting forever for ourself to exit, lol

    if (processes.Any())
    {
        Log("Matching processes:");

        foreach (var process in processes)
        {
            Log($"- {process.Id}");
        }

        Log("Waiting for processes to exit to continue...");
        await Task.WhenAll(processes.Select(process => process.WaitForExitAsync(tokenSource.Token)));
        Log("Processes exited, continuing.");
    }
    else
    {
        Log("No matching processes found.");
    }

    await Task.WhenAll(ProxyHelper.GetMelonLoaderProxyDlls(gameRootPath).Select(proxy => RetryDelete(proxy, 200, tokenSource.Token)));
    return exitCode = 0;
}
catch (Exception e)
when (e is OperationCanceledException or TaskCanceledException)
{
    Log("Timed out while attempting to delete MelonLoader proxy DLLs.");
    return exitCode = 1;
}
catch (Exception e)
{
    Log(e);
    return exitCode = 1;
}

async Task RetryDelete(string path, int retryDelay, CancellationToken token)
{
    if (token.IsCancellationRequested) throw new TaskCanceledException();

    try
    {
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
        Log($"Deleted \"{path}\"");
    }
    catch
    {
        await Task.Delay(retryDelay, token);
        await RetryDelete(path, retryDelay, token);
    }
}

void CurrentDomain_ProcessExit(object? _, EventArgs __)
{
    AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;

    if (exitCode != 0)
    {
        Log("Failed to automatically delete MelonLoader proxy DLLs. Manual uninstallation required.");

        PInvoke.MessageBox(
            HWND.Null,
            "An error occurred while trying to disable MelonLoader.\r\n\r\n" +
            "You will need to manually uninstall MelonLoader to use BepInEx and BepInEx mods.\r\n\r\n" +
            "Press OK to open the instructions on uninstalling MelonLoader.",
            "Manually MelonLoader uninstallation required.",
            MESSAGEBOX_STYLE.MB_OK |
            MESSAGEBOX_STYLE.MB_TASKMODAL |
            MESSAGEBOX_STYLE.MB_ICONERROR |
            MESSAGEBOX_STYLE.MB_TOPMOST);

        using var ___ = Process.Start(new ProcessStartInfo("cmd", "/c start \"\" \"https://github.com/LavaGang/MelonLoader.Installer#how-to-un-install-melonloader\"")
        {
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
    else if (exe is not null)
    {
        Log($"Relaunching \"{exe}\"...");

        string[] cmdArgs = [
            "/c",
            $"start \"\" \"{exe}\"",
            ..launchArgs
        ];

        var doorstopEnvArgNames = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Select(x => x.Key as string)
            .Where(x => x?.StartsWith("DOORSTOP_") ?? false)
            .Cast<string>();

        foreach (var name in doorstopEnvArgNames)
        {
            Environment.SetEnvironmentVariable(name, null);
        }

        using var ___ = Process.Start(new ProcessStartInfo("cmd", string.Join(" ", cmdArgs))
        {
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    Log($"[ exit ] {exitCode}");

    File.WriteAllText($"{Assembly.GetExecutingAssembly().GetName().Name}.log", log.ToString());
}