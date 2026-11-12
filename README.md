# RadioRamen

Japanese radio and your own MP3s for **Ramen Shop Simulator (Windows/Steam)**. Includes 42 stations across 13 categories, custom streams and favourites.

[Download RadioRamen](https://github.com/Darojax/RadioRamen/releases/latest)

## Installation

1. Close the game.
2. Install [BepInEx 5 Windows x64](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.6) in the game folder containing `Ramen Shop Simulator.exe`. Launch once, then close the game. Skip this step if BepInEx is already installed.
3. Extract the RadioRamen ZIP into the game folder, merging the `BepInEx` folders.
4. Launch the game and load your save.

When updating, keep `RadioRamen.ini` to retain your settings. Replace `Stations.ini` for the updated station list. Keep `UserStations.ini` for your custom feeds and favourites.

## Controls

- **Left click the radio:** open Radio Controls.
- **E:** turn the radio on or off. MP3 playback pauses when switched off.
- **Internet radio:** choose a station, or use search, category and region filters. Use the stars to save favourites.
- **Add station / Edit:** save a direct HTTP(S) audio stream URL or edit your custom feeds.
- **Your music:** choose an MP3. Shuffle, repeat and next track are available.
- **Volume / Mute:** adjust the radio independently of other sounds.
- **Radio sound:** choose Clean, FM or AM.
- **Audible distance:** Normal has a wide range; Nearby fades sooner. Lower volume carries less far in both modes.
- **Right click / Escape / X:** close the menu. The shop keeps running while the menu is open.

The last station or MP3 and its playback position are remembered between sessions. MP3s pause with the game; live radio is muted. The menu remembers each tab's browsing position during the session. A quiet tuning sound plays while a radio station connects.

## Your MP3s

Copy MP3 files directly into:

```text
BepInEx/plugins/RadioRamen/Music/
```

Choose **Your music**, then **Refresh**. Subfolders are not scanned.

Settings are saved in `BepInEx/plugins/RadioRamen/RadioRamen.ini`.

Audio uses [BASS and BASSHLS](https://www.un4seen.com/bass.html) by Un4seen Developments. Their notices are included in `BepInEx/RadioRamen/licenses/`.

## Building from source

Requires **Windows x64**, the installed game, the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), [Git](https://git-scm.com/downloads/win) and [FFmpeg](https://ffmpeg.org/download.html). Make `dotnet`, `git` and `ffmpeg` available on your PATH. FFmpeg needs the `libmp3lame` encoder to generate the audio test files.

In PowerShell, run:

```powershell
git clone https://github.com/Darojax/RadioRamen.git
cd RadioRamen
.\scripts\Build.ps1 -GameRoot 'D:\SteamLibrary\steamapps\common\Ramen Shop Simulator'
```

Replace `-GameRoot` with your game folder. If FFmpeg is not on PATH, also pass `-Ffmpeg 'C:\Tools\ffmpeg\bin\ffmpeg.exe'`.

The script downloads and hash-verifies the pinned BepInEx, BASS and BASSHLS dependencies, builds the DLL, runs the automated checks and creates **`dist/RadioRamen-0.2.2.zip`**. Game libraries are read from your installation and are not redistributed. The ZIP includes the required audio libraries and their notices; install it using the instructions above.

Optional switches: `-ManagedHttp` checks the managed HTTP audio path; `-LiveStreams` also checks playback from the bundled internet stations and requires a working internet connection.
