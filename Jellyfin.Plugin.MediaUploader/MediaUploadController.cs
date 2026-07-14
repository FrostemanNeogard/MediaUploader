using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MediaUploader.Configuration;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaUploader
{
    [ApiController]
    [Route("Plugins/MediaUploader")]
    public class MediaUploadController : ControllerBase
    {
        private readonly ILogger<MediaUploadController> _logger;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryManager _libraryManager;

        private static readonly TimeSpan ScanDebounceInterval = TimeSpan.FromSeconds(30);
        private static readonly object ScanLock = new object();
        private static DateTime _lastScanUtc = DateTime.MinValue;

        public MediaUploadController(
            ILogger<MediaUploadController> logger,
            IServerConfigurationManager configurationManager,
            IFileSystem fileSystem,
            ILibraryManager libraryManager)
        {
            _logger = logger;
            _configurationManager = configurationManager;
            _fileSystem = fileSystem;
            _libraryManager = libraryManager;
        }

        [HttpGet("Destinations")]
        [Produces("application/json")]
        public IActionResult GetDestinations()
        {
            var destinations = Plugin.Instance?.Configuration.Destinations
                ?? new List<DestinationConfig>();

            var result = destinations
                .Where(d => !string.IsNullOrWhiteSpace(d.Name) && !string.IsNullOrWhiteSpace(d.Path))
                .Select(d => new DestinationInfo { Name = d.Name, Path = d.Path });

            return Ok(result);
        }

        [HttpPost("Upload")]
        [RequestSizeLimit(10L * 1024 * 1024 * 1024)]
        [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
#pragma warning disable SA1404
        [SuppressMessage("Reliability", "CA2007:Aufruf von \"ConfigureAwait\" für erwarteten Task erwägen", Justification = "<Pending>")]
#pragma warning restore SA1404
        public async Task<IActionResult> UploadFile()
        {
            _logger.LogInformation("Media Uploader Improved: UploadFile endpoint hit.");

            var configuredPath = Plugin.Instance?.Configuration.UploadPath;
            if (string.IsNullOrEmpty(configuredPath))
            {
                _logger.LogError("Media Uploader Improved: Upload path is not configured in plugin settings!");
                return StatusCode(StatusCodes.Status500InternalServerError, "Upload path is not configured in plugin settings.");
            }

            var baseDirectory = Path.GetFullPath(configuredPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            _logger.LogInformation("Media Uploader Improved: Using configured base directory: '{BaseDirectory}'", baseDirectory);

            try
            {
                var form = await Request.ReadFormAsync(CancellationToken.None).ConfigureAwait(false);
                var formFiles = form.Files;

                var files = formFiles.GetFiles("files").ToList();
                var singleFile = formFiles.GetFile("file");
                if (singleFile != null)
                {
                    files.Add(singleFile);
                }

                var destination = form["destination"].ToString();

                if (files.Count == 0)
                {
                    _logger.LogWarning("Media Uploader Improved: No file uploaded.");
                    return BadRequest("No file uploaded.");
                }

                var destinationRelative = BuildSafeRelativePath(destination);

                var uploaded = new List<string>();
                var failed = new List<string>();

                foreach (var file in files)
                {
                    if (file == null || file.Length == 0)
                    {
                        _logger.LogWarning("Media Uploader Improved: Skipping empty file entry.");
                        continue;
                    }

                    try
                    {
                        var fileRelative = BuildSafeRelativePath(file.FileName);

                        var fullTargetPath = Path.GetFullPath(Path.Combine(baseDirectory, destinationRelative, fileRelative));
                        var basePrefix = baseDirectory + Path.DirectorySeparatorChar;

                        if (!fullTargetPath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogError(
                                "Media Uploader Improved: Invalid target path generated. Attempted relative '{DestinationRelative}' + '{FileRelative}', resolved to '{ResolvedPath}', base directory '{BaseDirectory}'",
                                destinationRelative,
                                fileRelative,
                                fullTargetPath,
                                baseDirectory);
                            failed.Add(file.FileName);
                            continue;
                        }

                        var targetDir = Path.GetDirectoryName(fullTargetPath);
                        if (!string.IsNullOrEmpty(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        _logger.LogInformation("Media Uploader Improved: Saving '{FileName}' to '{FullTargetPath}'", file.FileName, fullTargetPath);
                        await using (var fileStream = new FileStream(fullTargetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await file.CopyToAsync(fileStream, CancellationToken.None).ConfigureAwait(false);
                        }

                        uploaded.Add(fullTargetPath);
                        _logger.LogInformation("Media Uploader Improved: File '{FileName}' saved successfully.", file.FileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Media Uploader Improved: Error saving file '{FileName}'", file.FileName);
                        failed.Add(file.FileName);
                    }
                }

                if (uploaded.Count == 0)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "No files could be saved. Check server logs and permissions.");
                }

                try
                {
                    if (ShouldQueueLibraryScan())
                    {
                        _logger.LogInformation("Media Uploader Improved: Queuing a library scan.");
                        _libraryManager.QueueLibraryScan();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Media Uploader Improved: Error requesting library validation.");
                }

                var result = new UploadResult
                {
                    TotalFiles = files.Count,
                    UploadedCount = uploaded.Count,
                    Uploaded = uploaded,
                    Failed = failed,
                    Message = $"{uploaded.Count} of {files.Count} file(s) uploaded successfully to '{destinationRelative}'."
                        + (failed.Count > 0 ? $" {failed.Count} failed." : string.Empty),
                };

                return Ok(result);
            }
            catch (IOException ioEx)
            {
                _logger.LogError(ioEx, "Media Uploader Improved: IO Error during upload process: {ErrorMessage}", ioEx.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"IO Error: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException authEx)
            {
                _logger.LogError(authEx, "Media Uploader Improved: Permission denied during upload process: {ErrorMessage}", authEx.Message);
                return StatusCode(StatusCodes.Status403Forbidden, $"Permission denied: {authEx.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Media Uploader Improved: Unexpected error processing file upload: {ErrorMessage}", ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"Unexpected error uploading file: {ex.Message}");
            }
        }

        [HttpGet("Page")]
        [Produces("text/html")]
        public IActionResult GetUploadPage()
        {
            _logger.LogInformation("Media Uploader Improved: Serving static upload page request.");
            try
            {
                var assembly = typeof(MediaUploadController).Assembly;
                var resourceName = "Jellyfin.Plugin.MediaUploader.Web.uploadPage.html";

                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    _logger.LogError("Media Uploader Improved: Could not find embedded resource: {ResourceName}. Check file exists, path/namespace, and Build Action='Embedded resource'.", resourceName);
                    return NotFound($"Resource not found: {resourceName}");
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var htmlContent = reader.ReadToEnd();

                return Content(htmlContent, "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Media Uploader Improved: Error serving static upload page");
                 return StatusCode(StatusCodes.Status500InternalServerError, "Error serving upload page");
            }
        }

        private string BuildSafeRelativePath(string? inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return string.Empty;
            }

            var segments = inputPath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            var safeSegments = segments
                .Where(segment => !string.Equals(segment, "..", StringComparison.Ordinal))
                .Select(segment => _fileSystem.GetValidFilename(segment));

            return Path.Combine(safeSegments.ToArray());
        }

        private static bool ShouldQueueLibraryScan()
        {
            var now = DateTime.UtcNow;
            bool shouldQueue;
            lock (ScanLock)
            {
                shouldQueue = now - _lastScanUtc >= ScanDebounceInterval;
                if (shouldQueue)
                {
                    _lastScanUtc = now;
                }
            }

            return shouldQueue;
        }

        private sealed class DestinationInfo
        {
            public string Name { get; set; } = string.Empty;

            public string Path { get; set; } = string.Empty;
        }

        private sealed class UploadResult
        {
            public int TotalFiles { get; set; }

            public int UploadedCount { get; set; }

            public List<string> Uploaded { get; set; } = new List<string>();

            public List<string> Failed { get; set; } = new List<string>();

            public string Message { get; set; } = string.Empty;
        }
    }
}
