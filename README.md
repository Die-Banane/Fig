<p align="center">
  <img src="assets/icon/fig.svg" width="128" height="128" alt="Fig icon">
</p>

<h1 align="center">Fig</h1>

<p align="center">
  <a href="https://github.com/Skynse/Fig/releases/latest"><img src="https://img.shields.io/github/v/release/Skynse/Fig?style=flat-square&label=release&color=8b5cf6" alt="Latest release"></a>
  <a href="https://github.com/Skynse/Fig/releases"><img src="https://img.shields.io/github/downloads/Skynse/Fig/total?style=flat-square&color=22c55e" alt="Downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Skynse/Fig?style=flat-square&color=3b82f6" alt="License: GPLv3"></a>
  <img src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux-lightgrey?style=flat-square" alt="Windows and Linux">
  <img src="https://img.shields.io/badge/.NET-10-512bd4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/Avalonia-12-8b44ac?style=flat-square" alt="Avalonia 12">
  <img src="https://img.shields.io/badge/FFmpeg-8.1-007808?style=flat-square&logo=ffmpeg&logoColor=white" alt="FFmpeg 8.1">
</p>

A simple, open-source video editor for cutting clips, long or short, without the hassle. Built with C# and Avalonia.

![Fig](assets/preview.png)

- **Timeline editing:** move, trim, split, ripple delete, with undo for everything.
- **Speed ramps:** keyframe a clip's speed, with frame blending or motion blur.
- **Effects and keyframes:** 30 effects, 20 transitions and presets, with any value keyframable. See the [creative library](docs/creative-library.md).
- **Text and layers:** titles and images, with tracks stacked top over bottom.
- **Markers:** bookmarks and ranges, and export each range as its own video.
- **Fast export:** H.264 / AAC, with hardware encoding where available.

## Install

Get the latest from [Releases](https://github.com/Skynse/Fig/releases). Each is a single file with everything included.

- **Windows:** run `Fig-<version>-win-x64-setup.exe` (no admin needed), or the portable `Fig-<version>-win-x64.exe`.
- **Linux:** `chmod +x Fig-<version>-linux-x64` and run it. It adds itself to your app menu. Run it with `--uninstall` to remove it from the menu.

## Build from source

Needs the .NET 10 SDK.

```bash
dotnet run --project src/Fig.App
dotnet test
```

See [DEVELOPMENT.md](DEVELOPMENT.md) for how things fit together and [CONTRIBUTING.md](CONTRIBUTING.md) before sending changes.

## License

[GPLv3](LICENSE)
