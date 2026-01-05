using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Insthync.AddressableAssetTools
{
    [System.Serializable]
    public class AssetReferenceScene : AssetReference
    {
        public string SceneName
        {
            get { return GetSceneName(); }
        }

        public string GetScenePath()
        {
            IResourceLocation resourceLocation = this.GetFirstResourceLocation();
            if (resourceLocation == null)
                return string.Empty;
            return resourceLocation.InternalId;
        }

        public string GetSceneName()
        {
            return Path.GetFileNameWithoutExtension(GetScenePath());
        }

        public async UniTask<string> GetScenePathAsync()
        {
            IResourceLocation resourceLocation = await this.GetFirstResourceLocationAsync();
            if (resourceLocation == null)
                return string.Empty;
            return resourceLocation.InternalId;
        }

        public async UniTask<string> GetSceneNameAsync()
        {
            return Path.GetFileNameWithoutExtension(await GetScenePathAsync());
        }

#if UNITY_EDITOR
        public AssetReferenceScene(SceneAsset scene) : base(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(scene)))
        {

        }

        public override bool ValidateAsset(string path)
        {
            return ValidateAsset(AssetDatabase.LoadAssetAtPath<SceneAsset>(path));
        }

        public override bool ValidateAsset(Object obj)
        {
            return (obj != null) && (obj is SceneAsset);
        }

        public override bool SetEditorAsset(Object value)
        {
            if (!base.SetEditorAsset(value))
            {
                return false;
            }
            if (value is SceneAsset scene)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
#endif
    }
}