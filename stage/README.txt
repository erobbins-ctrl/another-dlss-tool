Another DLSS Tool
=================

Brings DLSS 5 neural rendering to Direct3D 9, 11 and 12 games that ship without DLSS,
using the official DLSS5-Feeder project (https://github.com/jlrouzies-fr/DLSS5-Feeder).

The app asks for administrator rights when it starts, because most game folders are
under Program Files.

Adding games
  Find Steam games  scans your Steam libraries for Direct3D games.
  Add a game        picks any game's main .exe (EA app, GOG, Epic, anywhere).
  Games with their own DLSS or with anti-cheat are flagged. Don't use this on
  anti-cheat games online.

Installing into a game
  1. Pick the game on the left.
  2. Press Install. The folder is snapshotted, then the official installer runs in its
     own window; answer its questions there.
  3. Self-test checks your driver and DLSS files (300/300 is a pass).
  4. In-game: Home opens ReShade; turn on neural rendering in the add-on's panel and
     turn the game's own anti-aliasing off.

Image quality
  The Overview tab's Image quality card turns on fixes from the DLSS5-Feeder project's
  settings guide: a steadier image, less warping around fire and lights, sharpening,
  and 16x texture filtering for Direct3D 8/9 games. They apply while the game runs
  (texture filtering needs a restart). Deep Fried Chicken, the neural add-on the
  project recommends, can be chosen under Install options; pick its zip from its
  Discord, then Reinstall.

Shaders
  The Shaders tab installs the top shader packs from ReShade's official list (iMMERSE,
  NiceGuy, qUINT, METEOR, ZenteonFX, SweetFX, prod80, AstrayFX, OtisFX, Standard
  effects), with a switch to show every pack on the list. It also looks for your own
  paid RTGI copy in Downloads, Desktop, Documents and your games' shader folders, or
  you can pick the file yourself. Lighting effects are screen-space: they only see
  what's on screen, and they're costly.

Presets (Nexus Mods)
  Connect your Nexus account on the Presets tab with your personal API key (Nexus,
  Site preferences, API keys). It's stored encrypted for your Windows account. The
  tab lists the most endorsed ReShade presets for the selected game. With Premium,
  Install downloads one, adds its shaders, fetches any shader packs it needs from
  ReShade's official list, offers to turn off effects that clash with neural rendering
  (anti-aliasing, sharpening, grain) and makes it ReShade's active preset. Paste a mod
  link to install any preset, or install one from a file or folder you downloaded.

NVIDIA files
  NVIDIA's DLSS files aren't included in the public download. Click NVIDIA files at
  the bottom left and pick nvngx_dlss.dll and nvngx_dlssnr.dll (or the archives they
  came in). On RTX 20, 30 and 40 cards the neural pass needs the community build of
  nvngx_dlssnr.dll, which isn't signed by NVIDIA: use Copy hash and compare it with the
  one posted in the RenoDX Discord.

Uninstall (in the app) removes everything an install added to a game and restores the
original files. Uninstalling the app itself does not touch your games, so uninstall
from games first.

If the app shows an error, details are saved to %LOCALAPPDATA%\DLSS5-Setup\crash.log.
