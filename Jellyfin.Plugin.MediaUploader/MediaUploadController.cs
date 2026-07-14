using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading; // Required for CancellationToken (used in commented out code)
using System.Threading.Tasks;
using Jellyfin.Data.Enums; // Required for BaseItemKind
using Jellyfin.Plugin.MediaUploader.Configuration; // Required for PluginConfiguration
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities; // Required for CollectionFolder
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Querying; // Required for InternalItemsQuery
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MediaUploader
{
    /// <summary>
    /// API Controller for handling media uploads.
    /// </summary>
    [ApiController]
    [Route("Plugins/MediaUploader")] // Base route for this controller
    public class MediaUploadController : ControllerBase
    {
        private readonly ILogger<MediaUploadController> _logger;
        private readonly IServerConfigurationManager _configurationManager;
        private readonly IFileSystem _fileSystem;
        private readonly ILibraryManager _libraryManager;

        // Debounce library scans so sequential per-file uploads don't queue a full scan each time.
        private static readonly TimeSpan ScanDebounceInterval = TimeSpan.FromSeconds(30);
        private static readonly object ScanLock = new object();
        private static DateTime _lastScanUtc = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="MediaUploadController"/> class.
        /// </summary>
        /// <param name="logger">The logger instance.</param>
        /// <param name="configurationManager">The server configuration manager instance.</param>
        /// <param name="fileSystem">The file system abstraction instance.</param>
        /// <param name="libraryManager">The library manager instance.</param>
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

        /// <summary>
        /// Returns the configured destination presets for the upload page.
        /// </summary>
        /// <returns>A list of destination presets (name + path relative to the base upload path).</returns>
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

        /// <summary>
        /// Handles the file upload POST request.
        /// Accepts one or more files via multipart/form-data (field name "files", or "file" for a single file)
        /// and an optional "destination" field containing a path relative to the configured base upload directory.
        /// Folder uploads preserve their relative structure via the file names provided by the browser.
        /// </summary>
        /// <returns>An IActionResult indicating the result of the upload operation.</returns>
        [HttpPost("Upload")] // Route: /Plugins/MediaUploader/Upload
        [RequestSizeLimit(10L * 1024 * 1024 * 1024)] // Explicit 10 GB total request limit
        [RequestFormLimits(MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024)]
#pragma warning disable SA1404
        [SuppressMessage("Reliability", "CA2007:Aufruf von \"ConfigureAwait\" für erwarteten Task erwägen", Justification = "<Pending>")] // Matching high limit for multipart section (workaround)
#pragma warning restore SA1404
        public async Task<IActionResult> UploadFile()
        {
            _logger.LogInformation("Media Uploader: UploadFile endpoint hit.");

            // --- 1. Get and Validate Configuration ---
            var configuredPath = Plugin.Instance?.Configuration.UploadPath;
            if (string.IsNullOrEmpty(configuredPath))
            {
                _logger.LogError("Media Uploader: Upload path is not configured in plugin settings!");
                return StatusCode(StatusCodes.Status500InternalServerError, "Upload path is not configured in plugin settings.");
            }

            // Resolve the base directory that everything must be written under.
            var baseDirectory = Path.GetFullPath(configuredPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            _logger.LogInformation("Media Uploader: Using configured base directory: '{BaseDirectory}'", baseDirectory);

            try
            {
                // --- 2. Read the multipart form data ---
                var form = await Request.ReadFormAsync(CancellationToken.None).ConfigureAwait(false);
                var formFiles = form.Files;

                // Support both the multi-file field "files" and the legacy single-file field "file".
                var files = formFiles.GetFiles("files").ToList();
                var singleFile = formFiles.GetFile("file");
                if (singleFile != null)
                {
                    files.Add(singleFile);
                }

                var destination = form["destination"].ToString();

                if (files.Count == 0)
                {
                    _logger.LogWarning("Media Uploader: No file uploaded.");
                    return BadRequest("No file uploaded.");
                }

                // Build the sanitized relative destination sub-path (e.g. "movies/My Movie (2024)").
                var destinationRelative = BuildSafeRelativePath(destination);

                // --- 3. Save each file, preserving relative folder structure ---
                var uploaded = new List<string>();
                var failed = new List<string>();

                foreach (var file in files)
                {
                    if (file == null || file.Length == 0)
                    {
                        _logger.LogWarning("Media Uploader: Skipping empty file entry.");
                        continue;
                    }

                    try
                    {
                        // The browser may supply a relative path (folder uploads) inside the file name.
                        var fileRelative = BuildSafeRelativePath(file.FileName);

                        var fullTargetPath = Path.GetFullPath(Path.Combine(baseDirectory, destinationRelative, fileRelative));
                        var basePrefix = baseDirectory + Path.DirectorySeparatorChar;

                        // Security Check: ensure the resolved path stays within the base directory.
                        if (!fullTargetPath.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogError(
                                "Media Uploader: Invalid target path generated. Attempted relative '{DestinationRelative}' + '{FileRelative}', resolved to '{ResolvedPath}', base directory '{BaseDirectory}'",
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

                        _logger.LogInformation("Media Uploader: Saving '{FileName}' to '{FullTargetPath}'", file.FileName, fullTargetPath);
                        await using (var fileStream = new FileStream(fullTargetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await file.CopyToAsync(fileStream, CancellationToken.None).ConfigureAwait(false);
                        }

                        uploaded.Add(fullTargetPath);
                        _logger.LogInformation("Media Uploader: File '{FileName}' saved successfully.", file.FileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Media Uploader: Error saving file '{FileName}'", file.FileName);
                        failed.Add(file.FileName);
                    }
                }

                if (uploaded.Count == 0)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, "No files could be saved. Check server logs and permissions.");
                }

                // --- 4. Trigger a library scan (best effort, debounced) so new files are picked up ---
                // Debounced because sequential per-file uploads would otherwise queue a full scan per file.
                try
                {
                    if (ShouldQueueLibraryScan())
                    {
                        _logger.LogInformation("Media Uploader: Queuing a library scan.");
                        _libraryManager.QueueLibraryScan();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Media Uploader: Error requesting library validation.");
                    // Don't fail the upload if the scan trigger fails.
                }

                // --- 5. Return a summary result ---
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
            catch (IOException ioEx) // Handle specific IO errors during file operations
            {
                _logger.LogError(ioEx, "Media Uploader: IO Error during upload process: {ErrorMessage}", ioEx.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"IO Error: {ioEx.Message}");
            }
            catch (UnauthorizedAccessException authEx) // Handle permission errors
            {
                _logger.LogError(authEx, "Media Uploader: Permission denied during upload process: {ErrorMessage}", authEx.Message);
                return StatusCode(StatusCodes.Status403Forbidden, $"Permission denied: {authEx.Message}");
            }
            catch (Exception ex) // Catch-all for other unexpected errors
            {
                _logger.LogError(ex, "Media Uploader: Unexpected error processing file upload: {ErrorMessage}", ex.Message);
                return StatusCode(StatusCodes.Status500InternalServerError, $"Unexpected error uploading file: {ex.Message}");
            }
        }

        /// <summary>
        /// Serves the static HTML page for direct uploads.
        /// </summary>
        /// <returns>An HTML page as ContentResult.</returns>
        [HttpGet("Page")] // Route: /Plugins/MediaUploader/Page
        [Produces("text/html")]
        public IActionResult GetUploadPage()
        {
            _logger.LogInformation("Media Uploader: Serving static upload page request.");
            try
            {
                var assembly = typeof(MediaUploadController).Assembly;
                // Ensure this resource name exactly matches Namespace.Folder.FileName.ext
                var resourceName = "Jellyfin.Plugin.MediaUploader.Web.uploadPage.html";

                using var stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    _logger.LogError("Media Uploader: Could not find embedded resource: {ResourceName}. Check file exists, path/namespace, and Build Action='Embedded resource'.", resourceName);
                    return NotFound($"Resource not found: {resourceName}");
                }

                using var reader = new StreamReader(stream, Encoding.UTF8);
                var htmlContent = reader.ReadToEnd();

                return Content(htmlContent, "text/html", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Media Uploader: Error serving static upload page");
                 return StatusCode(StatusCodes.Status500InternalServerError, "Error serving upload page");
            }
        }

        /// <summary>
        /// Builds a sanitized, traversal-safe relative path from user supplied input.
        /// Each path segment is run through <see cref="IFileSystem.GetValidFilename"/> and
        /// combined without any ".." segments being able to escape the base directory.
        /// </summary>
        /// <param name="inputPath">The raw path supplied by the user or browser.</param>
        /// <returns>A sanitized relative path, or an empty string when no input is given.</returns>
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

        /// <summary>
        /// Returns true at most once per <see cref="ScanDebounceInterval"/> so that a burst of
        /// uploads (e.g. one request per file) does not queue a full library scan for every file.
        /// </summary>
        /// <returns>True when a scan should be queued now.</returns>
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

        /// <summary>
        /// A named upload destination returned by the Destinations endpoint.
        /// </summary>
        private sealed class DestinationInfo
        {
            public string Name { get; set; } = string.Empty;

            public string Path { get; set; } = string.Empty;
        }

        /// <summary>
        /// The summary returned after an upload request.
        /// </summary>
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
