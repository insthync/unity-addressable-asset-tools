#if !DISABLE_ADDRESSABLES
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Experimental.Rendering;
#endif

namespace Insthync.AddressableAssetTools
{
    [CreateAssetMenu(fileName = "Addressable Asset Download Manager Settings", menuName = "Addressables/Addressable Asset Download Manager Settings")]
    public class AddressableAssetDownloadManagerSettings : ScriptableObject
    {
        [SerializeField]
        protected List<AssetReference> initialObjects = new List<AssetReference>();
        public virtual List<AssetReference> InitialObjects => initialObjects;

        [SerializeField]
        protected List<AssetReferenceShaderVariantCollection> shaderVariantCollections = new List<AssetReferenceShaderVariantCollection>();
        public virtual List<AssetReferenceShaderVariantCollection> ShaderVariantCollections => shaderVariantCollections;

#if UNITY_6000_0_OR_NEWER
        // GraphicsStateCollection exists in Unity 6 and newer only.
        [SerializeField]
        [Tooltip("Recorded graphics state collections, one per platform/graphics API. Only the one matching the running device is warmed up; it stays loaded so its shaders and pipelines are not unloaded before use.")]
        protected List<AssetReferenceT<GraphicsStateCollection>> graphicsStateCollections = new List<AssetReferenceT<GraphicsStateCollection>>();
        public virtual List<AssetReferenceT<GraphicsStateCollection>> GraphicsStateCollections => graphicsStateCollections;
#endif
    }
}
#endif