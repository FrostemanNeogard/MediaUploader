# Media Uploader Plugin for Jellyfin

A plugin for Jellyfin media server that allows uploading media files directly via a web interface.

## Overview

This plugin provides two main functionalities:

1.  An API endpoint (`/Plugins/MediaUploader/Upload`) to programmatically upload files.
2.  A simple, standalone web page accessible via a direct link, allowing users (including non-admins) to upload files using their personal Jellyfin API Key.

The target directory for uploads must be configured in the plugin settings.

## Features

* Direct file upload via HTTP POST request (supports multiple files in one request).
* Standalone HTML upload page accessible via a direct link.
* Configurable **base** upload directory plus a list of named destination presets (e.g. Movies, Shows) selectable on the upload page.
* Upload many files at once, or upload an entire **folder** while preserving its relative structure.
* Optional subfolder field so files land exactly where you want (e.g. `Show Name/Season 1`).
* A library scan is queued automatically after a successful upload so new files are picked up.
* Basic progress bar during upload on the standalone page.
* Option to save API Key in the browser's local storage (use with caution).

## Installation

**Recommended Method: Using Plugin Repository**

1.  Navigate to your Jellyfin Dashboard.
2.  Go to **Plugins** in the sidebar, then click on the **Repositories** tab.
3.  Click the **+** button to add a new repository.
4.  Enter a **Name** for the repository (e.g., "Media Uploader Repo").
5.  Enter the **Repository URL**:
    ```
    https://raw.githubusercontent.com/TheAnonymous/MediaUploader/refs/heads/main/manifest.json
    ```
6.  Click **Save**.
7.  Go back to the **Catalog** tab within Plugins.
8.  You might need to click the **Refresh** button (top right) for the server to fetch the new repository data.
9.  Find **"Media Uploader"** in the catalog.
10. Click **Install**.
11. Restart your Jellyfin server when prompted.

**Fallback Method: Manual Installation**

*If the repository method fails or you prefer manual installation:*

1.  Download the `Jellyfin.Plugin.MediaUploader_x.x.x.x.zip` file from the [Plugin Releases Page](https://github.com/TheAnonymous/MediaUploader/releases).
2.  **Stop** your Jellyfin server.
3.  Create a folder named `Jellyfin.Plugin.MediaUploader` inside your server's `plugins` directory.
    * **Common Plugin Directory Locations:**
        * **Windows:** `C:\ProgramData\Jellyfin\Server\plugins`
        * **Linux (Package):** `/var/lib/jellyfin/plugins`
        * **Linux (Docker):** Typically mapped to `/config/plugins` inside the container.
4.  Extract the **contents** of the downloaded ZIP file directly into the newly created `Jellyfin.Plugin.MediaUploader` folder. (Do not put the ZIP file itself there, and do not have an extra nested folder inside).
5.  **Start** your Jellyfin server. The plugin should now be loaded.

## Configuration

After installing the plugin and restarting Jellyfin:

1.  Navigate to your Jellyfin Dashboard.
2.  Go to **Plugins** in the sidebar.
3.  Find **"Media Uploader"** in the list (under "Installed") and click on it (or the settings/three-dot icon next to it).
4.  **Set the Base Upload Path:** You **must** enter the full path to a directory on your server that all uploads are written under (e.g., `/media` or `D:\Media`).
     * **Crucially:** Ensure the user account running the Jellyfin server process has **write permissions** for this directory (and the sub-directories you target)! This field is mandatory.
5.  **Configure Destinations (optional but recommended):** Add named presets under the *Destinations* section. Each **Path** is relative to the Base Upload Path above. For example, with a base of `/media`, a destination named `Movies` with path `movies` resolves to `/media/movies`, and `Shows` with path `shows` resolves to `/media/shows`.
6.  Click **Save**.
6.  On the same settings page, you will find the direct link to the standalone upload page.

## Usage (Direct Upload Page)

1.  Obtain the link to the **Direct Upload Page** from the plugin's configuration page in the Jellyfin Dashboard. Bookmark this link for easy access.
2.  **Generate an API Key:** If you don't have one, go to your Jellyfin user profile (click your profile icon top right) &rarr; Settings &rarr; API Keys &rarr; Click the '+' icon to create a new key. Give it a name (e.g., "MediaUpload Key") and copy the generated key. **Keep this key secure!**
3.  **Open the Upload Page:** Navigate to the bookmarked link.
4.  **Enter API Key:** Paste your generated API Key into the input field. You can optionally check the box to save the key in your browser's local storage for next time.
    * <span style="color:red;">**Security Warning:**</span> Saving API Keys in local storage is less secure. Only use this feature in trusted environments on private computers. For higher security, use a password manager and paste the key each time.
5.  **Select Destination:** Choose a preset from the *Destination* dropdown (loaded from the plugin configuration), or pick *Custom...* to type a relative path. Use the *Subfolder* field to add an extra sub-path (e.g. `My Movie (2024)` or `Show Name/Season 1`).
6.  **Select Files or Folder:** Click the file input to choose one or more files. Check *"Upload a folder (preserve its structure)"* to select an entire folder; its relative layout is kept when written to the server.
7.  **Start Upload:** Click the "Start Upload" button. You should see a progress bar and, once finished, a summary of how many files were uploaded (and any that failed).
8.  Uploaded files are written to `<Base Upload Path>/<Destination>/<Subfolder>/<file>` and a library scan is queued automatically.

## Building from Source (Optional)

If you want to build the plugin yourself:

1.  **Prerequisites:**
    * .NET 8 SDK ([https://dotnet.microsoft.com/download/dotnet/8.0](https://dotnet.microsoft.com/download/dotnet/8.0))
    * Git
2.  **Clone the repository:**
    ```bash
    git clone YOUR_REPOSITORY_URL_HERE
    cd [repository-folder]
    ```
    *(Replace `YOUR_REPOSITORY_URL_HERE`)*
3.  **Ensure `global.json` uses .NET 8 SDK:** Create or modify `global.json` in the root directory:
    ```json
    {
      "sdk": {
        "version": "8.0.200", // Or your installed 8.0.xxx version
        "rollForward": "latestMinor",
        "allowPrerelease": false
      }
    }
    ```
4.  **Restore dependencies:**
    ```bash
    dotnet restore
    ```
5.  **Build and Package:**
    * Ensure the `build.ps1` script is present in the root directory. (You might need to create it using the previously provided code if cloning fresh).
    * Run the build script from PowerShell:
        ```powershell
        Set-ExecutionPolicy Bypass -Scope Process -Force; .\build.ps1 -Configuration Release
        ```
    * This will create a `dist` folder containing the installable `Jellyfin.Plugin.MediaUploader_x.x.x.x.zip` file.
6.  **Install:** Follow the **Manual Installation** steps under the "Installation" section using the files from the `dist` folder (extracting recommended).

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

## License

This plugin is licensed under the [GPLv3](LICENSE).
