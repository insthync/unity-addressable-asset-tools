# unity-addressable-asset-tools
A collection of tools which being used for my project's addressable asset management

## Startup download memory

`AddressableAssetDownloadManager` downloads all catalog bundles in batches (16 by default),
releases each operation before starting the next batch, and clears the temporary download plan
when it finishes or fails. Cached batches are skipped. Existing download progress events still
report the overall byte total.

For Unity packed binary catalogs, `useBundleKeysForBuiltInCatalogs` resolves the generated
`.bundle` keys instead of expanding every asset address, GUID, and label in the catalog.
Unity's Editor Asset Database locator is skipped because it loads source assets directly,
without downloadable bundles. This avoids importing and inspecting every addressable asset
before Play mode can initialize. Built-content catalogs and custom locators are still scanned.
Other locator implementations, and catalogs without bundle keys, use a full lookup fallback.
Disable this option for custom binary catalogs that do not expose every bundle under a
`.bundle` key. Standard bundle downloads are flattened after collecting all dependencies;
custom providers retain their dependency graph.

Editor and development builds log `[Addressables Download]` memory samples after initialization,
catalog updates, bundle planning, downloading, and settings release. Compare these stages in
fresh builds with the same startup content to distinguish catalog, download, and asset memory.

Initial objects stay sequential, with one frame between activations. Each successful instance
retains its Addressables handle until its `AssetReferenceReleaser` is destroyed. Failed or
abandoned startup operations release their handles. Shader collections load one at a time,
use `WarmUpProgressively` with `shaderVariantsPerFrame` (32 by default), and release their
load handles before the next collection. A single variant can still block the main thread;
this setting limits variants per frame, not milliseconds. Required live assets and warmed
shader programs still consume memory.

Development logs include pending download bytes (zero means there is no content to download),
per-object memory/timing, shader counts and memory before/after warmup, and a final startup
sample after settings release. These are stage samples rather than a continuous peak trace;
background initialization can also contribute to each sample.

## Runtime asset ownership

Instantiation helpers attach `AssetReferenceReleaser` to each successful Addressables instance.
Destroying that instance releases its owning operation; explicit `Addressables.ReleaseInstance`
also remains valid. Inactive instances are monitored until activation or destruction so objects
that never become active do not depend on Unity sending `OnDestroy`. Abandoned parent hierarchies
and failed instantiations release their operations too. Plain prefab fallbacks keep normal Unity
ownership. A typed instantiation with a missing component destroys the unusable instance.

Bulk asset-loading helpers retain their existing overloads and result order but default to four
concurrent loads. Pass `maxConcurrentLoads` to the new overloads to adjust this limit. Bulk
instantiation is sequential to avoid accumulating a large set of pending instance operations.
Synchronous cache requests join an existing asynchronous operation for the same key rather than
starting another load. Successful asset loads are still cached until `Release` or `ReleaseAll`;
the cache is not automatically evicted while consumers may still use its assets.

`InstantiateByAssetReference` now starts its configured asset instantiation instead of cloning
its parent, and applies `destroyDelay` to successful instances.

`ReleaseAll` drops the old cache entries before releasing their handles, preserves fresh loads
started by release callbacks, trims empty cache storage, and logs one summary instead of a
stack trace for every asset. Individual `Release` calls also remove their entry before invoking
Addressables cleanup.
