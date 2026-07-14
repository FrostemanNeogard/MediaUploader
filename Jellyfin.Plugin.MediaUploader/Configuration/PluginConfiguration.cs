// <copyright file="PluginConfiguration.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Jellyfin.Plugin.MediaUploader.Configuration;

using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base directory path that all uploads are written under.
    /// Upload destinations are resolved as sub-paths of this directory.
    /// </summary>
    public string UploadPath { get; set; } = string.Empty;

#pragma warning disable CA1002
#pragma warning disable CA2227
    /// <summary>
    /// Gets or sets the list of selectable destination presets shown on the upload page.
    /// Each destination's <see cref="DestinationConfig.Path"/> is relative to <see cref="UploadPath"/>.
    /// </summary>
    public List<DestinationConfig> Destinations { get; set; } =
    [
        new DestinationConfig { Name = "Movies", Path = "movies" },
        new DestinationConfig { Name = "Shows", Path = "shows" },
    ];
#pragma warning restore CA1002
#pragma warning restore CA2227
}
