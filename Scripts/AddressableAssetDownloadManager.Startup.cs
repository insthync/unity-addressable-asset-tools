#if !DISABLE_ADDRESSABLES
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Experimental.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Insthync.AddressableAssetTools
{
    public partial class AddressableAssetDownloadManager
    {
        // Warmed graphics state collections stay loaded for the app lifetime. Releasing them could
        // unload their shader bundles before first use and discard the compiled programs/pipelines.
        private static readonly List<AsyncOperationHandle<GraphicsStateCollection>> s_retainedGraphicsStateHandles =
            new List<AsyncOperationHandle<GraphicsStateCollection>>();

        private async UniTask<bool> InitializeStartupAssets(CancellationToken cancellationToken)
        {
            AsyncOperationHandle<AddressableAssetDownloadManagerSettings> handle = default;
            try
            {
                Debug.Log("Read addressable asset download manager settings.");
                AddressableAssetDownloadManagerSettings settings;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    handle = settingsAssetReference.LoadAssetAsync();
                    await handle.Task;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                        throw handle.OperationException ?? new Exception("Unable to load addressable startup settings.");
                    settings = handle.Result;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Unable to load settings {ex.Message}\n{ex.StackTrace}");
                    AsyncOperationStatus status = handle.IsValid() ? handle.Status : AsyncOperationStatus.Failed;
                    onUnableToLoadSettings.Invoke(status, ex);
                    return false;
                }

                LogDownloadMemory("settings loaded");
                IList<AssetReference> initialObjects = settings.InitialObjects;
                for (int i = 0; i < initialObjects.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AssetReference asset = initialObjects[i];
                    if (asset == null)
                    {
                        Debug.LogWarning($"Null initial object {i}, skipping...");
                        continue;
                    }
                    await InitializeStartupObject(asset.RuntimeKey, cancellationToken);
                    // Give Unity a frame between object activations, including cached loads.
                    await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
                }
                LogDownloadMemory("initial objects ready");

                var warmedCollections = new HashSet<object>();
                IList<AssetReferenceShaderVariantCollection> collections = settings.ShaderVariantCollections;
                for (int i = 0; i < collections.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AssetReferenceShaderVariantCollection collection = collections[i];
                    if (collection == null)
                    {
                        Debug.LogWarning($"Null shader variant collection {i}, skipping...");
                        continue;
                    }
                    object key = collection.RuntimeKey;
                    if (!warmedCollections.Add(key))
                        continue;
                    await WarmUpStartupShaders(key, cancellationToken);
                    await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
                }
                LogDownloadMemory("shader warmup ready");

                IList<AssetReferenceT<GraphicsStateCollection>> graphicsStateCollections = settings.GraphicsStateCollections;
                for (int i = 0; i < graphicsStateCollections.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AssetReferenceT<GraphicsStateCollection> collection = graphicsStateCollections[i];
                    if (collection == null || !collection.RuntimeKeyIsValid())
                        continue;
                    object key = collection.RuntimeKey;
                    if (!warmedCollections.Add(key))
                        continue;
                    await WarmUpStartupGraphicsStates(key, cancellationToken);
                    await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
                }
                LogDownloadMemory("graphics state warmup ready");
                return true;
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                LogDownloadMemory("settings released");
            }
        }

        private async UniTask InitializeStartupObject(object key, CancellationToken cancellationToken)
        {
            AsyncOperationHandle<GameObject> handle = default;
            bool retainedByInstance = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            LogDownloadMemory($"before initial object {key}");
            try
            {
                Debug.Log($"Initializing {key}");
                handle = Addressables.InstantiateAsync(key);
                await handle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw handle.OperationException ?? new Exception($"Unable to instantiate startup object: {key}");
                GameObject instance = handle.Result;
                AssetReferenceReleaser releaser = instance.GetComponent<AssetReferenceReleaser>();
                if (releaser == null)
                    releaser = instance.AddComponent<AssetReferenceReleaser>();
                releaser.Initialize(handle);
                // The live instance owns this handle until its releaser runs on destruction.
                retainedByInstance = true;
                Debug.Log($"Initialized {instance.name}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unable to initialize {key} {ex.Message}\n{ex.StackTrace}");
                AsyncOperationStatus status = handle.IsValid() ? handle.Status : AsyncOperationStatus.Failed;
                onUnableToInitialObject.Invoke(key, status, ex);
            }
            finally
            {
                if (!retainedByInstance && handle.IsValid())
                    Addressables.Release(handle);
                LogStartupDuration("initial object", key, clock.ElapsedMilliseconds);
                LogDownloadMemory($"after initial object {key}");
            }
        }

        private async UniTask WarmUpStartupShaders(object key, CancellationToken cancellationToken)
        {
            AsyncOperationHandle<ShaderVariantCollection> handle = default;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            LogDownloadMemory($"before shader collection {key}");
            try
            {
                Debug.Log($"Warming up shader variant collection {key}");
                // Use an independent handle; do not reuse an AssetReference's cached load handle.
                handle = Addressables.LoadAssetAsync<ShaderVariantCollection>(key);
                await handle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw handle.OperationException ?? new Exception($"Unable to load shader variant collection: {key}");
                ShaderVariantCollection collection = handle.Result;
                LogShaderVariantCount(key, collection.variantCount, collection.warmedUpVariantCount);
                LogDownloadMemory($"shader collection loaded {key}");
                if (!collection.isWarmedUp)
                    await WarmUpShaderVariants(collection.WarmUpProgressively,
                        Mathf.Max(1, shaderVariantsPerFrame), cancellationToken);
                Debug.Log($"Warmed up shader variant collection {collection.name}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unable to warm up shader collection {key} {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
                LogStartupDuration("shader collection", key, clock.ElapsedMilliseconds);
                LogDownloadMemory($"shader collection released {key}");
            }
        }

        private async UniTask WarmUpStartupGraphicsStates(object key, CancellationToken cancellationToken)
        {
            AsyncOperationHandle<GraphicsStateCollection> handle = default;
            bool retained = false;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                handle = Addressables.LoadAssetAsync<GraphicsStateCollection>(key);
                await handle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                    throw handle.OperationException ?? new Exception($"Unable to load graphics state collection: {key}");
                GraphicsStateCollection collection = handle.Result;
                if (collection.runtimePlatform != Application.platform ||
                    collection.graphicsDeviceType != SystemInfo.graphicsDeviceType)
                {
                    // Recorded for another platform or graphics API; release it
                    return;
                }
                LogDownloadDiagnostic($"[Addressables Download] Graphics state collection {collection.name}: {collection.totalGraphicsStateCount:N0} states, {collection.variantCount:N0} variants.");
                int perFrame = Mathf.Max(1, graphicsStatesPerFrame);
                while (!collection.isWarmedUp)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    collection.WarmUpProgressively(perFrame).Complete();
                    await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
                }
                s_retainedGraphicsStateHandles.Add(handle);
                retained = true;
                Debug.Log($"Warmed up graphics state collection {collection.name}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Unable to warm up graphics state collection {key} {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (!retained && handle.IsValid())
                    Addressables.Release(handle);
                LogStartupDuration("graphics state collection", key, clock.ElapsedMilliseconds);
            }
        }

        internal static async UniTask WarmUpShaderVariants(
            Func<int, bool> warmUpStep,
            int variantsPerFrame,
            CancellationToken cancellationToken)
        {
            if (variantsPerFrame < 1)
                throw new ArgumentOutOfRangeException(nameof(variantsPerFrame));
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (warmUpStep(variantsPerFrame))
                    return;
                await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogStartupDuration(string stage, object key, long milliseconds)
        {
            LogDownloadDiagnostic($"[Addressables Download] {stage} {key}: {milliseconds:N0} ms.");
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogShaderVariantCount(object key, int total, int warmed)
        {
            LogDownloadDiagnostic($"[Addressables Download] Shader collection {key}: {total:N0} variants; {warmed:N0} already warmed.");
        }
    }
}
#endif
