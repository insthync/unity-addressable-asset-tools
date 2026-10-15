#if !DISABLE_ADDRESSABLES
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Insthync.AddressableAssetTools
{
    public class AssetReferenceReleaser : MonoBehaviour
    {
        private AsyncOperationHandle<GameObject> _instanceHandle;
        private bool _ownsHandle;

        private sealed class InactiveInstanceMonitor : IPlayerLoopItem
        {
            private readonly AssetReferenceReleaser _owner;

            public InactiveInstanceMonitor(AssetReferenceReleaser owner)
            {
                _owner = owner;
            }

            public bool MoveNext()
            {
                if (_owner == null)
                {
                    _owner.ReleaseOwnedHandle();
                    return false;
                }
                // Once active, Unity will deliver OnDestroy normally.
                return !_owner.gameObject.activeInHierarchy;
            }
        }

        internal void Initialize(AsyncOperationHandle<GameObject> handle)
        {
            if (_ownsHandle)
            {
                if (_instanceHandle.Equals(handle))
                    return;
                throw new System.InvalidOperationException("An instance already owns another Addressables handle.");
            }
            _instanceHandle = handle;
            _ownsHandle = true;
            // Unity does not send OnDestroy to objects that have never been active.
            if (!gameObject.activeInHierarchy)
                PlayerLoopHelper.AddAction(PlayerLoopTiming.Update, new InactiveInstanceMonitor(this));
        }

        private void ReleaseOwnedHandle()
        {
            AsyncOperationHandle<GameObject> handle = _instanceHandle;
            _instanceHandle = default;
            if (handle.IsValid())
                Addressables.Release(handle);
        }

        private void OnDestroy()
        {
            if (_ownsHandle)
                ReleaseOwnedHandle();
            else
                Addressables.ReleaseInstance(gameObject);
        }
    }
}
#endif
