using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets.Initialization;

namespace Insthync.AddressableAssetTools
{
    public partial class AddressableAssetDownloadManager : MonoBehaviour
    {
        public string remoteConfigUrl;
        public AssetReferenceDownloadManagerSettings settingsAssetReference;
        [Header("Events")]
        public UnityEvent onStart = new UnityEvent();
        public UnityEvent onEnd = new UnityEvent();
        public UnityEvent onFileSizeRetrieving = new UnityEvent();
        public AddressableAssetFileSizeEvent onFileSizeRetrieved = new AddressableAssetFileSizeEvent();
        public AddressableAssetTotalProgressEvent onDepsDownloading = new AddressableAssetTotalProgressEvent();
        public AddressableAssetTotalProgressEvent onDepsDownloaded = new AddressableAssetTotalProgressEvent();
        public AddressableAssetDownloadProgressEvent onDepsFileDownloading = new AddressableAssetDownloadProgressEvent();
        public System.Action<System.Exception> onDepsDownloadError;
        public UnityEvent onDownloadedAll = new UnityEvent();

        public long FileSize { get; protected set; } = 0;
        public int LoadedCount { get; protected set; } = 0;
        public int TotalCount { get; protected set; } = 0;


        public AddressableRemoteConfig _remoteConfig;

        private async void Start()
        {
            await UniTask.Yield();
            onStart?.Invoke();

            if (!string.IsNullOrWhiteSpace(remoteConfigUrl))
            {
                string url;
                if (!remoteConfigUrl.Contains("?"))
                    url = $"{remoteConfigUrl}?";
                else
                    url = $"{remoteConfigUrl}&";

                url += $"time={System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond}";
                url += $"&platform={Application.platform}";
                url += $"&version={Application.version}";
                url += $"&unity_version={Application.unityVersion}";

                using (UnityWebRequest webRequest = UnityWebRequest.Get(url))
                {
                    webRequest.SetRequestHeader("User-Agent", $"{Application.identifier}/{Application.version} (Unity {Application.unityVersion}; {Application.platform})");

                    UnityWebRequestAsyncOperation ayncOp = webRequest.SendWebRequest();
                    do
                    {
                        await Task.Yield();
                    } while (!ayncOp.isDone);
                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string dataAsJson = webRequest.downloadHandler.text;
                        Debug.Log($"Found addressable remote config. {dataAsJson}");
                        _remoteConfig = JsonConvert.DeserializeObject<AddressableRemoteConfig>(dataAsJson);
                    }
                    else
                    {
                        Debug.LogError($"Not found addressable remote config. ({url}) ({webRequest.error})");
                    }
                }
            }

            if (_remoteConfig != null && _remoteConfig.replaceRuntimeProperties != null)
            {
                foreach (KeyValuePair<string, string> kv in _remoteConfig.replaceRuntimeProperties)
                {
                    AddressablesRuntimeProperties.SetPropertyValue(kv.Key, kv.Value);
                }
            }

            Debug.Log("Initializing addressable.");
            AsyncOperationHandle<IResourceLocator> initialResourceLocatorHandle = Addressables.InitializeAsync(false);
            IResourceLocator initialResourceLocator = await initialResourceLocatorHandle.Task;
            Addressables.Release(initialResourceLocatorHandle);

            if (_remoteConfig != null && _remoteConfig.catalogUrls != null)
            {
                Debug.Log($"Reading remote config and catalogs.");
                foreach (string catalogUrl in _remoteConfig.catalogUrls)
                {
                    string url;
                    if (!catalogUrl.Contains("?"))
                        url = $"{catalogUrl}?time={System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond}";
                    else
                        url = $"{catalogUrl}&time={System.DateTime.Now.Ticks / System.TimeSpan.TicksPerMillisecond}";
                    AsyncOperationHandle<IResourceLocator> handle = Addressables.LoadContentCatalogAsync(url, false);
                    IResourceLocator resourceLocator = await handle.Task;
                    Addressables.Release(handle);
                }
            }

            Debug.Log("Checking for catalog updates.");
            AsyncOperationHandle<List<string>> checkForUpdateHandle = Addressables.CheckForCatalogUpdates(false);
            await checkForUpdateHandle.Task;
            List<string> catalogsToUpdate = null;
            if (checkForUpdateHandle.Status == AsyncOperationStatus.Succeeded)
                catalogsToUpdate = new List<string>(checkForUpdateHandle.Result);
            Addressables.Release(checkForUpdateHandle);

