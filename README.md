# Media Uploader Improved Plugin for Jellyfin

A plugin for [Jellyfin](https://jellyfin.org) that lets users upload media files directly from an interactive UI.

## Overview

This plugin provides a **standalone upload page** (`/Plugins/MediaUploader/Page`) with an interactive UI to allow users to effortlessly upload their media from their local device to their Jellyfin server.

Uploads are written under a configurable **base path**, into a chosen **destination** (with an optional **subfolder**). This makes it easy to drop files straight into the right library folder (e.g. `/media/movies` or `/media/shows`).

## Features

* Upload **multiple files at once** allowing you to upload entire seasons of shows seamlessly.
* **Batch-upload a folder**: uploads the top-level video files of the selected folder, ignoring subfolders (e.g. trailers) and non-video files (subtitles, text files, etc.).
* Configurable **base upload path** plus a user-defined list of named **destination presets** (e.g. `Movies → {base_path}/movies`, `Shows → {base_path}/shows`) selectable on the upload page, or a free-text custom path, to accomodate your specific organizational needs.
* Optional **subfolder** field so files land exactly where you want (e.g. `Show Name/Season X`).
* A library scan is queued automatically (debounced) after uploads so new files are picked up immediately.

## Installation

**Method A — Plugin Repository (recommended for updates)**

1. In the Jellyfin Dashboard, go to **Plugins → Repositories** and click **"Manage Repositories"**, then click **+ New Repository**.
2. For the **"Repository Name"** field, set it to anything you want (e.g. "Media Uploader Improved").
3. For the **"Repository URL"** field, enter the following URL: 
   ```
   https://raw.githubusercontent.com/FrostemanNeogard/MediaUploader/main/manifest.json
   ```
4. Click **"Add"** to save your new repository, then head back to the **Plugins** tab. Refresh, search for **Media Uploader Improved**, and click **Install**.
5. Restart your Jellyfin server when prompted.

**Method B — Manual Installation**

1. Build the plugin (see below) or download `Jellyfin.Plugin.MediaUploader_1.0.0.0.zip` from the [releases page](https://github.com/FrostemanNeogard/MediaUploader/releases).
2. **Stop** your Jellyfin server.
3. Create a folder named `Jellyfin.Plugin.MediaUploader` inside your server's plugins directory:
   * **Windows:** `C:\ProgramData\Jellyfin\Server\plugins`
   * **Linux (package):** `/var/lib/jellyfin/plugins`
   * **Docker:** typically `/config/plugins` inside the container
4. Extract the **contents** of the zip directly into that folder (no nested subfolder).
5. **Start** your Jellyfin server. The plugin loads on startup.

## Configuration

After installing and restarting:

1. Jellyfin Dashboard → **Plugins** → **Media Uploader Improved** → Settings.
2. **Base Upload Path** (required): the full server path that all uploads are written under (e.g. `/media` or `D:\Media`). The Jellyfin process must have **write** access to it (and to the sub-directories you target).
3. **Destinations**: add the named presets you want (the list starts empty). Each **Path** is relative to the Base Upload Path. With a base of `/media`, a preset named `Movies` with path `movies` resolves to `/media/movies` on your server; `Shows` with path `shows` resolves to `/media/shows`.
4. **Save**: Hit the blue **"Save"** button once you've added all your desired destionations (this can be chaned at any time through the **plugin settings** page).
5. The settings page shows the direct link to the standalone upload page — bookmark it.

## Usage

1. Open the upload page link from the plugin settings, either via the **"Go to Upload Page"** button, or by navigation to `{your_jellyfin_url}/Plugins/MediaUploader/Page`.
2. **API Key**: paste a personal Jellyfin API key. You can get this by navigating to (Jellyfin Dashboard → API Keys → **+ New API Key**). Give it a fitting name, for example "media uploader improved".
   * <span style="color:red;">**Security Warning:**</span> saving the key in local storage on the Upload Page is less secure; only do this on trusted, private computers. It is recommended to store this somewhere safe instead.
3. **Files**: click to choose one or more files. Enable **Batch upload a folder** if you **instead** want to upload the top-level video files of a selected folder (subfolders and non-video files are skipped).
4. **Destination**: pick a preset from the dropdown (loaded from the plugin configuration) or choose **Custom…** to type a relative path. Use **Subfolder** for an extra level (this is particularily useful for show discovery in Jellyfin, e.g. `Breaking Bad/Season 1`).
5. **Start Upload**. You'll see a progress bar and, when done, a summary of how many files uploaded (and any that were skipped).
6. Files are written to `<Base Upload Path>/<Destination>/<Subfolder>/<file>`, and a library scan is queued automatically.

## Building from Source (optional)

**Prerequisites:** .NET 8 SDK ([download](https://dotnet.microsoft.com/download/dotnet/8.0)) and Git. `global.json` in the repo already pins the SDK to .NET 8.

```bash
dotnet restore
dotnet build -c Release
```
The plugin DLL is at `bin/Release/net8.0/Jellyfin.Plugin.MediaUploader.dll`. Zip it (or just the DLL) and install as described in Method B.

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

## License

This plugin is licensed under the [GPLv3](LICENSE).
