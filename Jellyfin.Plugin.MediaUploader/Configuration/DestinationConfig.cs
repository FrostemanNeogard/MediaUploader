// <copyright file="DestinationConfig.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MediaUploader.Configuration;

/// <summary>
/// A named upload destination preset.
/// </summary>
public class DestinationConfig
{
    /// <summary>
    /// Gets or sets the display name shown in the upload page dropdown.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path relative to the configured base <see cref="PluginConfiguration.UploadPath"/>.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
