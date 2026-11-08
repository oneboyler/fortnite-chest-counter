FORTNITE CHEST COUNTER
======================

Reads your Fortnite replay files and counts the chests you opened or
destroyed, plus any "treasure" that came out of them.

HOW TO USE
  Chest Counter.exe   Double-click. A small see-through overlay appears on top of
                      the game and a chest icon appears in the tray. Leave it running. A few seconds
                      after every game ends it reads the replay, adds the game and
                      flashes "+67" next to the count. (It can't count during a game:
                      Fortnite keeps the replay locked until the game ends.)

    F8                Hide / show the overlay.
    Ctrl+F8           Switch view: full, compact (last game, total chests, games,
                      best game, treasures) and a graph of chests per game
                      (bars = each game, gold line = average of your last 10 games,
                      dashed line = all-time average). Shows your last 100 games.
    Shift+F8          Move mode: drag it with the mouse, right-click it for the menu.
                      Shift+F8 again locks it; then clicks go straight through it.
                      F8 only reads whether the key is held, so the game still gets it.
    Tray icon         Left-click = hide/show. Right-click = menu: Show graph, New session,
                      Compact view, Windows pop-up after each game, Start with Windows, Full report,
                      Setup and help, open the replay/data folder, Exit.
    First start       A setup window checks that Fortnite records replays and runs in
                      Windowed Fullscreen, explains the keys, and offers Start with Windows.

    Overlay           top:    LAST GAME (green right after it's counted), TOTAL CHESTS
                      middle: per mode: chests, games, best game, p/g (per game)
                      bottom: GAMES, AVERAGE (chests per game), BEST GAME (all modes), TREASURES
                      A small dot in the top-right corner: gold = you're in a game,
                      green = it's reading the game that just ended.
    Works over Windowed Fullscreen / Borderless, not exclusive Fullscreen.

  The first start reads all your replays (about 40 seconds for 100). After that
  it only reads new games, a few seconds each, once the game has ended. It runs
  at low priority so it doesn't take CPU from the game.

  Chest Stats.bat     Full text report: per mode, teammates you played with, rarest
                      items, etc. Also writes data\matches.csv.

WHAT COUNTS
  Fortnite only keeps your last ~100 replays (saved/renamed ones stay). This tool
  remembers every game it has read in data\matches.json, so your totals keep
  growing even after Fortnite deletes the old replays. Don't delete that file.

  Custom games are left out: any match with fewer than 10 players in the lobby
  (you alone, or with a friend). Real matches always have 75+ players and bots.
  A game you save and rename in Fortnite is only counted once.

  Only normal Battle Royale (build and zero build) and Ranked count, and only
  this season (the newest Fortnite version among your games). Reload, Blitz,
  LTMs, Creative and older seasons are left out; the full report lists them.

UPDATES
  When a new version is out, the overlay and a pop-up tell you. Right-click the
  chest icon in the tray > "Update to vX.Y": it downloads it, swaps the program
  files and restarts. Your data folder (counted games, settings) is kept.

WHERE THINGS ARE
  Chest Counter.exe   The app.
  data\matches.json   Every game it has read.
  data\matches.csv    One row per game. Opens in Excel / Google Sheets.
  data\treasure.txt   What counts as "treasure". Edit it, then restart the app.
  data\settings.json  Window position and tray options.
  app\ChestStats.exe  The program behind Chest Stats.bat.
  Source code         github.com/oneboyler/fortnite-chest-counter
                      build.bat rebuilds both programs (needs the .NET 9 SDK;
                      exit the app first). It's built on
                      github.com/Shiqan/FortniteReplayDecompressor (MIT license).

Replays are read from %LOCALAPPDATA%\FortniteGame\Saved\Demos.
Replay recording must be on in Fortnite (Settings > Game > Replays).
Nothing to install: unzip the folder anywhere (not inside Program Files) and run it.
Windows may say "Windows protected your PC" the first time, because the app isn't
signed. Click "More info" > "Run anyway". It only reads your replay files and
sends nothing about you or your games anywhere. Its only internet use: it asks
GitHub for the latest version number on start and every 6 hours (tray menu >
"Check for updates" turns that off), and downloads an update when you click it.

HOW IT COUNTS
  Chests      Normal, rare and relic chests, buried treasure and booty chests.
              Not ammo boxes, supply drops or loot cannons.
  Opened      Exact. The replay records who opened every chest.
  Destroyed   The replay doesn't say who broke a chest, so the tool works it out:
                - a rocket (boat rocket / rocket launcher) blew up at the chest
                  just before it broke -> whoever fired it
                - a recorded hit on the chest -> that player
                - otherwise whoever was standing within 6 m (best guess)
              Chests broken with nobody/no rocket nearby are not credited.
  Season      Count of the season treasures (gizmos, jam tracks, the car...)
  treasure    that came out of YOUR chests. Every prize gives your character a
              "prize handler" in the replay. A squad shares its prizes, so a
              teammate's find shows up too; those are counted separately as
              "shared" (in the full report) when none of your chests broke or
              opened within 2 seconds. It can't tell which prize it was: the
              replay doesn't say. Awards other than gizmos drop once each, so
              nearly all are gizmos. Other squads' treasures aren't in your replay.
  Treasure    Items flying out of a chest are matched to that chest. Usually
              right, but not 100%. Anything you've never seen before in any of
              your games is also called out in the after-game pop-up.
  Teammates   Your replay only records what's near you, so teammates' chests
              can't be counted. Your own numbers are accurate.

AFTER A FORTNITE UPDATE
  If Epic renames chests or changes the replay format, counts may drop to zero
  or replays may fail to read. Chest Stats.bat prints a warning when it sees a
  chest type it doesn't know.
