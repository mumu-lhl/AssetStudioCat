using System.ComponentModel;
using System.Globalization;

namespace AssetStudioGUI.Avalonia.Localization;

/// <summary>Provides the application strings and notifies bindings when the UI language changes.</summary>
public sealed class AppLocalizer : INotifyPropertyChanged
{
    public const string SystemDefault = "auto";
    public const string English = "en";
    public const string SimplifiedChinese = "zh-CN";

    private static readonly IReadOnlyDictionary<string, string> EnglishStrings = new Dictionary<string, string>
    {
        ["Language"] = "Language",
        ["SystemDefault"] = "System default",
        ["English"] = "English",
        ["SimplifiedChinese"] = "Simplified Chinese",
        ["File"] = "_File",
        ["FileName"] = "file",
        ["Export"] = "_Export",
        ["Settings"] = "_Settings",
        ["CacheManager"] = "_Cache manager",
        ["OpenFiles"] = "Open files…",
        ["OpenFolder"] = "Open folder…",
        ["OpenMostRecent"] = "Open most recent",
        ["ExtractOpenedBundles"] = "Extract opened Bundles…",
        ["RebuildIndex"] = "Rebuild index",
        ["ExportAssetListCsv"] = "Export filtered asset list as CSV…",
        ["ExportAssetListJson"] = "Export filtered asset list as JSON…",
        ["ExportConverted"] = "Export converted…",
        ["ExportRaw"] = "Export raw…",
        ["ExportDump"] = "Export complete dump…",
        ["ExportAnimator"] = "Export Animator as FBX…",
        ["ExportSceneModel"] = "Export selected scene model as FBX…",
        ["BatchConverted"] = "Batch converted",
        ["BatchRaw"] = "Batch raw",
        ["BatchDump"] = "Batch dump",
        ["SelectedObjects"] = "Selected objects…",
        ["FilteredObjects"] = "Filtered objects…",
        ["Sort"] = "Sort",
        ["Descending"] = "Descending",
        ["FilterByName"] = "Filter by name or container",
        ["SceneHierarchy"] = "Scene hierarchy",
        ["LoadHierarchy"] = "Load hierarchy",
        ["AssetClasses"] = "Asset classes",
        ["Name"] = "Name",
        ["Container"] = "Container",
        ["Type"] = "Type",
        ["PathId"] = "Path ID",
        ["Size"] = "Size",
        ["Previous"] = "Previous",
        ["Next"] = "Next",
        ["Preview"] = "Preview",
        ["PlayInSystemPlayer"] = "Play in system player",
        ["Dump"] = "Dump",
        ["LoadDump"] = "Load dump",
        ["Information"] = "Information",
        ["CancelLoad"] = "Cancel load",
        ["CancelExport"] = "Cancel export",
        ["CancelExtraction"] = "Cancel extraction",
        ["SettingsTitle"] = "Settings",
        ["LoadingAndCache"] = "Loading and cache",
        ["BundleDecompressionMode"] = "Bundle decompression mode",
        ["AutomaticModeHelp"] = "Automatic mode uses memory only when the estimated unpacked size fits the conservative memory budget.",
        ["DecompressionDirectory"] = "Decompression directory",
        ["Browse"] = "Browse…",
        ["DecompressionDirectoryHelp"] = "Temporary decompressed files are isolated per session and removed on exit. The reusable asset index is stored separately below.",
        ["PersistentCacheRoot"] = "Persistent cache root",
        ["OverrideUnityVersion"] = "Override Unity version (optional)",
        ["UnityVersionExample"] = "Example: 2019.4.40f1",
        ["UnityVersionHelp"] = "Use only when the game version is stripped or detected incorrectly. Rebuild the index after changing this value.",
        ["PreviewCacheLimit"] = "Preview cache limit (MB)",
        ["RestoreLastSource"] = "Restore the most recent source at startup",
        ["ConvertedExport"] = "Converted export",
        ["TextureSpriteFormat"] = "Texture and Sprite image format",
        ["ApplySpriteMask"] = "Apply Sprite alpha mask",
        ["ConvertAudioToWav"] = "Convert supported AudioClips to WAV",
        ["FbxExport"] = "FBX export",
        ["Animations"] = "Animations",
        ["Skins"] = "Skins",
        ["BlendShapes"] = "BlendShapes",
        ["AllNodes"] = "All nodes",
        ["EulerFilter"] = "Euler filter",
        ["AsciiFbx"] = "ASCII FBX",
        ["FbxScaleFactor"] = "FBX scale factor",
        ["Cancel"] = "Cancel",
        ["Save"] = "Save",
        ["CacheManagerTitle"] = "Index cache manager",
        ["PersistentDiskIndexes"] = "Persistent disk indexes",
        ["CacheManagerHelp"] = "Each folder or selected file set has its own index. Deleting one does not touch source assets.",
        ["Source"] = "Source",
        ["Assets"] = "Assets",
        ["Cache"] = "Cache",
        ["Created"] = "Created",
        ["Refresh"] = "Refresh",
        ["DeleteSelected"] = "Delete selected",
        ["ClearAllIndexes"] = "Clear all indexes",
        ["SettingsSaved"] = "Settings saved",
        ["SelectDecompressionDirectory"] = "Select the decompression directory",
        ["SelectPersistentCacheDirectory"] = "Select the persistent cache directory",
        ["UnableToSaveSettings"] = "Unable to save settings",
        ["OpenAssetBundleDirectory"] = "Open AssetBundle directory",
        ["OpenAssetFiles"] = "Open AssetBundle or assets files",
        ["UnityAssets"] = "Unity assets",
        ["ChooseExtractionDirectory"] = "Choose a directory for extracted Bundle files",
        ["ExportRawAsset"] = "Export raw asset",
        ["RawAsset"] = "Raw asset",
        ["ExportAssetList"] = "Export filtered asset list as {0}",
        ["ChooseConvertedDirectory"] = "Choose a directory for the converted asset",
        ["ExportCompleteDump"] = "Export complete object dump",
        ["TextDump"] = "Text dump",
        ["ChooseAnimatorDirectory"] = "Choose a directory for the Animator FBX",
        ["ChooseSceneDirectory"] = "Choose a directory for the scene model FBX",
        ["ChooseBatchDirectory"] = "Choose a directory for batch {0} export",
        ["AllTypes"] = "All types",
        ["NoIndexOpen"] = "No index open",
        ["Rows"] = "Rows {0}–{1}",
        ["Ready"] = "Ready",
        ["SelectTexturePreview"] = "Select a Texture2D asset to preview it.",
        ["NoAssetSelected"] = "No asset selected.",
        ["SelectAssetLoadDump"] = "Select an asset, then choose Load dump.",
        ["ChooseLoadDump"] = "Choose Load dump to inspect this object.",
        ["CheckingIndex"] = "Checking asset index…",
        ["RebuildingIndex"] = "Rebuilding asset index…",
        ["OpenedCachedIndex"] = "Opened cached index: {0:N0} assets",
        ["BuiltStreamingIndex"] = "Built streaming index: {0:N0} assets; {1} decompression",
        ["LoadingCancelled"] = "Loading cancelled",
        ["OpenFailed"] = "Open failed: {0}",
        ["RecentSourceUnavailable"] = "The most recent source is no longer available",
        ["IndexNotOpen"] = "The source index is not open.",
        ["PreviewNotImplemented"] = "Preview for {0} is not implemented yet.",
        ["LoadingPreview"] = "Loading {0} from its source bundle…",
        ["PreviewCacheHit"] = "Decoded preview cache hit",
        ["PreviewFailed"] = "Preview failed: {0}",
        ["BuildingHierarchy"] = "Building scene hierarchy from the disk index…",
        ["HierarchyLoaded"] = "Scene hierarchy loaded: {0:N0} roots",
        ["HierarchyFailed"] = "Hierarchy failed: {0}",
        ["LoadingDump"] = "Loading object dump…",
        ["DumpTruncated"] = "Dump preview truncated to protect memory",
        ["DumpLoaded"] = "Object dump loaded",
        ["DumpFailed"] = "Dump failed: {0}",
        ["ExportingSceneModel"] = "Exporting scene model {0}…",
        ["SceneModelExported"] = "Scene model exported ({0} files)",
        ["SceneModelExportFailed"] = "Scene model export failed: {0}",
        ["ExportingAnimator"] = "Loading the selected AssetBundle and exporting Animator FBX…",
        ["AnimatorExported"] = "Animator exported ({0} files)",
        ["AnimatorExportFailed"] = "Animator export failed: {0}",
        ["Converting"] = "Converting {0}…",
        ["ConvertedExported"] = "Converted asset exported ({0} files)",
        ["ConvertedExportFailed"] = "Converted export failed: {0}",
        ["PreparingAudio"] = "Preparing AudioClip for the system player…",
        ["AudioExportNoFile"] = "Audio export produced no playable file.",
        ["OpenedAudio"] = "Opened AudioClip in the system player",
        ["AudioPlaybackFailed"] = "Audio playback failed: {0}",
        ["ExportingSelected"] = "Exporting selected assets…",
        ["ExportingFiltered"] = "Exporting filtered assets…",
        ["ExportProgress"] = "Exported {0}: {1} succeeded, {2} failed — {3}",
        ["BatchExportComplete"] = "Batch export complete: {0} succeeded",
        ["BatchExportCompleteWithFailures"] = "Batch export complete: {0} succeeded, {1} failed; see {2}",
        ["BatchExportCancelled"] = "Batch export cancelled",
        ["ExtractingBundles"] = "Extracting Bundle files…",
        ["ExtractionProgress"] = "Extracted {0:N0} files — {1:N0}/{2:N0}: {3}",
        ["ExtractionComplete"] = "Bundle extraction complete: {0:N0} files",
        ["ExtractionCancelled"] = "Bundle extraction cancelled",
        ["WritingAssetList"] = "Writing filtered asset list…",
        ["AssetListExported"] = "Asset list exported: {0:N0} rows",
        ["AssetListCancelled"] = "Asset list export cancelled",
        ["AssetListExportFailed"] = "Asset list export failed: {0}",
        ["ExportingDump"] = "Exporting complete dump…",
        ["StreamingRaw"] = "Streaming raw asset to disk…",
        ["ExportedFiles"] = "Exported {0} file{1}",
        ["ExportFailed"] = "Export failed: {0}",
        ["DecompressionMemory"] = "Bundle decompression: memory",
        ["DecompressionDisk"] = "Bundle decompression: disk ({0})",
        ["DecompressionAuto"] = "Bundle decompression: automatic",
        ["CacheSummary"] = "{0:N0} indexes — {1:N1} MB",
        ["ScenePathId"] = "PathID {0}",
    };

