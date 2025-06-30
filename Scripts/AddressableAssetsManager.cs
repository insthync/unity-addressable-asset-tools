using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Insthync.AddressableAssetTools
{
    public static class AddressableAssetsManager
    {
        private static readonly Dictionary<object, Object> s_loadedAssets = new Dictionary<object, Object>();
        private static readonly Dictionary<object, AsyncOperationHandle> s_assetRefs = new Dictionary<object, AsyncOperationHandle>();

        public static async UniTask<AsyncOperationHandle<TType>?> LoadObjectAsync<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            // Check if the asset is actually marked as Addressable
            if (assetRef.IsDataValid())
            {
                Debug.LogWarning($"Asset is not marked as Addressable: {assetRef.RuntimeKey}. Ignoring load.");
                return null;
            }

            // Check if the Addressable asset exists before loading
            AsyncOperationHandle<IList<IResourceLocation>> loadResourceLocationsHandle = Addressables.LoadResourceLocationsAsync(assetRef.RuntimeKey);
            IList<IResourceLocation> locations = await loadResourceLocationsHandle.ToUniTask();
            loadResourceLocationsHandle.Release();
            if (locations == null || locations.Count == 0)
            {
                Debug.LogWarning($"Addressable asset not found: {assetRef.RuntimeKey}. Ignoring load.");
                return null;
            }

            AsyncOperationHandle<TType> handler = Addressables.LoadAssetAsync<TType>(assetRef.RuntimeKey);
            handlerCallback?.Invoke(handler);
            TType handlerResult;
            try
            {
                handlerResult = await handler.ToUniTask();
            }
            catch
            {
            }
            return handler;
        }

        public static AsyncOperationHandle<TType>? LoadObject<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            // Check if the asset is actually marked as Addressable
            if (assetRef.IsDataValid())
            {
                Debug.LogWarning($"Asset is not marked as Addressable: {assetRef.RuntimeKey}. Ignoring load.");
                return null;
            }

            // Check if the Addressable asset exists before loading
            AsyncOperationHandle<IList<IResourceLocation>> loadResourceLocationsHandle = Addressables.LoadResourceLocationsAsync(assetRef.RuntimeKey);
            IList<IResourceLocation> locations = loadResourceLocationsHandle.WaitForCompletion();
            loadResourceLocationsHandle.Release();
            if (locations == null || locations.Count == 0)
            {
                Debug.LogWarning($"Addressable asset not found: {assetRef.RuntimeKey}. Ignoring load.");
                return null;
            }

            AsyncOperationHandle<TType> handler = Addressables.LoadAssetAsync<TType>(assetRef.RuntimeKey);
            handlerCallback?.Invoke(handler);
            TType handlerResult;
            try
            {
                handlerResult = handler.WaitForCompletion();
            }
            catch
            {
            }
            return handler;
        }

        public static async UniTask<TType> GetOrLoadObjectAsync<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            if (s_loadedAssets.TryGetValue(assetRef.RuntimeKey, out Object result))
                return result as TType;

            AsyncOperationHandle<TType>? handler = await assetRef.LoadObjectAsync<TType>(handlerCallback);
            if (!handler.HasValue)
                return null;

            TType handlerResult = handler.Value.Result;
            s_loadedAssets[assetRef.RuntimeKey] = handlerResult;
            s_assetRefs[assetRef.RuntimeKey] = handler.Value;
            return handlerResult;
        }

        public static TType GetOrLoadObject<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            if (s_loadedAssets.TryGetValue(assetRef.RuntimeKey, out Object result))
                return result as TType;

            AsyncOperationHandle<TType>? handler = assetRef.LoadObject<TType>(handlerCallback);
            if (!handler.HasValue)
                return null;

            TType handlerResult = handler.Value.Result;
            s_loadedAssets[assetRef.RuntimeKey] = handlerResult;
            s_assetRefs[assetRef.RuntimeKey] = handler.Value;
            return handlerResult;
        }

        public static async UniTask<TType> GetOrLoadAssetAsync<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            GameObject loadedObject = await assetRef.GetOrLoadAssetAsync(handlerCallback);
            if (loadedObject != null)
                return loadedObject.GetComponent<TType>();
            return null;
        }

        public static TType GetOrLoadAsset<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            GameObject loadedObject = assetRef.GetOrLoadAsset(handlerCallback);
            if (loadedObject != null)
                return loadedObject.GetComponent<TType>();
            return null;
        }

        public static async UniTask<GameObject> GetOrLoadAssetAsync(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            return await assetRef.GetOrLoadObjectAsync<GameObject>(handlerCallback);
        }

        public static GameObject GetOrLoadAsset(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            return assetRef.GetOrLoadObject<GameObject>(handlerCallback);
        }

        public static async UniTask<TType> GetOrLoadAssetAsyncOrUsePrefab<TType>(this AssetReference assetRef, TType prefab, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            TType tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = await assetRef.GetOrLoadAssetAsync<TType>(handlerCallback);
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }

        public static TType GetOrLoadAssetOrUsePrefab<TType>(this AssetReference assetRef, TType prefab, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            TType tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = assetRef.GetOrLoadAsset<TType>(handlerCallback);
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }

        public static async UniTask<TType> GetOrLoadObjectAsyncOrUseAsset<TType>(this AssetReference assetRef, TType asset, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            TType tempAsset = null;
            if (assetRef.IsDataValid())
                tempAsset = await assetRef.GetOrLoadObjectAsync<TType>(handlerCallback);
            if (tempAsset == null)
                tempAsset = asset;
            return tempAsset;
        }
        
        public static TType GetOrLoadObjectOrUseAsset<TType>(this AssetReference assetRef, TType asset, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            TType tempAsset = null;
            if (assetRef.IsDataValid())
                tempAsset = assetRef.GetOrLoadObject<TType>(handlerCallback);
            if (tempAsset == null)
                tempAsset = asset;
            return tempAsset;
        }
        
        public static async UniTask<GameObject> GetOrLoadAssetAsyncOrUsePrefab(this AssetReference assetRef, GameObject prefab, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            GameObject tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = await assetRef.GetOrLoadAssetAsync(handlerCallback);
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }
        
        public static GameObject GetOrLoadAssetOrUsePrefab(this AssetReference assetRef, GameObject prefab, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            GameObject tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = assetRef.GetOrLoadAsset(handlerCallback);
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }

        public static async UniTask<TType[]> GetOrLoadObjectsAsync<TType>(this IEnumerable<AssetReference> assetRefs, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            List<UniTask<TType>> tasks = new List<UniTask<TType>>();
            foreach (AssetReference assetRef in assetRefs)
            {
                tasks.Add(assetRef.GetOrLoadObjectAsync<TType>(handlerCallback));
            }
            return await UniTask.WhenAll(tasks);
        }

        public static TType[] GetOrLoadObjects<TType>(this IEnumerable<AssetReference> assetRefs, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Object
        {
            List<TType> results = new List<TType>();
            foreach (AssetReference assetRef in assetRefs)
            {
                results.Add(assetRef.GetOrLoadObject<TType>(handlerCallback));
            }
            return results.ToArray();
        }

        public static async UniTask<TType[]> GetOrLoadAssetsAsync<TType>(this IEnumerable<AssetReference> assetRefs, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            List<UniTask<TType>> tasks = new List<UniTask<TType>>();
            foreach (AssetReference assetRef in assetRefs)
            {
                tasks.Add(assetRef.GetOrLoadAssetAsync<TType>(handlerCallback));
            }
            return await UniTask.WhenAll(tasks);
        }

        public static TType[] GetOrLoadAssets<TType>(this IEnumerable<AssetReference> assetRefs, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            List<TType> results = new List<TType>();
            foreach (AssetReference assetRef in assetRefs)
            {
                results.Add(assetRef.GetOrLoadAsset<TType>(handlerCallback));
            }
            return results.ToArray();
        }
        
        public static async UniTask<GameObject[]> GetOrLoadAssetsAsync(this IEnumerable<AssetReference> assetRefs, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            List<UniTask<GameObject>> tasks = new List<UniTask<GameObject>>();
            foreach (AssetReference assetRef in assetRefs)
            {
                tasks.Add(assetRef.GetOrLoadAssetAsync(handlerCallback));
            }
            return await UniTask.WhenAll(tasks);
        }

        public static GameObject[] GetOrLoadAssets(this IEnumerable<AssetReference> assetRefs, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            List<GameObject> results = new List<GameObject>();
            foreach (AssetReference assetRef in assetRefs)
            {
                results.Add(assetRef.GetOrLoadAsset(handlerCallback));
            }
            return results.ToArray();
        }
        
        public static void Release<TAssetRef>(this TAssetRef assetRef)
            where TAssetRef : AssetReference
        {
            Release(assetRef.RuntimeKey);
        }

        public static void Release(object runtimeKey)
        {
            if (s_assetRefs.TryGetValue(runtimeKey, out AsyncOperationHandle handler))
                Addressables.Release(handler);
            s_assetRefs.Remove(runtimeKey);
            s_loadedAssets.Remove(runtimeKey);
        }

        public static void ReleaseAll()
        {
            List<object> keys = new List<object>(s_assetRefs.Keys);
            for (int i = 0; i < keys.Count; ++i)
            {
                Release(keys[i]);
            }
        }
    }
}