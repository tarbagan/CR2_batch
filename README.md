# CR2(RAW) Batch Studio

![Platform](https://img.shields.io/badge/platform-Windows-1f5d8e)
![Framework](https://img.shields.io/badge/.NET-8.0-2c6996)
![Release](https://img.shields.io/badge/release-v1.1.0-d07a2d)

CR2(RAW) Batch Studio is a Windows app for photographers who process `Canon CR2/RAW` files in `Adobe Photoshop / Camera Raw`, save one `XMP` preset, and then batch-apply that preset to many RAW files with review and final selection.

## Features

- `WPF` desktop app for Windows
- batch queue for files and folders
- Adobe `XMP` preset selection
- `CR2/RAW -> JPG` rendering through Adobe Photoshop automation
- review tab with zoom, mouse wheel zoom and drag-pan preview
- final selection workflow and project save/load
- portable package with ready-to-run `exe`

## What The Program Does

The program is built around one practical workflow:

1. Open one `CR2` file in `Adobe Photoshop / Camera Raw`
2. Adjust the RAW settings the way you need
3. Save the settings as an `XMP` preset / sidecar file
4. Start `CR2(RAW) Batch Studio`
5. Add many `CR2/RAW` files into the queue
6. Select that `XMP` file
7. Start batch rendering to `JPG`
8. Review the output files and move only final picks into the final folder

## Important

- `Adobe Photoshop` must be installed
- `Adobe Photoshop` should be running before batch rendering
- the app uses the Adobe workflow because only Adobe gives output that matches `Camera Raw / Photoshop`

## Quick Start

1. Run `CR2RawBatchStudio.exe`
2. In `Photoshop`, open one raw file and create your `XMP` settings file
3. In the app, choose:
   - `XMP preset`
   - `Out folder`
   - `Final folder`
4. Add RAW files or a folder with RAW files
5. Click `Render`
6. Open the `Review` tab
7. Sort the rendered `JPG` files
8. Send selected images to the final folder

## How To Create The XMP Preset

Typical Adobe workflow:

1. Open a `Canon CR2` file in `Photoshop`
2. The file opens in `Adobe Camera Raw`
3. Adjust exposure, white balance, color, sharpness and other RAW settings
4. Save the settings as an `XMP` file
5. Use that saved `XMP` file in this app for batch processing

That means one manually prepared RAW look can be reused for the whole batch.

## Source Code

- Open [PhotoBatchStudio.sln](PhotoBatchStudio.sln) in Visual Studio 2022
- Build `PhotoBatchStudio.App`

## Download

- Project page: `https://tarbagan.github.io/CR2_batch/`
- Direct ZIP: `https://tarbagan.github.io/CR2_batch/downloads/CR2-RAW-Batch-Studio-v1.1.0-win-x64.zip`

## Windows SmartScreen

Windows can show a warning because the app is not yet digitally signed.

This does not mean the archive is malicious. It means the file does not yet have a paid code-signing certificate.

If you downloaded the archive from this repository or the GitHub Pages download page, you can verify:
- the repository source code is open
- the archive name matches the current release
- the app is built for this project only

To remove the warning completely in future releases, the app must be signed with a trusted `code signing certificate`.

## Release notes

See [CHANGELOG.md](CHANGELOG.md).
