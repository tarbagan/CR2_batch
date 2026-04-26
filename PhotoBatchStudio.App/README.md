# PhotoBatchStudio.App

Windows desktop scaffold for a future photographer workflow in Visual Studio.

Planned capabilities:

- batch add files and folders
- apply Adobe Photoshop / Camera Raw `XMP` sidecars to `CR2` and other RAW files
- detect blurry raster images
- connect external AI deblur or super-resolution tools
- batch export and metadata writing for `JPG`
- render RAW to JPG through Adobe Photoshop automation

## Open in Visual Studio

Open `PhotoBatchStudio.sln`.

## Current state

This is a `WPF` project with:

- main window
- queue view
- processing settings
- command structure
- working RAW to JPG flow through Adobe Photoshop automation
- fallback RAW copy + sidecar mode
- JPG metadata write support

## Adobe requirement

For `CR2 + XMP -> JPG` to match Adobe processing, Adobe Photoshop with Camera Raw must be installed on the same Windows machine.
