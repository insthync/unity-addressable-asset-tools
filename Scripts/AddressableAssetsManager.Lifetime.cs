#if !DISABLE_ADDRESSABLES
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Insthync.AddressableAssetTools
{
    public static partial class AddressableAssetsManager
    {
        internal static async UniTask<TResult[]> LoadInBatches<TSource, TResult>(
            IEnumerable<TSource> sources,
            Func<TSource, UniTask<TResult>> load,
            int maxConcurrentLoads)
        {
            if (maxConcurrentLoads < 1)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentLoads));
            var results = new List<TResult>();
            var batch = new List<UniTask<TResult>>(maxConcurrentLoads);
            foreach (TSource source in sources)
            {
                batch.Add(load(source));
                if (batch.Count < maxConcurrentLoads)
                    continue;
                results.AddRange(await UniTask.WhenAll(batch));
                batch.Clear();
            }
            if (batch.Count > 0)
                results.AddRange(await UniTask.WhenAll(batch));
            return results.ToArray();
        }

        internal static async UniTask<GameObject> InstantiateAndOwn(
            Func<AsyncOperationHandle<GameObject>> instantiate,
            Transform expectedParent = null)
        {
            bool parentRequired = expectedParent != null;
            AsyncOperationHandle<GameObject> handle = default;
            bool retainedByInstance = false;
            try
            {
                handle = instantiate();
                GameObject instance = await handle.ToUniTask();
                if (handle.Status != AsyncOperationStatus.Succeeded || instance == null)
                    throw handle.OperationException ?? new Exception("Unable to instantiate addressable object.");
                // The caller's hierarchy may disappear before the asset has finished loading.
                if (parentRequired && expectedParent == null)
                    return null;
                AssetReferenceReleaser releaser = instance.GetComponent<AssetReferenceReleaser>();
                if (releaser == null)
                    releaser = instance.AddComponent<AssetReferenceReleaser>();
                releaser.Initialize(handle);
                retainedByInstance = true;
                return instance;
            }
            finally
            {
                if (!retainedByInstance && handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        private static T GetComponentOrRelease<T>(GameObject instance)
            where T : Component
        {
            if (instance == null)
                return null;
            T component = instance.GetComponent<T>();
            if (component != null)
                return component;
            // A failed typed instantiation must not leave an unreachable live object behind.
            if (!Addressables.ReleaseInstance(instance))
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(instance);
                else
                    UnityEngine.Object.DestroyImmediate(instance);
            }
            return null;
        }
    }
}
#endif
