# CR2 Batch

![Platform](https://img.shields.io/badge/platform-Windows-1f5d8e)
![Framework](https://img.shields.io/badge/.NET-8.0-2c6996)
![Release](https://img.shields.io/badge/release-v1.0.0-d07a2d)

CR2 Batch is a Windows desktop application for batch processing `CR2` and other photo files with an Adobe-oriented workflow.

## Features

- `WPF` desktop app for Windows
- batch queue for files and folders
- Adobe `XMP` preset selection
- `CR2/RAW -> JPG` rendering through Adobe Photoshop automation
- fallback RAW copy + sidecar mode
- raster export and JPG metadata update

## Adobe workflow

To match Adobe output, the application uses `Adobe Photoshop + Camera Raw` for RAW rendering instead of trying to imitate the Adobe engine.

## Source code

- Open [PhotoBatchStudio.sln](PhotoBatchStudio.sln) in Visual Studio 2022
- Build `PhotoBatchStudio.App`

## Download

- Project page: `https://tarbagan.github.io/CR2_batch/`
- Direct ZIP: `https://tarbagan.github.io/CR2_batch/downloads/CR2-Batch-v1.0.0-win-x64.zip`

## Release notes

See [CHANGELOG.md](CHANGELOG.md).
