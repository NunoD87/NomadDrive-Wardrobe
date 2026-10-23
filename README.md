<a id="readme-top"></a>

[![Contributors][contributors-shield]][contributors-url]
[![Forks][forks-shield]][forks-url]
[![Stargazers][stars-shield]][stars-url]
[![Issues][issues-shield]][issues-url]
[![MIT License][license-shield]][license-url]

<br />
<div align="center">
  <h3 align="center">Nomad Drive Wardrobe</h3>

  <p align="center">
    A BepInEx plugin for Nomad Drive: pick your character outfit from the pause menu and mix and match parts.
    <br />
    <a href="https://github.com/NunoD87/NomadDrive-Wardrobe#getting-started"><strong>Install guide »</strong></a>
    <br />
    <br />
    <a href="https://github.com/NunoD87/NomadDrive-Wardrobe/releases">Download</a>
    &middot;
    <a href="https://github.com/NunoD87/NomadDrive-Wardrobe/issues/new?labels=bug">Report Bug</a>
    &middot;
    <a href="https://github.com/NunoD87/NomadDrive-Wardrobe/issues/new?labels=enhancement">Request Feature</a>
  </p>
</div>

<details>
  <summary>Table of Contents</summary>
  <ol>
    <li><a href="#about-the-project">About The Project</a>
      <ul><li><a href="#built-with">Built With</a></li></ul>
    </li>
    <li><a href="#getting-started">Getting Started</a>
      <ul>
        <li><a href="#prerequisites">Prerequisites</a></li>
        <li><a href="#installation">Installation</a></li>
      </ul>
    </li>
    <li><a href="#usage">Usage</a></li>
    <li><a href="#how-it-works">How It Works</a></li>
    <li><a href="#known-limits">Known Limits</a></li>
    <li><a href="#roadmap">Roadmap</a></li>
    <li><a href="#contributing">Contributing</a></li>
    <li><a href="#license">License</a></li>
    <li><a href="#contact">Contact</a></li>
    <li><a href="#acknowledgments">Acknowledgments</a></li>
  </ol>
</details>

## About The Project

In Nomad Drive your character comes from a round-robin by join order: the first player to spawn gets outfit 1, the next outfit 2, and so on. There is no way to change it. This mod adds one.

* A **Wardrobe** button in the pause menu (below Settings) opens an outfit picker built from the game's own UI.
* A **live preview** of your character turns beside the list, so you see what you pick.
* Pick a whole **outfit**, or **mix and match** feet, legs, torso, head and accessories, with readable names ("Cowboy Hat", "Tank Top", ...).
* Your choice is **saved** and re-applied every time you join a game.
* **Everyone sees your look, and only you need the mod.** The mod uses the game's own networked cosmetic commands, so the server checks and syncs the choice like any normal change.
* It uses only the outfits already in the game. No extra files, models or textures.

> **Status: v0.x. The v0.1 picker was tested in single player. The v0.2 native panel and preview are new and not yet tested in game.** Please report anything odd.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

### Built With

