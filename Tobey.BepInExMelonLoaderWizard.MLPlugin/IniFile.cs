﻿#nullable enable

using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Tobey.BepInExMelonLoaderWizard;
internal class IniFile(string iniPath) // modified from https://stackoverflow.com/a/14906422
{
    string Path = new FileInfo(iniPath + ".ini").FullName;

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern long WritePrivateProfileString(string Section, string? Key, string? Value, string FilePath);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

    public string Read(string Key, string Section)
    {
        var RetVal = new StringBuilder(255);
        GetPrivateProfileString(Section , Key, "", RetVal, 255, Path);
        return RetVal.ToString();
    }

    public void Write(string? Key, string? Value, string Section)
    {
        WritePrivateProfileString(Section, Key, Value, Path);
    }

    public void DeleteKey(string Key, string Section)
    {
        Write(Key, null, Section);
    }

    public void DeleteSection(string Section)
    {
        Write(null, null, Section);
    }

    public bool KeyExists(string Key, string Section)
    {
        return Read(Key, Section).Length > 0;
    }
}
