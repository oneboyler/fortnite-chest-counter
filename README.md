# Fortnite Chest Counter

A small see-through overlay that counts the chests you open or break in Fortnite, straight from your replay files. It also counts the season treasures (gizmos and awards) that came out of your chests.

It sits in the tray and updates a few seconds after each game ends.

## Download

Get **ChestCounter-win-x64.zip** from [Releases](https://github.com/oneboyler/fortnite-chest-counter/releases/latest), unzip it anywhere (not in Program Files) and run **Chest Counter.exe**. There's nothing to install.

Windows may say "Windows protected your PC" the first time, because the app isn't signed. Click **More info > Run anyway**.

On the first start, a setup window checks two things and explains the keys:
- **Replays:** Fortnite must record them. In Fortnite, turn this on under Settings > Game > Replays (Record Replays).
- **Window mode:** Fortnite needs Windowed Fullscreen. The overlay can't show over real Fullscreen.

## Keys

| Key | What it does |
| --- | --- |
| F8 | Hide / show the overlay |
| Shift+F8 | Move it: drag it with the mouse, then Shift+F8 again to lock it |
| Ctrl+F8 | Switch between the numbers and a graph of chests per game |

Right-click the tray icon for the full report, Start with Windows, and more.

## What it counts

- **Opened chests:** exact. The replay records who opened every chest.
- **Destroyed chests:** worked out from rockets, hits, or whoever was standing closest.
- **Season treasure:** every prize gives your character a hidden "prize handler" in the replay.
  - Prizes that line up with one of your own chests count as yours.
  - Teammates' finds (a squad shares prizes) are listed separately in the full report.
  - The replay doesn't say which prize it was. Awards other than gizmos drop once each, so nearly all are gizmos.
- **Custom games:** left out.

## Privacy

It only reads the replay files in `%LOCALAPPDATA%\FortniteGame\Saved\Demos` and writes its own `data` folder next to the exe. It has no network code and sends nothing anywhere.

## Building

You need the .NET 9 SDK. `build.bat` builds a version that uses the installed .NET runtime. `release.bat` builds the self-contained zip.

The replay parsing is a modified copy of [Shiqan/FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor) (MIT, see `vendor/FortniteReplayDecompressor/LICENSE`).
