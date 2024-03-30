# Tobey's BepInEx MelonLoader Wizard 🚘

BMW is a BepInEx patcher which takes care of migrating a user from MelonLoader to BepInEx.

Intended for use in conjunction with [BepInEx.MelonLoader.Loader](https://github.com/BepInEx/BepInEx.MelonLoader.Loader/).

## Features

-   Detects MelonLoader mod files located in the `Mods`, `Plugins` etc. folders, and prompts the user to move them to the
    appropriate `MLLoader` subfolders.
-   Detects core files and folders left behind by MelonLoader, and prompts the user to delete them.
-   Detects MelonLoader installed on top of an existing BepInEx & [BepInEx.MelonLoader.Loader](https://github.com/BepInEx/BepInEx.MelonLoader.Loader/)
    installation and prompts the user to uninstall it.

## Usage

BMW is intended to be included alongside the BepInEx 5 build of [BepInEx.MelonLoader.Loader](https://github.com/BepInEx/BepInEx.MelonLoader.Loader/),
e.g. as part of a BepInEx pack for games where both MelonLoader and BepInEx mods are in use by the community.

In this scenario, it is recommended to ship a copy of BepInEx's `winhttp.dll` renamed to `version.dll`, so that when a user
installs your BepInEx pack _after_ having already installed MelonLoader, the default MelonLoader proxy assembly will be
replaced with the UnityDoorstop proxy from BepInEx, enabling BepInEx & BMW to load instead of MelonLoader.

## Caveats

-   Native Unix games are not currently supported:\
    BMW's prompts rely on Win32 API calls to display message boxes, and as such will only work with Windows games - though it
    _should_ work for Unix users when running a Windows game through Proton or Wine etc., although this is currently untested.

-   Only BepInEx 5 is supported.
