#if !DISABLE_ADDRESSABLES
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Insthync.AddressableAssetTools
{
    public static partial class AddressableAssetsManager
    {
        private sealed class PendingLoad
        {
            public bool ReleaseRequested;
            public AsyncOperationHandle Handle;
        }

        private static readonly Dictionary<object, PendingLoad> s_loadingAssets = new Dictionary<object, PendingLoad>();
        private static readonly Dictionary<object, AsyncOperationHandle> s_loadedAssets = new Dictionary<object, AsyncOperationHandle>();
        private static readonly List<AsyncOperationHandle<SceneInstance>> s_addressableSceneHandles = new List<AsyncOperationHandle<SceneInstance>>();
        private static readonly Dictionary<object, IList<IResourceLocation>> s_resourceLocations = new Dictionary<object, IList<IResourceLocation>>();

        public static bool TryGetRuntimeKeyBySceneName(string sceneName, out object runtimeKey)
        {
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                foreach (object key in locator.Keys)
                {
                    if (!locator.Locate(key, typeof(SceneInstance), out IList<IResourceLocation> list))
                        continue;
                    foreach (IResourceLocation location in list)
                    {
                        string path = Path.GetFileNameWithoutExtension(location.InternalId);
                        if (string.Equals(path, sceneName, System.StringComparison.OrdinalIgnoreCase))
                        {
                            runtimeKey = key;
                            return true;
                        }
                    }
                }
            }
            runtimeKey = null;
            return false;
        }

        public static AsyncOperationHandle<SceneInstance> LoadAddressableScene(AssetReferenceScene addressableScene, LoadSceneMode loadSceneMode = LoadSceneMode.Single)
        {
            AsyncOperationHandle<SceneInstance> addressableAsyncOp = addressableScene.LoadSceneAsync(loadSceneMode, true);
            s_addressableSceneHandles.Add(addressableAsyncOp);
            return addressableAsyncOp;
        }

        public static void AddAddressableSceneHandle(AsyncOperationHandle<SceneInstance> addressableSceneHandle)
        {
            s_addressableSceneHandles.Add(addressableSceneHandle);
        }

        public static async UniTask UnloadAddressableScenes()
        {
            if (s_addressableSceneHandles.Count == 0)
                return;
            for (int i = 0; i < s_addressableSceneHandles.Count; ++i)
            {
                if (!s_addressableSceneHandles[i].IsValid())
                    continue;
                while (!s_addressableSceneHandles[i].IsDone)
                    await UniTask.Yield();
                if (s_addressableSceneHandles[i].Status != AsyncOperationStatus.Succeeded)
                {
                    Addressables.Release(s_addressableSceneHandles[i]);
                    continue;
                }
                AsyncOperationHandle<SceneInstance> addressableAsyncOp = Addressables.UnloadSceneAsync(s_addressableSceneHandles[i], UnloadSceneOptions.UnloadAllEmbeddedSceneObjects, true);
                while (!addressableAsyncOp.IsDone)
                {
                    await UniTask.Yield();
                }
            }
            s_addressableSceneHandles.Clear();
        }

        public static string GetScenePathByRuntimeKey(object runtimeKey)
        {
            IResourceLocation resourceLocation = GetFirstResourceLocationByRuntimeKey(runtimeKey);
            if (resourceLocation == null)
                return string.Empty;
            return resourceLocation.InternalId;
        }

        public static string GetScenePath(this AssetReferenceScene scene)
        {
            return GetScenePathByRuntimeKey(scene.RuntimeKey);
        }

        public static string GetSceneName(this AssetReferenceScene scene)
        {
            return Path.GetFileNameWithoutExtension(scene.GetScenePath());
        }

        public static IList<IResourceLocation> GetResourceLocation(this AssetReference asset)
        {
            return GetResourceLocationByRuntimeKey(asset.RuntimeKey);
        }

        public static IList<IResourceLocation> GetResourceLocationByRuntimeKey(object runtimeKey)
        {
            if (s_resourceLocations.TryGetValue(runtimeKey, out IList<IResourceLocation> cachedLocations))
                return cachedLocations;
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (!locator.Locate(runtimeKey, typeof(SceneInstance), out IList<IResourceLocation> list))
                    continue;
                s_resourceLocations[runtimeKey] = list;
                return list;
            }
            return null;
        }

        public static void ClearResourceLocationCache()
        {
            s_resourceLocations.Clear();
        }

        public static IResourceLocation GetFirstResourceLocation(this AssetReference asset)
        {
            return GetFirstResourceLocationByRuntimeKey(asset.RuntimeKey);
        }

        public static IResourceLocation GetFirstResourceLocationByRuntimeKey(object runtimeKey)
        {
            var list = GetResourceLocationByRuntimeKey(runtimeKey);
            if (list != null && list.Count > 0)
                return list[0];
            return null;
        }

        public static async UniTask<TType> GetOrLoadObjectAsync<TType>(this AssetReference assetRef)
            where TType : Object
        {
            if (assetRef == null)
                return null;
            object runtimeKey = assetRef.RuntimeKey;
            AsyncOperationHandle loadedHandle;
            if (s_loadedAssets.TryGetValue(runtimeKey, out loadedHandle))
            {
                if (loadedHandle.IsValid())
                    return loadedHandle.Result as TType;
                s_loadedAssets.Remove(runtimeKey);
            }

            // Check if the asset is actually marked as Addressable
            if (!assetRef.IsDataValid())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"Asset is not marked as Addressable: {runtimeKey}. Ignoring load.");
