#if !DISABLE_ADDRESSABLES
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Insthync.AddressableAssetTools
{
    public class InstantiateByAssetReference : MonoBehaviour
    {
        public AssetReference assetReference;
        public bool instantiateOnStart = false;
        [Tooltip("If `destroyDelay` is greater than 0, the instantiated object will be destroyed after the delay.")]
        public float destroyDelay = 0f;
        public Transform parent;

        private void Start()
        {
            if (instantiateOnStart)
            {
                if (parent == null)
                    parent = transform;
                InstantiateAsync(parent).Forget();
            }
        }

        public virtual async UniTask<GameObject> InstantiateAsync(Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (!assetReference.IsDataValid())
            {
                Debug.LogWarning("AssetReference is not valid.");
                return null;
            }
            GameObject instance = await AddressableAssetsManager.InstantiateAndOwn(
                () => Addressables.InstantiateAsync(assetReference.RuntimeKey, position, rotation, parent, true), parent);
            ScheduleDestroy(instance);
            return instance;
        }

        public virtual async UniTask<GameObject> InstantiateAsync(Transform parent = null, bool instantiateInWorldSpace = false)
        {
            if (!assetReference.IsDataValid())
            {
                Debug.LogWarning("AssetReference is not valid.");
                return null;
            }
            GameObject instance = await AddressableAssetsManager.InstantiateAndOwn(
                () => Addressables.InstantiateAsync(assetReference.RuntimeKey, parent, instantiateInWorldSpace, true), parent);
            ScheduleDestroy(instance);
            return instance;
        }

        private void ScheduleDestroy(GameObject instance)
        {
            if (instance != null && destroyDelay > 0f)
                Destroy(instance, destroyDelay);
        }
    }
}
#endif