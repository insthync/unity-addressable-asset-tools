#if !DISABLE_ADDRESSABLES
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.Profiling;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Insthync.AddressableAssetTools
{
    public partial class AddressableAssetDownloadManager
    {
        private sealed class LocationReferenceComparer : IEqualityComparer<IResourceLocation>
        {
            public bool Equals(IResourceLocation x, IResourceLocation y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(IResourceLocation value)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
            }
        }

        internal static async Task<List<IResourceLocation>> CollectDownloadBundles(
            IEnumerable<IResourceLocator> locators,
            bool useBundleKeys)
        {
            var bundles = new List<IResourceLocation>();
            var identities = new HashSet<(string, string, string)>();
            var visited = new HashSet<IResourceLocation>(new LocationReferenceComparer());
            var pending = new Stack<IResourceLocation>();
            var budget = System.Diagnostics.Stopwatch.StartNew();
            int keyCount = 0;
            int resolvedKeys = 0;
            foreach (IResourceLocator locator in new List<IResourceLocator>(locators))
            {
                // Packed binary catalogs expose bundles under their bundle filenames.
                // Resolving asset keys, even with a bundle type filter, expands their metadata.
                Type locatorType = locator.GetType();
#if UNITY_EDITOR
                // Asset Database play mode resolves source assets, not downloadable bundles.
                // Enumerating its keys/locations imports and inspects the entire project.
                if (locatorType.Assembly.GetName().Name == "Unity.Addressables.Editor"
                    && locatorType.FullName == "UnityEditor.AddressableAssets.Settings.AddressableAssetSettingsLocator")
                {
                    LogDownloadDiagnostic("[Addressables Download] Skipping bundle scan for the Editor Asset Database locator; source assets load directly.");
                    continue;
                }
#endif
                bool binaryCatalog = useBundleKeys
                    && locatorType.Assembly == typeof(Addressables).Assembly
                    && locatorType.FullName == "UnityEngine.AddressableAssets.ResourceLocators.ContentCatalogData+ResourceLocator";
                int bundleKeyCount = 0;
                if (binaryCatalog)
                {
                    foreach (object key in locator.Keys)
                    {
                        keyCount++;
                        if (key is string name && name.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                        {
                            if (locator.Locate(key, typeof(IAssetBundleResource), out IList<IResourceLocation> locations))
                            {
                                bundleKeyCount++;
                                resolvedKeys++;
                                CollectBundleLocations(locations, pending, visited, identities, bundles);
                            }
                        }
                        if (budget.ElapsedMilliseconds >= 2)
                        {
                            await UniTask.Yield();
                            budget.Restart();
                        }
                    }
                }
                if (!binaryCatalog || bundleKeyCount == 0)
                {
                    foreach (object key in locator.Keys)
                    {
                        keyCount++;
                        resolvedKeys++;
                        if (locator.Locate(key, null, out IList<IResourceLocation> locations))
                            CollectBundleLocations(locations, pending, visited, identities, bundles);
                        if (budget.ElapsedMilliseconds >= 2)
                        {
                            await UniTask.Yield();
                            budget.Restart();
                        }
                    }
                }
                // Do not retain asset locations from fallback locators across catalogs.
                visited.Clear();
            }
            LogDownloadPlan(keyCount, resolvedKeys, bundles.Count);
            return bundles;
        }

        private static void CollectBundleLocations(
            IList<IResourceLocation> locations,
            Stack<IResourceLocation> pending,
            HashSet<IResourceLocation> visited,
            HashSet<(string, string, string)> identities,
            List<IResourceLocation> bundles)
        {
            for (int i = 0; i < locations.Count; i++)
                pending.Push(locations[i]);
            while (pending.Count > 0)
            {
                IResourceLocation location = pending.Pop();
                if (location == null || !visited.Add(location))
                    continue;
                if (location.ResourceType == typeof(IAssetBundleResource))
                {
                    var options = location.Data as AssetBundleRequestOptions;
                    if (identities.Add((location.InternalId, location.ProviderId, options?.Hash ?? "")))
                    {
                        IResourceLocation downloadLocation = location;
                        if (options != null && location.ProviderId.StartsWith(typeof(AssetBundleProvider).FullName, StringComparison.Ordinal))
                        {
                            // Each dependency is already in this plan. Flatten standard bundle
                            // downloads so a single bundle cannot expand the whole batch again.
                            var bundleLocation = new ResourceLocationBase(location.PrimaryKey,
                                location.InternalId, location.ProviderId, location.ResourceType);
                            bundleLocation.Data = options;
                            downloadLocation = bundleLocation;
                        }
                        bundles.Add(downloadLocation);
                    }
                }
                if (location.HasDependencies)
                {
                    for (int i = 0; i < location.Dependencies.Count; i++)
                        pending.Push(location.Dependencies[i]);
                }
            }
        }

        internal static IResourceLocation CreateDownloadSizeRoot(IList<IResourceLocation> bundles)
        {
            // GetDownloadSizeAsync examines dependencies, not a supplied bundle itself.
            // A size-only root ensures root bundles are counted too; never load this root.
            var dependencies = new IResourceLocation[bundles.Count];
            bundles.CopyTo(dependencies, 0);
            return new ResourceLocationBase("download-size", "download-size",
                "download-size-only", typeof(object), dependencies);
        }

        private static async Task<long> GetBundleDownloadSize(IList<IResourceLocation> bundles)
        {
            AsyncOperationHandle<long> handle = Addressables.GetDownloadSizeAsync(CreateDownloadSizeRoot(bundles));
            try
            {
                while (!handle.IsDone)
                    await UniTask.Yield();
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw handle.OperationException ?? new Exception("Unable to get bundle download size.");
                return handle.Result;
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        private static async Task DownloadAllBundles(
            IEnumerable<IResourceLocator> locators,
            bool useBundleKeys,
            int batchSize,
            Action onFileSizeRetrieving,
            AddressableAssetFileSizeDelegate onFileSizeRetrieved,
            Action onDepsDownloading,
            AddressableAssetDownloadProgressDelegate onProgress,
            Action onDepsDownloaded,
            Action<Exception> onError)
        {
            List<IResourceLocation> bundles = null;
            try
            {
                onFileSizeRetrieving?.Invoke();
                bundles = await CollectDownloadBundles(locators, useBundleKeys);
                LogDownloadMemory("bundle plan ready");
                long totalBytes = await GetBundleDownloadSize(bundles);
                LogDownloadMemory("download size ready");
                LogDownloadSize(totalBytes, batchSize);
                onFileSizeRetrieved?.Invoke(totalBytes);
                onDepsDownloading?.Invoke();
                await DownloadBundleBatches(bundles, batchSize, totalBytes,
                    GetBundleDownloadSize,
                    locations => Addressables.DownloadDependenciesAsync(locations, false),
                    onProgress);
                onProgress?.Invoke(totalBytes, totalBytes, 1f);
                onDepsDownloaded?.Invoke();
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                throw;
            }
            finally
            {
                bundles?.Clear();
                LogDownloadMemory("download operations released");
            }
        }

        internal static async Task DownloadBundleBatches(
            IList<IResourceLocation> bundles,
            int batchSize,
            long totalBytes,
            Func<IList<IResourceLocation>, Task<long>> getSize,
            Func<IList<IResourceLocation>, AsyncOperationHandle> download,
            AddressableAssetDownloadProgressDelegate onProgress)
        {
            if (batchSize < 1)
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            var batch = new List<IResourceLocation>(batchSize);
            try
            {
                long completedBytes = 0;
                if (totalBytes > 0)
                {
                    for (int offset = 0; offset < bundles.Count; offset += batchSize)
                    {
                        batch.Clear();
                        int count = Math.Min(batchSize, bundles.Count - offset);
                        for (int i = 0; i < count; i++)
                            batch.Add(bundles[offset + i]);
                        long batchBytes = await getSize(batch);
                        if (batchBytes > 0)
                        {
                            AsyncOperationHandle handle = download(batch);
                            try
                            {
                                while (!handle.IsDone)
                                {
                                    await UniTask.Yield();
                                    if (!handle.IsDone)
                                    {
                                        float batchProgress = Mathf.Clamp01(handle.GetDownloadStatus().Percent);
                                        long bytes = Math.Min(totalBytes, completedBytes + (long)(batchProgress * batchBytes));
                                        onProgress?.Invoke(bytes, totalBytes, (float)((double)bytes / totalBytes));
                                    }
                                }
                                if (handle.Status != AsyncOperationStatus.Succeeded)
                                    throw handle.OperationException ?? new Exception("Unable to download bundle batch.");
                            }
                            finally
                            {
                                if (handle.IsValid())
                                    Addressables.Release(handle);
                            }
                        }
                        completedBytes = Math.Min(totalBytes, completedBytes + batchBytes);
                        onProgress?.Invoke(completedBytes, totalBytes, (float)((double)completedBytes / totalBytes));
                        await UniTask.Yield();
                    }
                }
            }
            finally
            {
                batch.Clear();
            }
        }

        private static void LogDownloadDiagnostic(string message)
        {
            // Frequent memory samples do not need an allocated stack trace per message.
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogDownloadSize(long bytes, int batchSize)
        {
            LogDownloadDiagnostic($"[Addressables Download] Pending download: {bytes:N0} bytes; batch limit: {batchSize} bundles.");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogDownloadPlan(int keys, int resolvedKeys, int bundles)
        {
            LogDownloadDiagnostic($"[Addressables Download] Catalog keys: {keys:N0}; resolved keys: {resolvedKeys:N0}; unique bundles: {bundles:N0}.");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogDownloadMemory(string stage)
        {
            const double mib = 1024d * 1024d;
            int loadedBundles = 0;
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
                loadedBundles++;
            LogDownloadDiagnostic($"[Addressables Download] {stage}: managed used {Profiler.GetMonoUsedSizeLong() / mib:F1} MiB; "
                + $"managed heap {Profiler.GetMonoHeapSizeLong() / mib:F1} MiB; "
                + $"Unity allocated {Profiler.GetTotalAllocatedMemoryLong() / mib:F1} MiB; "
                + $"Unity reserved {Profiler.GetTotalReservedMemoryLong() / mib:F1} MiB; loaded bundles {loadedBundles}.");
        }
    }
}
#endif