#endif
                return null;
            }

            if (s_loadingAssets.TryGetValue(runtimeKey, out PendingLoad existingLoad))
            {
                while (s_loadingAssets.TryGetValue(runtimeKey, out PendingLoad currentLoad) &&
                    ReferenceEquals(currentLoad, existingLoad))
                    await UniTask.Yield();
                if (existingLoad.ReleaseRequested)
                    return null;
            }
            if (s_loadedAssets.TryGetValue(runtimeKey, out loadedHandle))
            {
                if (loadedHandle.IsValid())
                    return loadedHandle.Result as TType;
                s_loadedAssets.Remove(runtimeKey);
            }
            PendingLoad pendingLoad = new PendingLoad();
            s_loadingAssets[runtimeKey] = pendingLoad;

            AsyncOperationHandle<TType> handler = default;
            try
            {
                handler = Addressables.LoadAssetAsync<TType>(runtimeKey);
                pendingLoad.Handle = handler;
                TType result = await handler.ToUniTask();
                if (pendingLoad.ReleaseRequested)
                {
                    Addressables.Release(handler);
                    handler = default;
                    return null;
                }
                if (s_loadedAssets.TryGetValue(runtimeKey, out loadedHandle) && loadedHandle.IsValid())
                {
                    Addressables.Release(handler);
                    handler = default;
                    return loadedHandle.Result as TType;
                }
                s_loadedAssets[runtimeKey] = handler;
                return result;
            }
            catch (System.Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"Failed to load addressable asset asynchronously: {runtimeKey}, {ex.Message}\n{ex.StackTrace}");
