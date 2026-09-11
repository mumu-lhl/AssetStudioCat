# Avalonia GUI development plan

## Goals

The new desktop GUI targets Windows, Linux, and macOS while keeping the existing
WinForms application available until feature parity is reached. It calls the
AssetStudio libraries directly; it is not a wrapper around the CLI.

The design has two equally important goals:

1. Preserve the existing GUI workflows, including Animator/AnimationClip FBX
   export, Live2D export, bundle extraction, asset filtering, and previews.
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
- A decompression cache stores seekable bundle data either for the current
  session or persistently, according to user settings.
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
3. Query only the page needed by the asset list or expanded hierarchy node.
4. Materialize a typed object only for preview, inspection, or export.
5. Resolve `PPtr` references through a lazy object store and retain objects in a
   bounded LRU cache.
6. Pin an Animator dependency closure while FBX export runs, then release it.

The first implementation milestone may use the existing eager parser as a
compatibility path, but it must be visibly labelled and must not be confused
with the completed low-memory loader.

## Decompression settings

Users can select `Auto`, `Memory`, or `Disk` and can choose the disk cache root.
Auto mode uses a conservative memory budget and falls back to disk before load.
The UI performs write-access and free-space checks before starting a disk load.

Session data is stored under an isolated directory. Normal close removes
session-only data, and startup cleanup handles abandoned sessions. Persistent
decompressed data uses source fingerprints in its path.

## Delivery slices

1. Solution scaffold and application shell.
2. Cross-platform settings, cache layout, and configurable decompression mode.
3. Persistent index schema, validation, rebuild, and paged queries.
4. Folder loading, progress/cancellation, asset list, filtering, and sorting.
5. Lazy object resolver plus text and Texture2D previews.
6. Converted/raw/dump export and Animator dependency loading/FBX export.
7. Scene hierarchy, audio, mesh, font, Live2D, packaging, and platform CI.

Commits are made at the end of these coherent slices rather than per file.

## Performance targets

- Empty application: below 200 MB working set on a typical desktop.
- Browsing an existing index: below 400 MB for a 4 GB source directory.
- Default preview cache: 128 MB, configurable.
- Asset rows and hierarchy children are paged/virtualized.
- Full-resolution large textures and complex FBX exports are explicit operations
  that may temporarily exceed the browsing memory target.