* [![BepInEx][BepInEx-badge]][BepInEx-url]
* [![Harmony][Harmony-badge]][Harmony-url]
* [![C#][CSharp-badge]][CSharp-url]
* [![GitHub Actions][Actions-badge]][Actions-url]

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Getting Started

### Prerequisites

* Nomad Drive (the playtest build the mod was written against).
* **BepInExPack_NomadDrive** from Nexus Mods: <https://www.nexusmods.com/nomaddrive/mods/7>
  (BepInEx set up for this game; the game's own core libraries are stripped, so plain BepInEx does not start).

### Installation

1. Extract **BepInExPack_NomadDrive** into the game folder, next to the game `.exe`.
2. Start the game once from Steam, then quit, so BepInEx creates its folders.
3. Download `NomadDriveWardrobe-<version>.zip` from [Releases][releases-url] and extract it into the same game folder.
   You should get `BepInEx/plugins/NomadDriveWardrobe/NomadDriveWardrobe.dll`.
4. Start the game from Steam.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Usage

1. Join or start a game, press **Esc** to open the pause menu.
2. Click **Wardrobe** (below Settings).
3. Choose an **Outfit**, or use the `<` `>` arrows for each part and switch accessories **On/Off**. The character on the right shows the result.
4. Click **Close**. Your look is saved and used next time automatically.

To go back to the game's default outfit, set `HasSavedLook` to `false` in the config. If the native panel cannot be built (for example after a game update), the mod falls back to a simple on-screen window.

The first time you open a game with the mod, the log (`BepInEx/LogOutput.log`, or `output_log.txt`) lists every outfit and part the game has, which is useful for bug reports.

**Config** (`BepInEx/config/com.nunod87.nomaddrive.wardrobe.cfg`, created on first run):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Switch the mod off without uninstalling |
| `AutoApply` | `true` | Re-apply the saved look when joining a game |
| `HasSavedLook` | `false` | Set by the mod when you pick something; set to `false` to use the game's outfit again |
| `Feet`, `LowerBody`, `UpperBody`, `Head`, `Accessories` | empty | Your saved parts. Written by the Wardrobe, no need to edit |

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## How It Works

Found by reading the game's assemblies:

* `PlayerCosmeticManager` sits on every player. On the server it picks `availableSets[counter++ % count]` for each new player, which is why outfits depend on join order.
* Each part (`Feet`, `LowerBody`, `UpperBody`, `Head`, up to 16 accessories) is a separate synced value. Every client shows the right parts from those values, including for players who join later.
* The game already has `SetCosmetic`, `AddAccessory`, `RemoveAccessory` and `ApplySet`, which send commands to the server. The server validates the id against the character's real parts. Nothing in the game calls them, so there is no menu. This mod is that menu.
* The pause menu button is a copy of the Settings button; the lower buttons move down to make room. The panel's rows are copies of the Settings screen's own option selectors, so they look and sound like the rest of the game.
* The preview is a hidden camera that draws only your character into a texture. While the panel is open the character is moved to an unused layer (and unhidden if first-person hides it); everything is restored when you close it.

Game types are reached through reflection, so this repository contains no game files.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Known Limits

* The preview uses the scene's lighting and a plain background, so it can look dark or flat in some places.
* Thumbnails for every single part are not there yet; the preview shows your whole character.
* Some parts may hide others (for example hats and hair); the game decides.
* Playtest and release builds can differ. A game update can break the mod; it logs an error if it cannot find the expected types.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Roadmap

- [x] Pause menu button and outfit picker
- [x] Mix and match parts and accessories
- [x] Save and re-apply the look
- [ ] Test in game (button placement, every part, late joiners)
- [x] Native-looking panel from the game's own UI (v0.2)
- [x] Live character preview (v0.2)
- [ ] Thumbnail for every part
- [ ] Gamepad navigation check
- [ ] Several saved presets
- [ ] Re-check against the 27 Oct 2026 release build

See the [open issues](https://github.com/NunoD87/NomadDrive-Wardrobe/issues) for a full list of proposed features (and known issues).

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Contributing

Contributions are what make the open source community such an amazing place to learn, inspire, and create. Any contributions you make are **greatly appreciated**.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'feat: add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

Build locally with `dotnet build src/NomadDriveWardrobe/NomadDriveWardrobe.csproj -c Release`.
CI builds every push, and pushing a tag like `v0.1.1` publishes a GitHub Release with the zip.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## License

Distributed under the MIT License. See `LICENSE` for more information.

Not affiliated with Indie Devil. Use at your own risk, and check the playtest terms before modding.

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Contact

Nuno Daniel - [@NunoD87](https://github.com/NunoD87)

Project Link: [https://github.com/NunoD87/NomadDrive-Wardrobe](https://github.com/NunoD87/NomadDrive-Wardrobe)

<p align="right">(<a href="#readme-top">back to top</a>)</p>

## Acknowledgments

* [BepInEx](https://github.com/BepInEx/BepInEx) and [HarmonyX](https://github.com/BepInEx/HarmonyX)
* [BepInExPack_NomadDrive](https://www.nexusmods.com/nomaddrive/mods/7) for the game-ready BepInEx setup
* [NomadDrive-MorePlayers](https://github.com/NunoD87/NomadDrive-MorePlayers), the sibling mod this one shares its setup with
* Indie Devil, for making Nomad Drive

<p align="right">(<a href="#readme-top">back to top</a>)</p>

<!-- MARKDOWN LINKS & IMAGES -->
[contributors-shield]: https://img.shields.io/github/contributors/NunoD87/NomadDrive-Wardrobe.svg?style=for-the-badge
[contributors-url]: https://github.com/NunoD87/NomadDrive-Wardrobe/graphs/contributors
[forks-shield]: https://img.shields.io/github/forks/NunoD87/NomadDrive-Wardrobe.svg?style=for-the-badge
[forks-url]: https://github.com/NunoD87/NomadDrive-Wardrobe/network/members
[stars-shield]: https://img.shields.io/github/stars/NunoD87/NomadDrive-Wardrobe.svg?style=for-the-badge
[stars-url]: https://github.com/NunoD87/NomadDrive-Wardrobe/stargazers
[issues-shield]: https://img.shields.io/github/issues/NunoD87/NomadDrive-Wardrobe.svg?style=for-the-badge
[issues-url]: https://github.com/NunoD87/NomadDrive-Wardrobe/issues
[license-shield]: https://img.shields.io/github/license/NunoD87/NomadDrive-Wardrobe.svg?style=for-the-badge
[license-url]: https://github.com/NunoD87/NomadDrive-Wardrobe/blob/main/LICENSE
[releases-url]: https://github.com/NunoD87/NomadDrive-Wardrobe/releases
[BepInEx-badge]: https://img.shields.io/badge/BepInEx-5.4.23.5-1f6feb?style=for-the-badge
[BepInEx-url]: https://github.com/BepInEx/BepInEx
[Harmony-badge]: https://img.shields.io/badge/HarmonyX-patches-7b2cbf?style=for-the-badge
[Harmony-url]: https://github.com/BepInEx/HarmonyX
[CSharp-badge]: https://img.shields.io/badge/C%23-net472-239120?style=for-the-badge&logo=csharp&logoColor=white
[CSharp-url]: https://learn.microsoft.com/dotnet/csharp/
[Actions-badge]: https://img.shields.io/badge/GitHub_Actions-CI-2088FF?style=for-the-badge&logo=githubactions&logoColor=white
[Actions-url]: https://github.com/NunoD87/NomadDrive-Wardrobe/actions
