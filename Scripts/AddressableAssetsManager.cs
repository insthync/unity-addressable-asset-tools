using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Insthync.AddressableAssetTools
{
    public static class AddressableAssetsManager
    {
        private static Dictionary<object, GameObject> s_loadedAssets = new Dictionary<object, GameObject>();
        private static Dictionary<object, AsyncOperationHandle> s_assetRefs = new Dictionary<object, AsyncOperationHandle>();

        public static async Task<TType> GetOrLoadAssetAsync<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            if (s_loadedAssets.TryGetValue(assetRef.RuntimeKey, out GameObject result))
                return result.GetComponent<TType>();
            AsyncOperationHandle<GameObject> handler = Addressables.LoadAssetAsync<GameObject>(assetRef.RuntimeKey);
            handlerCallback?.Invoke(handler);
            GameObject handlerResult = await handler.Task;
            s_loadedAssets[assetRef.RuntimeKey] = handlerResult;
            s_assetRefs[assetRef.RuntimeKey] = handler;
            return handlerResult.GetComponent<TType>();
        }

        public static TType GetOrLoadAsset<TType>(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
            where TType : Component
        {
            if (s_loadedAssets.TryGetValue(assetRef.RuntimeKey, out GameObject result))
                return result.GetComponent<TType>();
            AsyncOperationHandle<GameObject> handler = Addressables.LoadAssetAsync<GameObject>(assetRef.RuntimeKey);
            handlerCallback?.Invoke(handler);
            GameObject handlerResult = handler.WaitForCompletion();
            s_loadedAssets[assetRef.RuntimeKey] = handlerResult;
            s_assetRefs[assetRef.RuntimeKey] = handler;
            return handlerResult.GetComponent<TType>();
        }

        public static async Task<GameObject> GetOrLoadAssetAsync(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            if (s_loadedAssets.TryGetValue(assetRef.RuntimeKey, out GameObject result))
                return result;
            AsyncOperationHandle<GameObject> handler = Addressables.LoadAssetAsync<GameObject>(assetRef.RuntimeKey);
            handlerCallback?.Invoke(handler);
            GameObject handlerResult = await handler.Task;
            s_loadedAssets[assetRef.RuntimeKey] = handlerResult;
            s_assetRefs[assetRef.RuntimeKey] = handler;
            return handlerResult;
        }

        public static GameObject GetOrLoadAsset(this AssetReference assetRef, System.Action<AsyncOperationHandle> handlerCallback = null)
        {
            if (s_loadedAssets.TryGetValue(assetRef.RuntimeKey, out GameObject result))
                return result;
            AsyncOperationHandle<GameObject> handler = Addressables.LoadAssetAsync<GameObject>(assetRef.RuntimeKey);
            handlerCallback?.Invoke(handler);
            GameObject handlerResult = handler.WaitForCompletion();
            s_loadedAssets[assetRef.RuntimeKey] = handlerResult;
            s_assetRefs[assetRef.RuntimeKey] = handler;
            return handlerResult;
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