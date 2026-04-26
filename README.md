# CR2(RAW) Batch Studio

![Platform](https://img.shields.io/badge/platform-Windows-1f5d8e)
![Framework](https://img.shields.io/badge/.NET-8.0-2c6996)
![Release](https://img.shields.io/badge/release-v1.1.0-d07a2d)

CR2(RAW) Batch Studio is a Windows app for photographers who prepare one Adobe Camera Raw / Photoshop `XMP` preset, batch-render many Canon `CR2/RAW` files to `JPG`, review the output, and optionally run `ONNX` AI postprocess on selected files.

Developer / Разработчик: `Иргит Валерий`

## Features

- `WPF` desktop app for Windows
- batch queue for files and folders
- Adobe `XMP` preset selection
- `CR2/RAW -> JPG` rendering through Adobe Photoshop automation
- review tab with zoom, mouse-wheel zoom, drag-pan preview, and delete-only sorting
- `ONNX` AI postprocess with per-model folders under `models`
- portable package with ready-to-run `exe`

## What The Program Does

The program is built around one practical workflow:

1. Open one `CR2` file in `Adobe Photoshop / Camera Raw`
2. Adjust the RAW settings the way you need
3. Save the settings as an `XMP` preset / sidecar file
4. Start `CR2(RAW) Batch Studio`
5. Add many `CR2/RAW` files into the queue
6. Select that saved `XMP` file
7. Render the batch to `JPG` in the `Out` folder
8. Review the output files and delete unwanted JPGs from `Out`
9. Optionally run `ONNX` AI postprocess on selected files

## Important

- `Adobe Photoshop` must be installed
- `Adobe Photoshop` should be running before batch rendering
- the app uses the Adobe workflow because it keeps the result close to your normal Camera Raw / Photoshop editing

## Quick Start

1. Run `CR2RawBatchStudio.exe`
2. In `Photoshop`, open one raw file and create your `XMP` settings file
3. In the app, choose:
   - `XMP preset`
   - `Out folder`
4. Add RAW files or a folder with RAW files
5. Click `Render`
6. Open the `Review` tab
7. Remove unwanted JPGs from the `Out` folder
8. If needed, open `Step 3` and run AI postprocess with the selected model

## Short Meaning Of The Program

The program is designed for one specific photographer workflow:

1. Open one `CR2/RAW` file in `Photoshop / Camera Raw`
2. Adjust the image manually
3. Save that RAW setup as `XMP`
4. Use the saved `XMP` for batch processing of many RAW files
5. Review the rendered `JPG` files
6. Remove unwanted files from `Out`
7. Optionally run AI postprocess on selected JPG files

That is why `Photoshop` should be installed and preferably already running before batch rendering.

## How To Create The XMP Preset

Typical Adobe workflow:

1. Open a `Canon CR2` file in `Photoshop`
2. The file opens in `Adobe Camera Raw`
3. Adjust exposure, white balance, color, sharpness and other RAW settings
4. Save the settings as an `XMP` file
5. Use that saved `XMP` file in this app for batch processing

That means one manually prepared RAW look can be reused for the whole batch.

## AI Postprocess

- Put ONNX models into `models\<ModelName>\`
- The app automatically discovers model folders and their `manifest.json`
- `NAFNet-GoPro` is suited for blur removal on motion-blurred frames
- `NAFNet-REDS` is a more general restoration profile
- Step 3 processes selected JPG files from `Out` and writes results back into the same folder

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
