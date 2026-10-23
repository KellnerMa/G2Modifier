# Grandia II Modifier

Standalone Windows editor for **Grandia II HD Remaster** and **Grandia II Anniversary Edition** (Steam, PC).
It edits the game's data tables, Millenia's Rage (patches in `grandia2.exe`) and character portraits.
Every file is backed up before the first change (SHA-256 manifest), only changed bytes are written, file sizes never change,
and everything can be restored to the original.

This repository contains the complete source code of the release published on Nexus Mods.
A ready-made `G2Modifier.exe` (current version) is in the `release` folder, together with its SHA-256 checksum.

## Build

Requirements: Windows 10/11 with .NET Framework 4.x (included in Windows). No Visual Studio, no NuGet packages, no other dependencies.

1. Download the whole repository (**Code > Download ZIP**) and extract it. `build.cmd` alone is not enough, it needs the `src` and `lang` folders next to it.
2. Run `build.cmd` (double-click, or from a command prompt in this folder).
3. The result is `G2Modifier.exe` in the same folder.

`build.cmd` uses the C# compiler that ships with Windows
(`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and embeds the language files from `lang\` as resources:

```
csc.exe /nologo /target:winexe /codepage:65001 /out:G2Modifier.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /resource:lang\en.txt,G2Modifier.lang.en.txt /resource:lang\fr.txt,G2Modifier.lang.fr.txt /resource:lang\de.txt,G2Modifier.lang.de.txt /recurse:src\*.cs
```

Note: this legacy compiler does not produce bit-identical builds (timestamp and module ID differ per build),
so a self-built exe will have a different hash than the release exe while being functionally identical.

## What the program does (and does not do)

- Reads and writes only files inside the game folder you select, and backups inside a backup folder you select (must be outside the game folder).
- Settings: `%APPDATA%\G2Modifier\settings.ini`, error log: `%APPDATA%\G2Modifier\fehler.log`.
- No network access, no installer, no registry changes, no background processes.
  The only other processes it touches: it opens Windows Explorer on the backup folder when you click that button,
  and it checks whether `grandia2.exe` is running before saving (to warn you).
- Patching `grandia2.exe` (Rage page) only happens after every patch location has been verified against the expected byte pattern; unknown exe versions are refused.

## Structure

| Folder / file | Content |
|---|---|
| `src\Core` | file formats, backup, Rage patches (`RagePatch.cs` HD Remaster, `RagePatchAE.cs` Anniversary Edition), DDS codec, item tools |
| `src\UI` | Windows Forms interface |
| `lang` | English / French / German UI texts (`build_lang.py` is the source of the translations) |
| `tests` | test programs used during development (run against copies of real game files) |
| `release` | prebuilt `G2Modifier.exe` and `SHA256.txt` |
| `LIESMICH.txt` | full German readme and change log |

## Changes in 0.13.3

- Item and move descriptions now reach the game: the editor shows the `sys` copy first (the text the game displays) and writes text changes to all copies. `xls_data` contains an older wording for 90 item descriptions, which previously blocked the sync.
- `sys_hardmode\ms_param.bin` is no longer reported as locked. The original file has placeholder bytes (`30 F0`) instead of names and descriptions (same in both game versions); its numbers are real hard-mode values. Names are shown from `sys`, the placeholders stay untouched.
- `build.cmd` now runs from its own folder and explains when `src` is missing (instead of compiler error CS5001).

## Credits

Data layouts and label lists are based on [G2DataGUI](https://github.com/krogenth/G2DataGUI) by krogenth (MIT License, see `LICENSE-G2DataGUI.txt`).

Unofficial fan project, not affiliated with the game's developers or publishers.
