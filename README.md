# RadioRamen

Japanese radio and your own MP3s for **Ramen Shop Simulator (Windows/Steam)**. Includes 42 stations across 13 categories, custom streams and favourites.

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
- **Audible distance:** Normal has a wide range; Nearby fades sooner as you walk away.
- **Escape / X:** close the menu. The shop keeps running while the menu is open.

The last station or MP3 and its playback position are remembered between sessions. MP3s pause with the game; live radio is muted. The menu remembers each tab's browsing position during the session.

## Your MP3s

Copy MP3 files directly into:

```text
BepInEx/plugins/RadioRamen/Music/
```

Choose **Your music**, then **Refresh**. Subfolders are not scanned.

Settings are saved in `BepInEx/plugins/RadioRamen/RadioRamen.ini`.

Audio uses [BASS and BASSHLS](https://www.un4seen.com/bass.html) by Un4seen Developments. Their notices are included in `BepInEx/RadioRamen/licenses/`.
