#if !DISABLE_ADDRESSABLES
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Experimental.Rendering;

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

        [SerializeField]
        [Tooltip("Recorded graphics state collections, one per platform/graphics API. Only the one matching the running device is warmed up; it stays loaded so its shaders and pipelines are not unloaded before use.")]
        protected List<AssetReferenceT<GraphicsStateCollection>> graphicsStateCollections = new List<AssetReferenceT<GraphicsStateCollection>>();
        public virtual List<AssetReferenceT<GraphicsStateCollection>> GraphicsStateCollections => graphicsStateCollections;
    }
}
#endif