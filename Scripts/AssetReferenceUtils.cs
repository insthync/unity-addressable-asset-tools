using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace Insthync.AddressableAssetTools
{
    public static class AssetReferenceUtils
    {
        public static bool IsDataValid(this AssetReference asset)
        {
            return asset != null && asset.RuntimeKeyIsValid();
        }

        public static AsyncOperationHandle<T> CreateGetComponentCompletedOperation<T>(AsyncOperationHandle<GameObject> handler)
        {
            return Addressables.ResourceManager.CreateCompletedOperation(handler.Result.GetComponent<T>(), string.Empty);
        }

        public static UniTask<IList<IResourceLocation>> GetResourceLocation(this AssetReference asset)
        {
            return GetResourceLocationByRuntimeKey(asset.RuntimeKey);
        }

        public static async UniTask<IList<IResourceLocation>> GetResourceLocationByRuntimeKey(object runtimeKey)
        {
            var handler = Addressables.LoadResourceLocationsAsync(runtimeKey);
            var result = await handler.ToUniTask();
            handler.Release();
            return result;
        }

        public static UniTask<IResourceLocation> GetFirstResourceLocation(this AssetReference asset)
        {
            return GetFirstResourceLocationByRuntimeKey(asset.RuntimeKey);
        }

        public static async UniTask<IResourceLocation> GetFirstResourceLocationByRuntimeKey(object runtimeKey)
        {
            var list = await GetResourceLocationByRuntimeKey(runtimeKey);
            if (list != null && list.Count > 0)
                return list[0];
            return null;
        }
    }
}