    private static readonly IReadOnlyDictionary<string, string> SimplifiedChineseStrings = new Dictionary<string, string>
    {
        ["Language"] = "语言",
        ["SystemDefault"] = "系统默认",
        ["English"] = "English",
        ["SimplifiedChinese"] = "简体中文",
        ["File"] = "文件(_F)",
        ["FileName"] = "文件",
        ["Export"] = "导出(_E)",
        ["Settings"] = "设置(_S)",
        ["CacheManager"] = "缓存管理器(_C)",
        ["OpenFiles"] = "打开文件…",
        ["OpenFolder"] = "打开文件夹…",
        ["OpenMostRecent"] = "打开最近使用的来源",
        ["ExtractOpenedBundles"] = "提取已打开的 Bundle…",
        ["RebuildIndex"] = "重建索引",
        ["ExportAssetListCsv"] = "将筛选后的资源列表导出为 CSV…",
        ["ExportAssetListJson"] = "将筛选后的资源列表导出为 JSON…",
        ["ExportConverted"] = "导出转换后的资源…",
        ["ExportRaw"] = "导出原始资源…",
        ["ExportDump"] = "导出完整转储…",
        ["ExportAnimator"] = "将 Animator 导出为 FBX…",
        ["ExportSceneModel"] = "将选中的场景模型导出为 FBX…",
        ["BatchConverted"] = "批量导出转换后资源",
        ["BatchRaw"] = "批量导出原始资源",
        ["BatchDump"] = "批量导出转储",
        ["SelectedObjects"] = "选中的对象…",
        ["FilteredObjects"] = "筛选后的对象…",
        ["Sort"] = "排序",
        ["Descending"] = "降序",
        ["FilterByName"] = "按名称或容器筛选",
        ["SceneHierarchy"] = "场景层级",
        ["LoadHierarchy"] = "加载层级",
        ["AssetClasses"] = "资源类型",
        ["Name"] = "名称",
        ["Container"] = "容器",
        ["Type"] = "类型",
        ["PathId"] = "路径 ID",
        ["Size"] = "大小",
        ["Previous"] = "上一页",
        ["Next"] = "下一页",
        ["Preview"] = "预览",
        ["PlayInSystemPlayer"] = "使用系统播放器播放",
        ["Dump"] = "转储",
        ["LoadDump"] = "加载转储",
        ["Information"] = "信息",
        ["CancelLoad"] = "取消加载",
        ["CancelExport"] = "取消导出",
        ["CancelExtraction"] = "取消提取",
        ["SettingsTitle"] = "设置",
        ["LoadingAndCache"] = "加载和缓存",
        ["BundleDecompressionMode"] = "Bundle 解压模式",
        ["AutomaticModeHelp"] = "自动模式仅在预计解压后的大小符合保守的内存预算时使用内存。",
        ["DecompressionDirectory"] = "解压目录",
        ["Browse"] = "浏览…",
        ["DecompressionDirectoryHelp"] = "临时解压文件按会话隔离，并会在退出时删除。可复用资源索引单独保存在下方的位置。",
        ["PersistentCacheRoot"] = "持久缓存根目录",
        ["OverrideUnityVersion"] = "覆盖 Unity 版本（可选）",
        ["UnityVersionExample"] = "示例：2019.4.40f1",
        ["UnityVersionHelp"] = "仅当游戏版本被剥离或检测错误时使用。修改后请重建索引。",
        ["PreviewCacheLimit"] = "预览缓存上限 (MB)",
        ["RestoreLastSource"] = "启动时还原最近使用的来源",
        ["ConvertedExport"] = "转换后导出",
        ["TextureSpriteFormat"] = "Texture 和 Sprite 图像格式",
        ["ApplySpriteMask"] = "应用 Sprite Alpha 蒙版",
        ["ConvertAudioToWav"] = "将支持的 AudioClip 转为 WAV",
        ["FbxExport"] = "FBX 导出",
        ["Animations"] = "动画",
        ["Skins"] = "蒙皮",
        ["BlendShapes"] = "混合形状",
        ["AllNodes"] = "所有节点",
        ["EulerFilter"] = "欧拉滤波",
        ["AsciiFbx"] = "ASCII FBX",
        ["FbxScaleFactor"] = "FBX 缩放系数",
        ["Cancel"] = "取消",
        ["Save"] = "保存",
        ["CacheManagerTitle"] = "索引缓存管理器",
        ["PersistentDiskIndexes"] = "持久磁盘索引",
        ["CacheManagerHelp"] = "每个文件夹或所选文件集都有独立索引。删除索引不会影响源资源。",
        ["Source"] = "来源",
        ["Assets"] = "资源",
        ["Cache"] = "缓存",
        ["Created"] = "创建时间",
        ["Refresh"] = "刷新",
        ["DeleteSelected"] = "删除选中项",
        ["ClearAllIndexes"] = "清除所有索引",
        ["SettingsSaved"] = "设置已保存",
        ["SelectDecompressionDirectory"] = "选择解压目录",
        ["SelectPersistentCacheDirectory"] = "选择持久缓存目录",
        ["UnableToSaveSettings"] = "无法保存设置",
        ["OpenAssetBundleDirectory"] = "打开 AssetBundle 目录",
        ["OpenAssetFiles"] = "打开 AssetBundle 或资源文件",
        ["UnityAssets"] = "Unity 资源",
        ["ChooseExtractionDirectory"] = "选择提取 Bundle 文件的目录",
        ["ExportRawAsset"] = "导出原始资源",
        ["RawAsset"] = "原始资源",
        ["ExportAssetList"] = "将筛选后的资源列表导出为 {0}",
        ["ChooseConvertedDirectory"] = "选择转换后资源的目录",
        ["ExportCompleteDump"] = "导出完整对象转储",
        ["TextDump"] = "文本转储",
        ["ChooseAnimatorDirectory"] = "选择 Animator FBX 的目录",
        ["ChooseSceneDirectory"] = "选择场景模型 FBX 的目录",
        ["ChooseBatchDirectory"] = "选择批量 {0} 导出的目录",
        ["AllTypes"] = "所有类型",
        ["NoIndexOpen"] = "未打开索引",
        ["Rows"] = "第 {0}–{1} 行",
        ["Ready"] = "就绪",
        ["SelectTexturePreview"] = "选择一个 Texture2D 资源以预览。",
        ["NoAssetSelected"] = "未选择资源。",
        ["SelectAssetLoadDump"] = "选择资源，然后点击“加载转储”。",
        ["ChooseLoadDump"] = "点击“加载转储”以检查此对象。",
        ["CheckingIndex"] = "正在检查资源索引…",
        ["RebuildingIndex"] = "正在重建资源索引…",
        ["OpenedCachedIndex"] = "已打开缓存索引：{0:N0} 个资源",
        ["BuiltStreamingIndex"] = "已构建流式索引：{0:N0} 个资源；{1} 解压",
        ["LoadingCancelled"] = "加载已取消",
        ["OpenFailed"] = "打开失败：{0}",
        ["RecentSourceUnavailable"] = "最近使用的来源已不可用",
        ["IndexNotOpen"] = "未打开源索引。",
        ["PreviewNotImplemented"] = "尚未实现 {0} 的预览。",
        ["LoadingPreview"] = "正在从源 Bundle 加载 {0}…",
        ["PreviewCacheHit"] = "已命中解码预览缓存",
        ["PreviewFailed"] = "预览失败：{0}",
        ["BuildingHierarchy"] = "正在从磁盘索引构建场景层级…",
        ["HierarchyLoaded"] = "场景层级已加载：{0:N0} 个根节点",
        ["HierarchyFailed"] = "层级加载失败：{0}",
        ["LoadingDump"] = "正在加载对象转储…",
        ["DumpTruncated"] = "为保护内存，转储预览已截断",
        ["DumpLoaded"] = "对象转储已加载",
        ["DumpFailed"] = "转储加载失败：{0}",
        ["ExportingSceneModel"] = "正在导出场景模型 {0}…",
        ["SceneModelExported"] = "场景模型已导出（{0} 个文件）",
        ["SceneModelExportFailed"] = "场景模型导出失败：{0}",
        ["ExportingAnimator"] = "正在加载选中的 AssetBundle 并导出 Animator FBX…",
        ["AnimatorExported"] = "Animator 已导出（{0} 个文件）",
        ["AnimatorExportFailed"] = "Animator 导出失败：{0}",
        ["Converting"] = "正在转换 {0}…",
        ["ConvertedExported"] = "转换后资源已导出（{0} 个文件）",
        ["ConvertedExportFailed"] = "转换后导出失败：{0}",
        ["PreparingAudio"] = "正在为系统播放器准备 AudioClip…",
        ["AudioExportNoFile"] = "音频导出未生成可播放的文件。",
        ["OpenedAudio"] = "已在系统播放器中打开 AudioClip",
        ["AudioPlaybackFailed"] = "音频播放失败：{0}",
        ["ExportingSelected"] = "正在导出选中的资源…",
        ["ExportingFiltered"] = "正在导出筛选后的资源…",
        ["ExportProgress"] = "已导出 {0}：成功 {1}，失败 {2} — {3}",
        ["BatchExportComplete"] = "批量导出完成：成功 {0}",
        ["BatchExportCompleteWithFailures"] = "批量导出完成：成功 {0}，失败 {1}；详见 {2}",
        ["BatchExportCancelled"] = "批量导出已取消",
        ["ExtractingBundles"] = "正在提取 Bundle 文件…",
        ["ExtractionProgress"] = "已提取 {0:N0} 个文件 — {1:N0}/{2:N0}：{3}",
        ["ExtractionComplete"] = "Bundle 提取完成：{0:N0} 个文件",
        ["ExtractionCancelled"] = "Bundle 提取已取消",
        ["WritingAssetList"] = "正在写入筛选后的资源列表…",
        ["AssetListExported"] = "资源列表已导出：{0:N0} 行",
        ["AssetListCancelled"] = "资源列表导出已取消",
        ["AssetListExportFailed"] = "资源列表导出失败：{0}",
        ["ExportingDump"] = "正在导出完整转储…",
        ["StreamingRaw"] = "正在将原始资源流式写入磁盘…",
        ["ExportedFiles"] = "已导出 {0} 个文件{1}",
        ["ExportFailed"] = "导出失败：{0}",
        ["DecompressionMemory"] = "Bundle 解压：内存",
        ["DecompressionDisk"] = "Bundle 解压：磁盘 ({0})",
        ["DecompressionAuto"] = "Bundle 解压：自动",
        ["CacheSummary"] = "{0:N0} 个索引 — {1:N1} MB",
        ["ScenePathId"] = "路径 ID {0}",
    };

