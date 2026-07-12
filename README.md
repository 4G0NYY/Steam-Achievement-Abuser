# Steam Achievement Abuser

**2026 Revamp by 4G0NYY** — now a single, self-contained CLI built on modern .NET 10.

- Based on: https://github.com/gibbed/SteamAchievementManager

# How does it work?
It works basically identical to the Steam Achievement Manager that can be found on Github, too. But instead of you having to manually go through each game, it automates that for you. In order to prevent Steam from crashing at around ~800 finished Games, there's a (configurable) delay between each game that's "abused".

Under the hood the tool briefly re-launches itself once per game — each run sets its own Steam app context before unlocking that game's achievements. This used to be a second `Steam Achievement Abuser App.exe`; as of the 2026 revamp it's all folded into the one executable.

# What is it?
This Program is the fastest (reliable) way of getting every single Achievement for every single one of your games on Steam.

# How to use:
1. Download the latest release [from here](https://github.com/4G0NYY/Steam-Achievement-Abuser/releases)
2. Unpack it into some folder
3. Start Steam (and make sure you're logged in)
4. Start `Steam Achievement Abuser.exe`
5. Pick a pause between games (or just press Enter for the default 5000 ms)
6. When it asks `N games found, want to start? (y/n)`, type `y` and watch the progress bar do its thing!
# Done!

Enjoy! :)

# You don't trust my pre-compiled .exe?
No Problem! After all, it's always more secure to compile a program yourself. In order to compile this little tool, you will need:
1. The [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (or newer)

## How to compile:
1. Install the .NET SDK.
2. Download the Source Code and unzip it into a folder.
3. Open a terminal and `cd` into the `src` directory (e.g. `cd C:\Users\testuser\Desktop\SteamAchievementAbuser\src`).
4. Run: `dotnet build "Steam Achievement Abuser\Steam Achievement Abuser.csproj" -c Release`
5. The build only takes a few seconds. You'll then find everything in `Steam Achievement Abuser\bin\Release\net10.0-windows\`.
6. You only need two files: `SAM.API.dll` and `Steam Achievement Abuser.exe`. (The separate "App" executable is gone — it's all one exe now.)
7. Run `Steam Achievement Abuser.exe` and it should work!

### Want a single portable .exe with no runtime install?
Publish it self-contained for 32-bit Windows (the tool must be 32-bit because Steam's `steamclient.dll` is). This bundles the whole .NET runtime into **one compressed `Steam Achievement Abuser.exe`** (~33 MB) — no other files needed:

```
dotnet publish "Steam Achievement Abuser\Steam Achievement Abuser.csproj" -c Release -r win-x86 --self-contained
```

This is exactly what the release pipeline ships, so the download from Releases is just that single .exe.

> **Note:** The tool always runs as a 32-bit (x86) process — that's required to talk to Steam's 32-bit client library.
