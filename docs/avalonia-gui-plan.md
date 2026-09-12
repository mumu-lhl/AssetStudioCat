# Avalonia GUI development plan

## Goals

The new desktop GUI targets Windows, Linux, and macOS while keeping the existing
WinForms application available until feature parity is reached. It calls the
AssetStudio libraries directly; it is not a wrapper around the CLI.

The design has two equally important goals:

1. Preserve the core desktop workflows: loading folders or explicit file sets,
   paged asset browsing, scene/Animator FBX export, conversion, filtering, and
   previews. The legacy WinForms-only Live2D and bundle-extraction dialogs remain
   separate until their native dependencies have cross-platform replacements.
2. Make very large bundle directories usable on machines with 8 GB of RAM by
   avoiding a permanently materialized object graph.

## Architecture

- `AssetStudio` remains the Unity serialization and bundle layer.
- `AssetStudioUtility` remains the conversion/export layer.
- `AssetStudio.AppCore` owns settings, sessions, persistent indexes, cache
  validation, paged queries, and preview/export orchestration. It has no UI
  dependency.
- `AssetStudioGUI.Avalonia` contains AXAML views, view models, and small platform
  adapters such as file pickers.
- `AssetStudio.AppCore.Tests` tests cache paths, settings, index invalidation,
  paging, and query behavior without launching a window.

## Cache model

The application uses separate cache classes so users can clean one without
destroying the others:

- A persistent paged asset index stores source fingerprints, object locations,
  names, types, containers, hierarchy edges, and dependency hints. Its interface
  is storage-agnostic; the first backend uses JSON-lines plus a binary seek table
  so packaging has no native database dependency. SQLite remains a compatible
  future backend.
- A decompression workspace stores seekable bundle data for the current session
  at the user-selected location and removes it on clean exit.
- A bounded preview cache stores thumbnails and recently decoded previews.

An index is built in a temporary directory and atomically promoted only after
the scan succeeds. Source size and last-write time provide the fast validity
check; a content fingerprint protects against ambiguous changes. Users can
rebuild the whole index or only changed sources.

## Loading model

The final low-memory path is deliberately different from the existing
`AssetsManager.ReadAssets()` path:

1. Scan bundle and serialized-file metadata.
2. Stream minimal rows into the persistent index instead of retaining every `Object`.
3. Query only the page needed by the asset list; hierarchy rows are created from
   the disk index and only the requested model is materialized for FBX export.
4. Materialize a typed object only for preview, inspection, or export.
5. Resolve `PPtr` references through a lazy object store and retain objects in a
   bounded LRU cache.
6. Pin an Animator dependency closure while FBX export runs, then release it.

The index builder now keeps serialized-file metadata and materializes one typed
object at a time to obtain its display name. Objects are discarded as their rows
are written, so the complete typed-object graph is never retained. A later
name-only reader can reduce transient allocations further.

Each index row also records the top-level source file, internal serialized-file
path, and PathID. Selecting a Texture2D reopens only that source file, resolves
one object, converts it to PNG, and closes the object session. Encoded previews
use a byte-bounded in-memory LRU; the Avalonia view retains only the displayed
decoded bitmap.

## Decompression settings

Users can select `Auto`, `Memory`, or `Disk` and can choose both the disk
decompression directory and persistent cache root.
Auto mode uses a conservative memory budget and falls back to disk before load.
The UI performs write-access and free-space checks before starting a disk load.

Session data is stored under an isolated directory. Normal close removes
session-only data; the reusable asset index is independent of decompressed
session files.

## Delivery slices

1. Solution scaffold and application shell.
2. Cross-platform settings, cache layout, and configurable decompression mode.
3. Persistent index schema, validation, rebuild, and paged queries.
4. Folder/file-set loading, progress/cancellation, asset list, filtering, and bounded sorting.
5. Lazy object resolver plus Texture2D, Sprite, mesh, text, shader, and audio metadata previews.
6. Converted/raw/dump export, Animator dependency loading/FBX export, and scene-model FBX export.
7. Scene hierarchy, recent-source restore, cache management, audio playback handoff, packaging, and platform CI.

## Current Avalonia operation model

- Opening a folder creates one independent index key; opening multiple files creates a key from the complete ordered selection. They never share or overwrite an index.
- The first open streams rows into `assets.jsonl` and writes a seek table. Later opens validate the source fingerprint and reuse the index. The cache manager can delete one index, and **Rebuild index** creates it again.
- The asset list reads at most one 250-row page. Search/type filtering streams the index; non-index sorting retains only the requested page plus its offset in a bounded priority queue.
- Selecting a row reopens its owning bundle only when preview, dump, converted export, raw export, or FBX export needs the real Unity object; preview data is held in a byte-bounded cache.
- Audio preview displays metadata. **Play in system player** exports only that clip into the cache's playback directory and asks Windows, macOS, or Linux to open it with the default player.
- **Extract opened Bundles** is the explicit, persistent decompression workflow. It preserves each bundle's internal path below a `*_unpacked` directory and never overwrites an existing extracted file.
- Filtered asset-list export writes CSV or JSON directly from the persistent index, so it remains a metadata-only operation.
- Loading and rebuilding have cancellation; a newer filter/sort query cancels its predecessor so stale results cannot replace the current page.
- Settings can override a stripped/incorrect Unity version for both indexing and on-demand loads; rebuilding the index applies the new interpretation to cached metadata.

Commits are made at the end of these coherent slices rather than per file.

## Continuous integration and releases

- `Avalonia GUI CI` builds the GUI and runs AppCore tests on Windows, Ubuntu,
  and macOS for feature-branch pushes and pull requests to `AssetStudioMod`.
- Pushing a tag matching `v*` runs tests, publishes self-contained packages for
  `win-x64`, `linux-x64`, `osx-x64`, and `osx-arm64`, generates SHA-256 sums,
  and creates a GitHub Release with generated notes.
- Linux and macOS packages use `tar.gz` to retain executable permissions;
  Windows uses `zip`.

## Performance targets

- Empty application: below 200 MB working set on a typical desktop.
- Browsing an existing index: below 400 MB for a 4 GB source directory.
- Default preview cache: 128 MB, configurable.
- Asset rows and hierarchy children are paged/virtualized.
- Full-resolution large textures and complex FBX exports are explicit operations
  that may temporarily exceed the browsing memory target.
