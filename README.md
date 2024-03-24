# Tobey's BepInEx MelonLoader Wizard 🚗

BMW is a BepInEx patcher which takes care of migrating a user from MelonLoader to BepInEx.

Intended for use in conjunction with [BepInEx.MelonLoader.Loader](https://github.com/BepInEx/BepInEx.MelonLoader.Loader/).

## Features

-   Detects MelonLoader mod files located in the `Mods`, `Plugins` etc. folders, and prompts the user to move them to the
    appropriate `MLLoader` subfolders.
-   Detects core files and folders left behind by MelonLoader, and prompts the user to delete them.

## Usage

BMW is intended to be included alongside the BepInEx 5 build of [BepInEx.MelonLoader.Loader](https://github.com/BepInEx/BepInEx.MelonLoader.Loader/),
e.g. as part of a BepInEx pack for games where both MelonLoader and BepInEx mods are in use by the community.

In this scenario, it is recommended to ship a copy of BepInEx's `winhttp.dll` renamed to `version.dll`, as this is the default
proxy DLL that MelonLoader ships with. If the user does not delete, rename or overwrite the MelonLoader proxy with the
BepInEx doorstop proxy, BepInEx will not load, and neither will BMW.

It's perfectly fine to include both `winhttp.dll` and `version.dll` from BepInEx's doorstop side-by-side, as the doorstop
will only inject BepInEx once regardless.

This way, when the user installs your BepInEx pack _after_ installing MelonLoader, BepInEx and BMW will be loaded.

## Caveats

-   If a user later (re-)installs MelonLoader, BepInEx and BMW will stop working. It is planned to add a MelonLoader mod/plugin
    in the future which will not be migrated to the `MLLoader` folder. In this way, in the event that MelonLoader has taken
    precedence, it will kick-in and help them fix their mistake.

-   Native Unix games are not currently supported:\
    BMW's prompts rely on Win32 API calls to display message boxes, and as such will only work with Windows games - though it
    _should_ work for Unix users when running a Windows game through Proton or Wine etc., although this is currently untested.

-   Only BepInEx 5 is supported.