#endif
                if (handler.IsValid())
                    Addressables.Release(handler);
                return null;
            }
            finally
            {
                if (s_loadingAssets.TryGetValue(runtimeKey, out PendingLoad currentLoad) &&
                    ReferenceEquals(currentLoad, pendingLoad))
                    s_loadingAssets.Remove(runtimeKey);
            }
        }

        public static TType GetOrLoadObject<TType>(this AssetReference assetRef)
            where TType : Object
        {
            if (assetRef == null)
                return null;
            object runtimeKey = assetRef.RuntimeKey;
            if (s_loadedAssets.TryGetValue(runtimeKey, out AsyncOperationHandle loadedHandle))
            {
                if (loadedHandle.IsValid())
                    return loadedHandle.Result as TType;
                s_loadedAssets.Remove(runtimeKey);
            }

            // Check if the asset is actually marked as Addressable
            if (!assetRef.IsDataValid())
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"Asset is not marked as Addressable: {runtimeKey}. Ignoring load.");
#endif
                return null;
            }
            AsyncOperationHandle<TType> handler = default;
            try
            {
                // A synchronous caller may arrive while an async caller owns this load.
                // Join that operation rather than starting a second prefab/texture load.
                if (s_loadingAssets.TryGetValue(runtimeKey, out PendingLoad pendingLoad) &&
                    !pendingLoad.ReleaseRequested && pendingLoad.Handle.IsValid())
                {
                    AsyncOperationHandle sharedHandle = pendingLoad.Handle;
                    Object sharedResult = sharedHandle.WaitForCompletion() as Object;
                    if (pendingLoad.ReleaseRequested)
                        return null;
                    if (s_loadedAssets.TryGetValue(runtimeKey, out loadedHandle) && loadedHandle.IsValid())
                        return loadedHandle.Result as TType;
                    if (!sharedHandle.IsValid() || sharedHandle.Status != AsyncOperationStatus.Succeeded)
                        return null;
                    // The async owner will release its reference after seeing this cache entry.
                    Addressables.ResourceManager.Acquire(sharedHandle);
                    s_loadedAssets[runtimeKey] = sharedHandle;
                    return sharedResult as TType;
                }

                handler = Addressables.LoadAssetAsync<TType>(runtimeKey);
                TType result = handler.WaitForCompletion();
                if (handler.Status != AsyncOperationStatus.Succeeded)
                    throw handler.OperationException ?? new System.Exception($"Failed to load addressable asset: {runtimeKey}");
                // Completion callbacks can populate the cache while WaitForCompletion runs.
                if (s_loadedAssets.TryGetValue(runtimeKey, out loadedHandle) && loadedHandle.IsValid())
                {
                    Addressables.Release(handler);
                    handler = default;
                    return loadedHandle.Result as TType;
                }
                s_loadedAssets[runtimeKey] = handler;
                return result;
            }
            catch (System.Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogError($"Failed to load addressable asset: {runtimeKey}, {ex.Message}\n{ex.StackTrace}");
#endif
                if (handler.IsValid())
                    Addressables.Release(handler);
                return null;
            }
        }

        public static async UniTask<TType> GetOrLoadAssetAsync<TType>(this AssetReference assetRef)
            where TType : Component
        {
            GameObject loadedObject = await assetRef.GetOrLoadAssetAsync();
            if (loadedObject != null)
                return loadedObject.GetComponent<TType>();
            return null;
        }

        public static TType GetOrLoadAsset<TType>(this AssetReference assetRef)
            where TType : Component
        {
            GameObject loadedObject = assetRef.GetOrLoadAsset();
            if (loadedObject != null)
                return loadedObject.GetComponent<TType>();
            return null;
        }

        public static async UniTask<GameObject> GetOrLoadAssetAsync(this AssetReference assetRef)
        {
            return await assetRef.GetOrLoadObjectAsync<GameObject>();
        }

        public static GameObject GetOrLoadAsset(this AssetReference assetRef)
        {
            return assetRef.GetOrLoadObject<GameObject>();
        }

        public static async UniTask<TType> GetOrLoadAssetAsyncOrUsePrefab<TType>(this AssetReference assetRef, TType prefab)
            where TType : Component
        {
            TType tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = await assetRef.GetOrLoadAssetAsync<TType>();
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }

        public static TType GetOrLoadAssetOrUsePrefab<TType>(this AssetReference assetRef, TType prefab)
            where TType : Component
        {
            TType tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = assetRef.GetOrLoadAsset<TType>();
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }

        public static async UniTask<TType> GetOrLoadObjectAsyncOrUseAsset<TType>(this AssetReference assetRef, TType asset)
            where TType : Object
        {
            TType tempAsset = null;
            if (assetRef.IsDataValid())
                tempAsset = await assetRef.GetOrLoadObjectAsync<TType>();
            if (tempAsset == null)
                tempAsset = asset;
            return tempAsset;
        }

        public static TType GetOrLoadObjectOrUseAsset<TType>(this AssetReference assetRef, TType asset)
            where TType : Object
        {
            TType tempAsset = null;
            if (assetRef.IsDataValid())
                tempAsset = assetRef.GetOrLoadObject<TType>();
            if (tempAsset == null)
                tempAsset = asset;
            return tempAsset;
        }

        public static async UniTask<GameObject> GetOrLoadAssetAsyncOrUsePrefab(this AssetReference assetRef, GameObject prefab)
        {
            GameObject tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = await assetRef.GetOrLoadAssetAsync();
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }

        public static GameObject GetOrLoadAssetOrUsePrefab(this AssetReference assetRef, GameObject prefab)
        {
            GameObject tempPrefab = null;
            if (assetRef.IsDataValid())
                tempPrefab = assetRef.GetOrLoadAsset();
            if (tempPrefab == null)
                tempPrefab = prefab;
            return tempPrefab;
        }

        public static UniTask<TType[]> GetOrLoadObjectsAsync<TType>(this IEnumerable<AssetReference> assetRefs)
            where TType : Object
        {
            return assetRefs.GetOrLoadObjectsAsync<TType>(4);
        }

        public static UniTask<TType[]> GetOrLoadObjectsAsync<TType>(this IEnumerable<AssetReference> assetRefs, int maxConcurrentLoads)
            where TType : Object
        {
            return LoadInBatches(assetRefs, asset => asset.GetOrLoadObjectAsync<TType>(), maxConcurrentLoads);
        }

        public static TType[] GetOrLoadObjects<TType>(this IEnumerable<AssetReference> assetRefs)
            where TType : Object
        {
            List<TType> results = new List<TType>();
            foreach (AssetReference assetRef in assetRefs)
            {
                results.Add(assetRef.GetOrLoadObject<TType>());
            }
            return results.ToArray();
        }

        public static UniTask<TType[]> GetOrLoadAssetsAsync<TType>(this IEnumerable<AssetReference> assetRefs)
            where TType : Component
        {
            return assetRefs.GetOrLoadAssetsAsync<TType>(4);
        }

        public static UniTask<TType[]> GetOrLoadAssetsAsync<TType>(this IEnumerable<AssetReference> assetRefs, int maxConcurrentLoads)
            where TType : Component
        {
            return LoadInBatches(assetRefs, asset => asset.GetOrLoadAssetAsync<TType>(), maxConcurrentLoads);
        }

        public static TType[] GetOrLoadAssets<TType>(this IEnumerable<AssetReference> assetRefs)
            where TType : Component
        {
            List<TType> results = new List<TType>();
            foreach (AssetReference assetRef in assetRefs)
            {
                results.Add(assetRef.GetOrLoadAsset<TType>());
            }
            return results.ToArray();
        }

        public static UniTask<GameObject[]> GetOrLoadAssetsAsync(this IEnumerable<AssetReference> assetRefs)
        {
            return assetRefs.GetOrLoadAssetsAsync(4);
        }

        public static UniTask<GameObject[]> GetOrLoadAssetsAsync(this IEnumerable<AssetReference> assetRefs, int maxConcurrentLoads)
        {
            return LoadInBatches(assetRefs, asset => asset.GetOrLoadAssetAsync(), maxConcurrentLoads);
        }

        public static GameObject[] GetOrLoadAssets(this IEnumerable<AssetReference> assetRefs)
        {
            List<GameObject> results = new List<GameObject>();
            foreach (AssetReference assetRef in assetRefs)
            {
                results.Add(assetRef.GetOrLoadAsset());
            }
            return results.ToArray();
        }

        public static async UniTask InstantiateObjectsOrUsePrefabs(this AssetReference[] addressablePrefabs, GameObject[] prefabs, Transform transform, List<GameObject> instantiatedObjects = null)
        {
            if ((prefabs == null || prefabs.Length <= 0) && addressablePrefabs != null && addressablePrefabs.Length > 0)
            {
                foreach (AssetReference addressablePrefab in addressablePrefabs)
                {
                    if (!addressablePrefab.IsDataValid())
                        continue;
                    GameObject result = await InstantiateAndOwn(
                        () => Addressables.InstantiateAsync(addressablePrefab.RuntimeKey, transform.position, transform.rotation, transform, true), transform);
                    if (result != null && instantiatedObjects != null)
                        instantiatedObjects.Add(result);
                }
                return;
            }
            if (prefabs != null && prefabs.Length > 0)
            {
                foreach (GameObject prefab in prefabs)
                {
                    if (prefab == null) continue;
                    GameObject instantiatedObject = Object.Instantiate(prefab, transform.position, transform.rotation, transform);
                    if (instantiatedObjects != null && instantiatedObject != null)
                        instantiatedObjects.Add(instantiatedObject);
                }
            }
        }

        public static async UniTask InstantiateObjectsOrUsePrefabs(this AssetReference[] addressablePrefabs, GameObject[] prefabs, Vector3 position, Quaternion rotation, Transform transform = null, List<GameObject> instantiatedObjects = null)
        {
            if ((prefabs == null || prefabs.Length <= 0) && addressablePrefabs != null && addressablePrefabs.Length > 0)
            {
                foreach (AssetReference addressablePrefab in addressablePrefabs)
                {
                    if (!addressablePrefab.IsDataValid())
                        continue;
                    GameObject result = await InstantiateAndOwn(
                        () => Addressables.InstantiateAsync(addressablePrefab.RuntimeKey, position, rotation, transform, true), transform);
                    if (result != null && instantiatedObjects != null)
                        instantiatedObjects.Add(result);
                }
                return;
            }
            if (prefabs != null && prefabs.Length > 0)
            {
                foreach (GameObject prefab in prefabs)
                {
                    if (prefab == null) continue;
                    GameObject instantiatedObject;
                    if (transform == null)
                        instantiatedObject = Object.Instantiate(prefab, position, rotation);
                    else
                        instantiatedObject = Object.Instantiate(prefab, position, rotation, transform);
                    if (instantiatedObjects != null && instantiatedObject != null)
                        instantiatedObjects.Add(instantiatedObject);
                }
            }
        }

        public static async UniTask<GameObject> InstantiateObjectOrUsePrefab(this AssetReference addressablePrefab, GameObject prefab, Transform transform)
        {
            if (prefab == null && addressablePrefab.IsDataValid())
            {
                return await InstantiateAndOwn(
                    () => Addressables.InstantiateAsync(addressablePrefab.RuntimeKey, transform.position, transform.rotation, transform, true), transform);
            }
            if (prefab != null)
            {
                return Object.Instantiate(prefab, transform.position, transform.rotation, transform);
            }
            return null;
        }

        public static async UniTask<T> InstantiateObjectOrUsePrefab<T>(this AssetReference addressablePrefab, GameObject prefab, Transform transform)
            where T : Component
        {
            GameObject instantiatedObject = await addressablePrefab.InstantiateObjectOrUsePrefab(prefab, transform);
            return GetComponentOrRelease<T>(instantiatedObject);
        }

        public static async UniTask<GameObject> InstantiateObjectOrUsePrefab(this AssetReference addressablePrefab, GameObject prefab, Vector3 position, Quaternion rotation, Transform transform = null)
        {
            if (prefab == null && addressablePrefab.IsDataValid())
            {
                return await InstantiateAndOwn(
                    () => Addressables.InstantiateAsync(addressablePrefab.RuntimeKey, position, rotation, transform, true), transform);
            }
            if (prefab != null)
            {
                if (transform == null)
                    return Object.Instantiate(prefab, position, rotation);
                else
                    return Object.Instantiate(prefab, position, rotation, transform);
            }
            return null;
        }

        public static async UniTask<T> InstantiateObjectOrUsePrefab<T>(this AssetReference addressablePrefab, GameObject prefab, Vector3 position, Quaternion rotation, Transform transform = null)
            where T : Component
        {
            GameObject instantiatedObject = await addressablePrefab.InstantiateObjectOrUsePrefab(prefab, position, rotation, transform);
            return GetComponentOrRelease<T>(instantiatedObject);
        }

        public static async UniTask<GameObject> InstantiateObject(this AssetReference addressablePrefab, Transform transform)
        {
            if (addressablePrefab.IsDataValid())
            {
                return await InstantiateAndOwn(
                    () => Addressables.InstantiateAsync(addressablePrefab.RuntimeKey, transform.position, transform.rotation, transform, true), transform);
            }
            return null;
        }

        public static async UniTask<T> InstantiateObject<T>(this AssetReference addressablePrefab, Transform transform)
            where T : Component
        {
            GameObject instantiatedObject = await addressablePrefab.InstantiateObject(transform);
            return GetComponentOrRelease<T>(instantiatedObject);
        }

        public static async UniTask<GameObject> InstantiateObject(this AssetReference addressablePrefab, Vector3 position, Quaternion rotation, Transform transform = null)
        {
            if (addressablePrefab.IsDataValid())
            {
                return await InstantiateAndOwn(
                    () => Addressables.InstantiateAsync(addressablePrefab.RuntimeKey, position, rotation, transform, true), transform);
            }
            return null;
        }

        public static async UniTask<T> InstantiateObject<T>(this AssetReference addressablePrefab, Vector3 position, Quaternion rotation, Transform transform = null)
            where T : Component
        {
            GameObject instantiatedObject = await addressablePrefab.InstantiateObject(position, rotation, transform);
            return GetComponentOrRelease<T>(instantiatedObject);
        }

        public static void Release(object runtimeKey)
        {
            if (s_loadingAssets.TryGetValue(runtimeKey, out PendingLoad pendingLoad))
                pendingLoad.ReleaseRequested = true;
            if (s_loadedAssets.TryGetValue(runtimeKey, out AsyncOperationHandle handle))
            {
                // A release callback may immediately request this key again.
                s_loadedAssets.Remove(runtimeKey);
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                "Released addressable asset: {0}", runtimeKey);
#endif
        }

        public static void ReleaseAll()
        {
            foreach (PendingLoad pendingLoad in s_loadingAssets.Values)
                pendingLoad.ReleaseRequested = true;
            var handles = new List<AsyncOperationHandle>(s_loadedAssets.Values);
            // Detach this generation before callbacks can start fresh loads.
            s_loadedAssets.Clear();
            s_loadedAssets.TrimExcess();
            foreach (AsyncOperationHandle handle in handles)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            ClearResourceLocationCache();
            s_resourceLocations.TrimExcess();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null,
                "Released {0} cached addressable assets.", handles.Count);
#endif
        }
    }
}
#endif