            if (catalogsToUpdate != null && catalogsToUpdate.Count > 0)
            {
                AsyncOperationHandle<List<IResourceLocator>> updateHandle = Addressables.UpdateCatalogs(true, catalogsToUpdate, false);
                await updateHandle.Task;
                Addressables.Release(updateHandle);
            }

            HashSet<object> keys = new HashSet<object>();
            foreach (IResourceLocator resourceLocator in Addressables.ResourceLocators)
            {
                foreach (object key in resourceLocator.Keys)
                {
                    keys.Add(key);
                }
            }

            // Downloads
            Debug.Log("Start assets downloading...");
            TotalCount = 1;
            await DownloadMany(keys,
                OnFileSizeRetrieving,
                OnFileSizeRetrieved,
                OnDepsDownloading,
                OnDepsFileDownloading,
                OnDepsDownloaded,
                OnDepsDownloadError);
            LoadedCount++;

            await UniTask.Yield();
            onDownloadedAll?.Invoke();

            // Read settings to find which assets will be instantiated
            Debug.Log("Read addressable asset download manager settings.");
            AsyncOperationHandle<AddressableAssetDownloadManagerSettings> settingsAsyncOp = settingsAssetReference.LoadAssetAsync();
            await settingsAsyncOp.Task;
            AddressableAssetDownloadManagerSettings settings = settingsAsyncOp.Result;
            settingsAssetReference.ReleaseAsset();
            Addressables.Release(settingsAsyncOp);

            // Instantiates
            for (int i = 0; i < settings.InitialObjects.Count; ++i)
            {
                try
                {
                    Debug.Log($"Initializing {settings.InitialObjects[i].RuntimeKey}");
                    AsyncOperationHandle<GameObject> instantiateOp = Addressables.InstantiateAsync(settings.InitialObjects[i].RuntimeKey);
                    await instantiateOp.Task;
                    Debug.Log($"Initialized {instantiateOp.Result.name}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Unable to initialize {settings.InitialObjects[i].RuntimeKey}");
                    Debug.LogException(ex);
                }
            }

            onEnd?.Invoke();
        }

        private void OnDestroy()
        {
            onStart?.RemoveAllListeners();
            onStart = null;
            onEnd?.RemoveAllListeners();
            onEnd = null;
            onFileSizeRetrieving?.RemoveAllListeners();
            onFileSizeRetrieving = null;
            onFileSizeRetrieved?.RemoveAllListeners();
            onFileSizeRetrieved = null;
            onDepsDownloading?.RemoveAllListeners();
            onDepsDownloading = null;
            onDepsFileDownloading?.RemoveAllListeners();
            onDepsFileDownloading = null;
            onDepsDownloaded?.RemoveAllListeners();
            onDepsDownloaded = null;
            onDownloadedAll?.RemoveAllListeners();
            onDownloadedAll = null;
        }

        protected virtual void OnFileSizeRetrieving()
        {
            FileSize = 0;
            onFileSizeRetrieving?.Invoke();
        }

        protected virtual void OnFileSizeRetrieved(long fileSize)
        {
            FileSize = fileSize;
            onFileSizeRetrieved?.Invoke(fileSize);
        }

        protected virtual void OnDepsDownloading()
        {
            onDepsDownloading?.Invoke(LoadedCount, TotalCount);
        }

        protected virtual void OnDepsFileDownloading(long downloadSize, long fileSize, float percentComplete)
        {
            onDepsFileDownloading?.Invoke(downloadSize, fileSize, percentComplete);
        }

        protected virtual void OnDepsDownloaded()
        {
            onDepsDownloaded?.Invoke(LoadedCount, TotalCount);
        }

        protected virtual void OnDepsDownloadError(System.Exception ex)
        {
            onDepsDownloadError?.Invoke(ex);
        }

        public static async Task<SceneInstance> DownloadAndLoadScene(
            object runtimeKey,
            LoadSceneParameters loadSceneParameters,
            System.Action onFileSizeRetrieving,
            AddressableAssetFileSizeDelegate onFileSizeRetrieved,
            System.Action onDepsDownloading,
            AddressableAssetDownloadProgressDelegate onDepsFileDownloading,
            System.Action onDepsDownloaded,
            System.Action<System.Exception> onError)
        {
            await Download(runtimeKey, onFileSizeRetrieving, onFileSizeRetrieved, onDepsDownloading, onDepsFileDownloading, onDepsDownloaded, onError);
            AsyncOperationHandle<SceneInstance> loadSceneOp = Addressables.LoadSceneAsync(runtimeKey, loadSceneParameters);
            while (!loadSceneOp.IsDone)
            {
                await UniTask.Yield();
            }
            return loadSceneOp.Result;
        }

