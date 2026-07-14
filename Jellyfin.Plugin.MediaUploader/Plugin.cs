namespace Jellyfin.Plugin.MediaUploader;

using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MediaUploader.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => "Media Uploader Improved";

    public override string Description => "Allows uploading media files directly via the web interface.";

    public override Guid Id => Guid.Parse("ec712ab4-8ecb-4d34-8087-09db56f33d44");

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = this.Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", this.GetType().Namespace),
            }
        ];
    }
}
