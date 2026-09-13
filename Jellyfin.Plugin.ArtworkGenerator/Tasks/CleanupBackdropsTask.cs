using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Tasks
{
    /// <summary>
    /// A scheduled task, under Library in Dashboard &gt; Scheduled Tasks, that brings every item on
    /// the server down to a single backdrop.
    /// </summary>
    /// <remarks>
    /// Jellyfin adds a backdrop chosen in Edit Images after the ones an item already has rather than
    /// replacing them, and a generated backdrop is a full resolution frame, so an item whose backdrop
    /// is picked again and again keeps every copy. The first backdrop is the one clients show, so it
    /// is kept. The rest are removed from the item and then deleted from disk, since an entry removed
    /// without its file would be found again by the next library scan. It has no default schedule:
    /// it runs when started by hand or on a trigger the administrator adds.
    /// </remarks>
    public sealed class CleanupBackdropsTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<CleanupBackdropsTask> _logger;

        public CleanupBackdropsTask(ILibraryManager libraryManager, ILogger<CleanupBackdropsTask> logger)
        {
            _libraryManager = libraryManager;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Name => "Cleanup Backdrops";

        /// <inheritdoc />
        public string Key => "ArtworkGeneratorCleanupBackdrops";

        /// <inheritdoc />
        public string Description => "Reduces every item on the server to a single backdrop, keeping the first and deleting the rest along with their files.";

        /// <inheritdoc />
        public string Category => "Library";

        /// <inheritdoc />
        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(progress);

            // Only ids are listed up front; each item is loaded on its own, so a large library is
            // never held in memory at once.
            var ids = _libraryManager.GetItemIds(new InternalItemsQuery
            {
                ImageTypes = new[] { ImageType.Backdrop },
                Recursive = true
            });

            var trimmed = 0;
            var removed = 0;
            var freed = 0L;

            for (var i = 0; i < ids.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(100d * i / ids.Count);

                var item = _libraryManager.GetItemById(ids[i]);
                if (item == null)
                {
                    continue;
                }

                var extras = Extras(item.GetImages(ImageType.Backdrop).ToList());
                if (extras.Count == 0)
                {
                    continue;
                }

                try
                {
                    // The item is saved before any file goes, so a failed delete leaves a stray file
                    // rather than an item pointing at an image that is no longer there.
                    item.RemoveImages(extras);
                    await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);

                    var remaining = item.ImageInfos ?? Array.Empty<ItemImageInfo>();
                    foreach (var image in extras.Where(image => IsSafeToDelete(image, remaining)))
                    {
                        freed += DeleteFile(image.Path, item);
                    }

                    trimmed++;
                    removed += extras.Count;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not reduce the backdrops of {Name}", item.Name);
                }
            }

            progress.Report(100);
            _logger.LogInformation(
                "Cleanup Backdrops removed {Removed} backdrop(s) from {Items} item(s) and freed {Megabytes:F1} MB",
                removed,
                trimmed,
                freed / (1024d * 1024d));
        }

        /// <inheritdoc />
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        // Extras
        // Every backdrop after the first, which is the one clients show.
        internal static IReadOnlyList<ItemImageInfo> Extras(IReadOnlyList<ItemImageInfo> backdrops)
            => backdrops.Skip(1).ToList();

        // IsSafeToDelete
        // Only a file on disk that no image the item keeps also points at. A backdrop Jellyfin only
        // knows by its web address has no file to delete.
        internal static bool IsSafeToDelete(ItemImageInfo image, IEnumerable<ItemImageInfo> remaining)
            => image.IsLocalFile
                && !string.IsNullOrWhiteSpace(image.Path)
                && !remaining.Any(kept => string.Equals(kept.Path, image.Path, StringComparison.Ordinal));

        // DeleteFile
        // Deletes one backdrop file and returns how many bytes it held, or zero when it was already
        // gone or could not be deleted.
        private long DeleteFile(string path, BaseItem item)
        {
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists)
                {
                    return 0;
                }

                var length = file.Length;
                file.Delete();
                return length;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete backdrop {Path} of {Name}", path, item.Name);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Could not delete backdrop {Path} of {Name}", path, item.Name);
            }

            return 0;
        }
    }
}