        public static async Task<GameObject> DownloadAndInstantiate(
            object runtimeKey,
            System.Action onFileSizeRetrieving,
            AddressableAssetFileSizeDelegate onFileSizeRetrieved,
            System.Action onDepsDownloading,
            AddressableAssetDownloadProgressDelegate onDepsFileDownloading,
            System.Action onDepsDownloaded,
            System.Action<System.Exception> onError)
        {
            await Download(runtimeKey, onFileSizeRetrieving, onFileSizeRetrieved, onDepsDownloading, onDepsFileDownloading, onDepsDownloaded, onError);
            AsyncOperationHandle<GameObject> instantiateOp = Addressables.InstantiateAsync(runtimeKey);
            while (!instantiateOp.IsDone)
            {
                await UniTask.Yield();
            }
            return instantiateOp.Result;
        }

        public static async Task Download(
            object runtimeKey,
            System.Action onFileSizeRetrieving,
            AddressableAssetFileSizeDelegate onFileSizeRetrieved,
            System.Action onDepsDownloading,
            AddressableAssetDownloadProgressDelegate onDepsFileDownloading,
            System.Action onDepsDownloaded,
            System.Action<System.Exception> onError)
        {
            // Get download size
            AsyncOperationHandle<long> getSizeOp;
            try
            {
                getSizeOp = Addressables.GetDownloadSizeAsync(runtimeKey);
                onFileSizeRetrieving?.Invoke();
                while (!getSizeOp.IsDone)
                {
                    await UniTask.Yield();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                onError?.Invoke(ex);
                return;
            }
            await UniTask.Yield();
            long fileSize = getSizeOp.Result;
            onFileSizeRetrieved.Invoke(fileSize);
            // Download dependencies
            if (fileSize > 0)
            {
                AsyncOperationHandle downloadOp;
                try
                {
                    downloadOp = Addressables.DownloadDependenciesAsync(runtimeKey);
                    await UniTask.Yield();
                    onDepsDownloading?.Invoke();
                    while (!downloadOp.IsDone)
                    {
                        await UniTask.Yield();
                        float percentageComplete = downloadOp.GetDownloadStatus().Percent;
                        onDepsFileDownloading?.Invoke((long)(percentageComplete * fileSize), fileSize, percentageComplete);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                    onError?.Invoke(ex);
                    return;
                }
                await UniTask.Yield();
                onDepsDownloaded?.Invoke();
                Addressables.ReleaseInstance(downloadOp);
            }
            else
            {
                onDepsDownloading?.Invoke();
                onDepsFileDownloading?.Invoke(0, 0, 1);
                onDepsDownloaded?.Invoke();
            }
        }

        public static async Task DownloadMany(
            IEnumerable runtimeKeys,
            System.Action onFileSizeRetrieving,
            AddressableAssetFileSizeDelegate onFileSizeRetrieved,
            System.Action onDepsDownloading,
            AddressableAssetDownloadProgressDelegate onDepsFileDownloading,
            System.Action onDepsDownloaded,
            System.Action<System.Exception> onError)
        {
            // Get download size
            AsyncOperationHandle<long> getSizeOp;
            try
            {
                getSizeOp = Addressables.GetDownloadSizeAsync(runtimeKeys);
                onFileSizeRetrieving?.Invoke();
                while (!getSizeOp.IsDone)
                {
                    await UniTask.Yield();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                onError?.Invoke(ex);
                return;
            }
            await UniTask.Yield();
            long fileSize = getSizeOp.Result;
            onFileSizeRetrieved.Invoke(fileSize);
            // Download dependencies
            if (fileSize > 0)
            {
                AsyncOperationHandle downloadOp;
                try
                {
                    downloadOp = Addressables.DownloadDependenciesAsync(runtimeKeys, Addressables.MergeMode.Union);
                    await UniTask.Yield();
                    onDepsDownloading?.Invoke();
                    while (!downloadOp.IsDone)
                    {
                        await UniTask.Yield();
                        float percentageComplete = downloadOp.GetDownloadStatus().Percent;
                        onDepsFileDownloading?.Invoke((long)(percentageComplete * fileSize), fileSize, percentageComplete);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogException(ex);
                    onError?.Invoke(ex);
                    return;
                }
                await UniTask.Yield();
                onDepsDownloaded?.Invoke();
                Addressables.ReleaseInstance(downloadOp);
            }
            else
            {
                onDepsDownloading?.Invoke();
                onDepsFileDownloading?.Invoke(0, 0, 1);
                onDepsDownloaded?.Invoke();
            }
        }
    }
}
