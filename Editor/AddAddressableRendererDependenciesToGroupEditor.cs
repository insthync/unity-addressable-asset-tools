using UnityEditor;
using UnityEngine;

namespace Insthync.AddressableAssetTools
{
    public class AddAddressableRendererDependenciesToGroupEditor : BaseAddAddressableToGroupEditor
    {
        [MenuItem("Tools/Addressables/Add Renderer Dependencies to Group By Assets")]
        public static void ShowWindow()
        {
            GetWindow<AddAddressableRendererDependenciesToGroupEditor>("Add Renderer Dependencies to Group By Assets");
        }

        protected override bool IsTargetAsset(string dependencyPath)
        {
            Object obj = AssetDatabase.LoadAssetAtPath<Object>(dependencyPath);

            bool isRendererDependencies = obj is Mesh || obj is Material || obj is Sprite || obj is Texture || obj is Shader ||
                obj is AnimatorOverrideController || obj is AnimationClip ||
                (obj is GameObject && dependencyPath.ToLower().EndsWith("fbx")) ||
                (obj is GameObject && dependencyPath.ToLower().EndsWith("obj"));
            return isRendererDependencies;
        }
    }
}
