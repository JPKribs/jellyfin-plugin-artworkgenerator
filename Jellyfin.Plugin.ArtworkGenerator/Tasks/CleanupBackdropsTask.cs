using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ArtworkGenerator.Utilities;
using JPKribs.Jellyfin.Base;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ArtworkGenerator.Tasks
{
    /// <summary>
    /// A scheduled task, under Library in Dashboard &gt; Scheduled Tasks, that removes the extra
    /// backdrops the plugin generated.
    /// </summary>
    /// <remarks>
    /// Jellyfin adds a backdrop chosen in Edit Images after the ones an item already has rather than
    /// replacing them, and a generated backdrop is a full resolution frame, so an item whose backdrop
    /// is picked again and again keeps every copy. The first backdrop is the one clients show, so it
    /// is always kept. Of the rest, only the ones the plugin generated, which it tags when it renders
    /// them, are removed from the item and then deleted from disk, since an entry removed without
    /// its file would be found again by the next library scan. Backdrops from anywhere else, such as
    /// fanart beside the media or images from another provider, are left alone, and a file another
    /// item still uses is never deleted. It has no default schedule: it runs when started by hand or
    /// on a trigger the administrator adds.
    /// </remarks>
    public sealed class CleanupBackdropsTask : PluginScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger<CleanupBackdropsTask> _logger;
        private readonly Func<BaseItem, CancellationToken, Task> _saveItem;

        public CleanupBackdropsTask(ILibraryManager libraryManager, ILogger<CleanupBackdropsTask> logger)
            : this(libraryManager, logger, (item, cancellationToken) => item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken))
        {
        }

        // A constructor taking how an item is saved, so the task can be run in full by tests
        // without a library database behind it.
        internal CleanupBackdropsTask(ILibraryManager libraryManager, ILogger<CleanupBackdropsTask> logger, Func<BaseItem, CancellationToken, Task> saveItem)
        {
            _libraryManager = libraryManager;
            _logger = logger;
            _saveItem = saveItem;
        }

        /// <inheritdoc />
        public override string Name => "Cleanup Backdrops";

        /// <inheritdoc />
        public override string Key => "ArtworkGeneratorCleanupBackdrops";

        /// <inheritdoc />
        public override string Description => "Removes the extra backdrops Artwork Generator created, keeping each item's first backdrop and any backdrop from another source, and deletes their files.";

        /// <inheritdoc />
        public override string Category => "Library";

        /// <inheritdoc />
        public override async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(progress);

            // Only ids are listed up front; each item is loaded on its own, so a large library is
            // never held in memory at once.
            var ids = _libraryManager.GetItemIds(new InternalItemsQuery
            {
                ImageTypes = new[] { ImageType.Backdrop },
                Recursive = true
            });

            // First pass: the items with generated extras to remove, and the files those extras use.
            var candidates = new List<Guid>();
            var candidatePaths = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < ids.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(40d * i / ids.Count);

                var item = _libraryManager.GetItemById(ids[i]);
                var removable = item == null ? Array.Empty<ItemImageInfo>() : Removable(item.GetImages(ImageType.Backdrop).ToList());
                if (removable.Count > 0)
                {
                    candidates.Add(item!.Id);
                    candidatePaths.UnionWith(removable.Select(image => image.Path));
                }
            }

            // Second pass, only when there is something to remove: count every use of those files
            // by any image of any item, so a file another item still shows, as a backdrop or as
            // anything else, is never deleted.
            var references = new Dictionary<string, int>(StringComparer.Ordinal);
            if (candidates.Count > 0)
            {
                var withImages = _libraryManager.GetItemIds(new InternalItemsQuery
                {
                    ImageTypes = Enum.GetValues<ImageType>(),
                    Recursive = true
                });

                for (var i = 0; i < withImages.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress.Report(40 + (40d * i / withImages.Count));

                    AddReferences(references, _libraryManager.GetItemById(withImages[i])?.ImageInfos, candidatePaths);
                }
            }

            var trimmed = 0;
            var removed = 0;
            var freed = 0L;

            for (var i = 0; i < candidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(80 + (20d * i / candidates.Count));

                var item = _libraryManager.GetItemById(candidates[i]);
                if (item == null)
                {
                    continue;
                }

                var extras = Removable(item.GetImages(ImageType.Backdrop).ToList());
                if (extras.Count == 0)
                {
                    continue;
                }

                try
                {
                    // The item is saved before any file goes, so a failed delete leaves a stray file
                    // rather than an item pointing at an image that is no longer there.
                    item.RemoveImages(extras);
                    await _saveItem(item, cancellationToken).ConfigureAwait(false);

                    foreach (var image in extras.Where(image => ReleaseReference(references, image.Path)))
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
                "Cleanup Backdrops removed {Removed} generated backdrop(s) from {Items} item(s) and freed {Megabytes:F1} MB",
                removed,
                trimmed,
                freed / (1024d * 1024d));
        }

        // Extras
        // Every backdrop after the first, which is the one clients show.
        internal static IReadOnlyList<ItemImageInfo> Extras(IReadOnlyList<ItemImageInfo> backdrops)
            => backdrops.Skip(1).ToList();

        // Removable
        // The extras the plugin generated. Anything else, local fanart or another provider's image,
        // stays on the item.
        internal static IReadOnlyList<ItemImageInfo> Removable(IReadOnlyList<ItemImageInfo> backdrops)
            => Extras(backdrops).Where(IsGenerated).ToList();

        // IsGenerated
        // A backdrop file on disk that carries the tag the plugin puts on the backdrops it renders.
        // A backdrop Jellyfin only knows by its web address was not saved from the plugin.
        internal static bool IsGenerated(ItemImageInfo image)
            => image.IsLocalFile
                && !string.IsNullOrWhiteSpace(image.Path)
                && GeneratedImageMarker.IsTaggedFile(image.Path);

        // AddReferences
        // Counts one use of each local file among an item's images, only of the given files when
        // a set is passed.
        internal static void AddReferences(IDictionary<string, int> references, IEnumerable<ItemImageInfo>? images, ISet<string>? only = null)
        {
            foreach (var image in images ?? Enumerable.Empty<ItemImageInfo>())
            {
                if (image.IsLocalFile
                    && !string.IsNullOrWhiteSpace(image.Path)
                    && (only == null || only.Contains(image.Path)))
                {
                    references[image.Path] = references.TryGetValue(image.Path, out var count) ? count + 1 : 1;
                }
            }
        }

        // ReleaseReference
        // Drops one use of a file and returns true once no item uses it any more, which is the only
        // time it is safe to delete.
        internal static bool ReleaseReference(IDictionary<string, int> references, string path)
        {
            if (!references.TryGetValue(path, out var count))
            {
                return false;
            }

            if (count > 1)
            {
                references[path] = count - 1;
                return false;
            }

            references.Remove(path);
            return true;
        }

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