    private string _language = English;
    private string _requestedLanguage = SystemDefault;

    public AppLocalizer(string? language = null)
    {
        Language = language ?? SystemDefault;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<LanguageOption> Languages =>
    [
        new(SystemDefault, this["SystemDefault"]),
        new(English, this["English"]),
        new(SimplifiedChinese, this["SimplifiedChinese"]),
    ];

    /// <summary>The effective language currently displayed by the application.</summary>
    public string Language
    {
        get => _language;
        set
        {
            var requested = NormalizeSettingLanguage(value);
            var effective = requested == SystemDefault ? DetectSystemLanguage() : requested;
            if (_requestedLanguage == requested && _language == effective)
            {
                return;
            }

            _requestedLanguage = requested;
            _language = effective;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RequestedLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Languages)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
    }

    /// <summary>The persisted preference: auto, en, or zh-CN.</summary>
    public string RequestedLanguage => _requestedLanguage;

    public string this[string key] => Strings.TryGetValue(key, out var value)
        ? value
        : EnglishStrings.TryGetValue(key, out value) ? value : key;

    public string Format(string key, params object?[] arguments) => string.Format(
        CultureInfo.CurrentCulture,
        this[key],
        arguments);

    private IReadOnlyDictionary<string, string> Strings => _language == SimplifiedChinese
        ? SimplifiedChineseStrings
        : EnglishStrings;

    public static string NormalizeSettingLanguage(string? language) => string.Equals(language, SimplifiedChinese, StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "zh", StringComparison.OrdinalIgnoreCase)
        ? SimplifiedChinese
        : string.Equals(language, English, StringComparison.OrdinalIgnoreCase)
            ? English
            : SystemDefault;

    private static string DetectSystemLanguage() => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
        ? SimplifiedChinese
        : English;
}

public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}
