using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;

namespace WitchDrawer.Core.Services;

/// <summary>
/// Low-level P/Invoke declarations for the Rust native library (witchdrawer_core.dll).
/// All methods use Cdecl calling convention and UTF-8 string marshalling.
/// </summary>
internal static class RustCore
{
    internal const string DllName = "witchdrawer_core.dll";

    // ── Native declarations ──────────────────────────────────────────────

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_init([MarshalAs(UnmanagedType.LPUTF8Str)] string dataDir);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wd_dispose(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_boxes(RustContextHandle ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_create_box(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        int boxType);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_update_box_name(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string boxId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newName);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_reorder_boxes(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string jsonArrayOfIds);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_delete_box(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string boxId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_items(RustContextHandle ctx, IntPtr boxIdOrNull);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_search_items(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string query,
        int limit);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_import_path(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string boxId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sourcePath,
        int gridCol,
        int gridRow);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_move_item_to_box(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string itemId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string targetBoxId,
        int gridCol,
        int gridRow);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_delete_item(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string itemId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_export_item(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string itemId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string targetDir);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_update_grid_pos(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string itemId,
        int gridCol,
        int gridRow);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_setting(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_set_setting(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_update_grid_positions(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string jsonPositions);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_delete_setting(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_checkpoint(RustContextHandle ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_todos(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string boxId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_get_archived_todos(RustContextHandle ctx, IntPtr boxIdOrNull);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_add_todo(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string boxId,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_set_todo_completed(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string todoId,
        int isCompleted);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_delete_todo(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string todoId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_archive_completed(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string boxId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_restore_archived(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string todoId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_check_update(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string currentVersion);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_download_and_apply_update(
        RustContextHandle ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string downloadUrl,
        IntPtr expectedSha256OrNull);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_cleanup_legacy_updater_artifacts(
        RustContextHandle ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr wd_confirm_update_startup(RustContextHandle ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void wd_free_string(IntPtr ptr);

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Read a returned C string and free it, then deserialize the JSON FfiResponse.</summary>
    internal static T Call<T>(Func<IntPtr> nativeCall)
    {
        var ptr = nativeCall();
        var json = ReadAndFree(ptr);
        var response = JsonSerializer.Deserialize<FfiResponse<T>>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize Rust response: {json}");

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "Unknown Rust error");
        }

        return response.Data ?? throw new InvalidOperationException("Rust returned ok but data was null");
    }

    /// <summary>Read a returned C string and free it, then deserialize a void FfiResponse (returns null on success).</summary>
    internal static void CallVoid(Func<IntPtr> nativeCall)
    {
        var ptr = nativeCall();
        var json = ReadAndFree(ptr);
        var response = JsonSerializer.Deserialize<FfiResponse<JsonElement>>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize Rust response: {json}");

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "Unknown Rust error");
        }
    }

    internal static T? CallNullable<T>(Func<IntPtr> nativeCall)
    {
        var ptr = nativeCall();
        var json = ReadAndFree(ptr);
        var response = JsonSerializer.Deserialize<FfiResponse<T>>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize Rust response: {json}");

        if (!response.Ok)
        {
            throw new InvalidOperationException(response.Error ?? "Unknown Rust error");
        }

        return response.Data;
    }

    internal static string ReadAndFree(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Rust returned a null pointer");
        }

        try
        {
            return Marshal.PtrToStringUTF8(ptr)
                ?? throw new InvalidOperationException("Rust returned null string");
        }
        finally
        {
            wd_free_string(ptr);
        }
    }

    // ── FFI response model ───────────────────────────────────────────────

    /// <summary>Matches the Rust FfiResponse&lt;T&gt; JSON envelope.</summary>
    private sealed class FfiResponse<T>
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("data")]
        public T? Data { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    // ── FFI JSON models (snake_case → PascalCase mapping) ─────────────────

    /// <summary>Matches Rust FfiBox.</summary>
    public sealed class FfiBoxDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("storage_path")]
        public string? StoragePath { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        public Box ToModel() => new(
            Guid.Parse(Id),
            Name,
            (BoxType)Type,
            StoragePath,
            SortOrder,
            DateTimeOffset.Parse(CreatedAt),
            DateTimeOffset.Parse(UpdatedAt));
    }

    /// <summary>Matches Rust FfiDrawerItem.</summary>
    public sealed class FfiDrawerItemDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("box_id")]
        public string BoxId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("item_kind")]
        public int ItemKind { get; set; }

        [JsonPropertyName("source_path")]
        public string? SourcePath { get; set; }

        [JsonPropertyName("stored_path")]
        public string? StoredPath { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("grid_column")]
        public int? GridColumn { get; set; }

        [JsonPropertyName("grid_row")]
        public int? GridRow { get; set; }

        public DrawerItem ToModel() => new(
            Guid.Parse(Id),
            Guid.Parse(BoxId),
            DisplayName,
            (ItemKind)ItemKind,
            SourcePath,
            StoredPath,
            SortOrder,
            DateTimeOffset.Parse(CreatedAt),
            DateTimeOffset.Parse(UpdatedAt),
            GridColumn,
            GridRow);
    }

    /// <summary>Matches Rust FfiTodoItem.</summary>
    public sealed class FfiTodoItemDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("box_id")]
        public string BoxId { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("is_completed")]
        public bool IsCompleted { get; set; }

        [JsonPropertyName("sort_order")]
        public int SortOrder { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("updated_at")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("completed_at")]
        public string? CompletedAt { get; set; }

        [JsonPropertyName("is_archived")]
        public bool IsArchived { get; set; }

        [JsonPropertyName("archived_at")]
        public string? ArchivedAt { get; set; }

        public TodoItem ToModel() => new(
            Guid.Parse(Id),
            Guid.Parse(BoxId),
            Title,
            IsCompleted,
            SortOrder,
            DateTimeOffset.Parse(CreatedAt),
            DateTimeOffset.Parse(UpdatedAt),
            CompletedAt is not null ? DateTimeOffset.Parse(CompletedAt) : null,
            IsArchived,
            ArchivedAt is not null ? DateTimeOffset.Parse(ArchivedAt) : null);
    }

    /// <summary>Matches Rust ItemDeleteResult.</summary>
    public sealed class FfiItemDeleteResultDto
    {
        [JsonPropertyName("item_id")]
        public string ItemId { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("was_stored_item")]
        public bool WasStoredItem { get; set; }

        [JsonPropertyName("restored_path")]
        public string? RestoredPath { get; set; }

        [JsonPropertyName("restored_to_original")]
        public bool RestoredToOriginal { get; set; }

        [JsonPropertyName("restored_to_desktop")]
        public bool RestoredToDesktop { get; set; }

        public ItemDeleteResult ToModel() => new(
            Guid.Parse(ItemId),
            DisplayName,
            WasStoredItem,
            RestoredPath,
            RestoredToOriginal,
            RestoredToDesktop);
    }

    /// <summary>Matches Rust BoxDeleteResult.</summary>
    public sealed class FfiBoxDeleteResultDto
    {
        [JsonPropertyName("box_id")]
        public string BoxId { get; set; } = string.Empty;

        [JsonPropertyName("box_name")]
        public string BoxName { get; set; } = string.Empty;

        [JsonPropertyName("box_type")]
        public int BoxType { get; set; }

        [JsonPropertyName("box_removed")]
        public bool BoxRemoved { get; set; }

        [JsonPropertyName("restored_count")]
        public int RestoredCount { get; set; }

        [JsonPropertyName("failed_count")]
        public int FailedCount { get; set; }

        [JsonPropertyName("failures")]
        public List<string> Failures { get; set; } = new();

        public BoxDeleteResult ToModel() => new(
            Guid.Parse(BoxId),
            BoxName,
            (BoxType)BoxType,
            BoxRemoved,
            RestoredCount,
            FailedCount,
            Failures);
    }

    /// <summary>Matches Rust UpdateCheckResult.</summary>
    public sealed class FfiUpdateCheckResultDto
    {
        [JsonPropertyName("has_update")]
        public bool HasUpdate { get; set; }

        [JsonPropertyName("latest_version")]
        public string LatestVersion { get; set; } = "0.0.0";

        [JsonPropertyName("release_notes")]
        public string ReleaseNotes { get; set; } = string.Empty;

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("expected_sha256")]
        public string? ExpectedSha256 { get; set; }

        public UpdateCheckResult ToModel() => new()
        {
            HasUpdate = HasUpdate,
            LatestVersion = Version.TryParse(LatestVersion, out var v) ? v : new Version(0, 0, 0),
            ReleaseNotes = ReleaseNotes,
            DownloadUrl = DownloadUrl,
            ExpectedSha256 = ExpectedSha256
        };
    }
}

internal sealed class RustContextHandle : SafeHandle
{
    internal RustContextHandle(IntPtr handle)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        RustCore.wd_dispose(handle);
        return true;
    }
}

// ============================================================================
// RustDrawerService – production async adapter with synchronous benchmark helpers
// ============================================================================

/// <summary>
/// Wraps the Rust native DrawerService via P/Invoke.
/// Implements the production async service contract while retaining synchronous
/// helpers for isolated integration tests and migration benchmarks.
/// </summary>
public sealed class RustDrawerService : IDisposable, IDrawerService
{
    private readonly string _dataDirectory;
    private readonly object _contextLock = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private RustContextHandle? _ctx;
    private bool _disposed;

    /// <summary>
    /// Configure the native context. Initialization is deferred until
    /// <see cref="InitializeAsync"/> or the first synchronous benchmark call.
    /// </summary>
    public RustDrawerService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
    }

    internal RustContextHandle Context
    {
        get
        {
            lock (_contextLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_ctx is not null)
                {
                    return _ctx;
                }

                var context = RustCore.wd_init(_dataDirectory);
                if (context == IntPtr.Zero)
                {
                    throw new InvalidOperationException("Failed to initialize Rust core");
                }

                _ctx = new RustContextHandle(context);
                return _ctx;
            }
        }
    }

    // ── Box operations ───────────────────────────────────────────────────

    public IReadOnlyList<Box> GetBoxes()
    {
        var list = RustCore.Call<List<RustCore.FfiBoxDto>>(() => RustCore.wd_get_boxes(Context));
        return list.Select(dto => dto.ToModel()).ToList();
    }

    public Box CreateBox(string name, BoxType type)
    {
        var dto = RustCore.Call<RustCore.FfiBoxDto>(() =>
            RustCore.wd_create_box(Context, name, (int)type));
        return dto.ToModel();
    }

    public void RenameBox(Guid boxId, string newName)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_update_box_name(Context, boxId.ToString(), newName));
    }

    public void ReorderBoxes(IReadOnlyList<Guid> orderedBoxIds)
    {
        var json = JsonSerializer.Serialize(orderedBoxIds.Select(id => id.ToString()));
        RustCore.CallVoid(() =>
            RustCore.wd_reorder_boxes(Context, json));
    }

    public BoxDeleteResult DeleteBox(Guid boxId)
    {
        var dto = RustCore.Call<RustCore.FfiBoxDeleteResultDto>(() =>
            RustCore.wd_delete_box(Context, boxId.ToString()));
        return dto.ToModel();
    }

    // ── Item operations ──────────────────────────────────────────────────

    public IReadOnlyList<DrawerItem> GetItems(Guid boxId)
    {
        var ptr = Marshal.StringToCoTaskMemUTF8(boxId.ToString());
        try
        {
            var list = RustCore.Call<List<RustCore.FfiDrawerItemDto>>(() => RustCore.wd_get_items(Context, ptr));
            return list.Select(dto => dto.ToModel()).ToList();
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(ptr);
        }
    }

    public IReadOnlyList<DrawerItem> GetAllItems()
    {
        var list = RustCore.Call<List<RustCore.FfiDrawerItemDto>>(() => RustCore.wd_get_items(Context, IntPtr.Zero));
        return list.Select(dto => dto.ToModel()).ToList();
    }

    public IReadOnlyList<DrawerItem> SearchItems(string query, int limit = 200)
    {
        var list = RustCore.Call<List<RustCore.FfiDrawerItemDto>>(() =>
            RustCore.wd_search_items(Context, query, limit));
        return list.Select(dto => dto.ToModel()).ToList();
    }

    public DrawerItem ImportPath(Guid boxId, string sourcePath, int? gridColumn = null, int? gridRow = null)
    {
        var dto = RustCore.Call<RustCore.FfiDrawerItemDto>(() =>
            RustCore.wd_import_path(Context,
                boxId.ToString(),
                sourcePath,
                gridColumn ?? -1,
                gridRow ?? -1));
        return dto.ToModel();
    }

    public void MoveItemToBox(Guid itemId, Guid targetBoxId, int? gridColumn = null, int? gridRow = null)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_move_item_to_box(Context,
                itemId.ToString(),
                targetBoxId.ToString(),
                gridColumn ?? -1,
                gridRow ?? -1));
    }

    public ItemDeleteResult DeleteItem(Guid itemId)
    {
        var dto = RustCore.Call<RustCore.FfiItemDeleteResultDto>(() =>
            RustCore.wd_delete_item(Context, itemId.ToString()));
        return dto.ToModel();
    }

    public string ExportItemToDirectory(Guid itemId, string targetDirectory)
    {
        return RustCore.Call<string>(() =>
            RustCore.wd_export_item(Context, itemId.ToString(), targetDirectory));
    }

    public void UpdateItemGridPosition(Guid itemId, int? gridColumn, int? gridRow)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_update_grid_pos(Context,
                itemId.ToString(),
                gridColumn ?? -1,
                gridRow ?? -1));
    }

    public void Dispose()
    {
        RustContextHandle? context;
        lock (_contextLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            context = _ctx;
            _ctx = null;
        }

        context?.Dispose();
        _mutationGate.Dispose();
    }

    public string? GetSetting(string key)
    {
        return RustCore.CallNullable<string>(() => RustCore.wd_get_setting(Context, key));
    }

    public void SetSetting(string key, string value)
    {
        RustCore.CallVoid(() => RustCore.wd_set_setting(Context, key, value));
    }

    public void UpdateItemGridPositions(IReadOnlyDictionary<Guid, (int GridColumn, int GridRow)> positions)
    {
        var payload = positions.Select(
            e => new { id = e.Key.ToString(), col = e.Value.GridColumn, row = e.Value.GridRow });
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        RustCore.CallVoid(() => RustCore.wd_update_grid_positions(Context, json));
    }

    public bool DeleteSetting(string key)
    {
        return RustCore.Call<bool>(() => RustCore.wd_delete_setting(Context, key));
    }

    public void Checkpoint()
    {
        RustCore.CallVoid(() => RustCore.wd_checkpoint(Context));
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(() => { _ = Context; }, cancellationToken);
    }

    public Task<IReadOnlyList<Box>> GetBoxesAsync(CancellationToken cancellationToken = default) =>
        RunAsync(GetBoxes, cancellationToken);

    public Task ReorderBoxesAsync(
        IReadOnlyList<Guid> orderedBoxIds,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => ReorderBoxes(orderedBoxIds), cancellationToken);

    public Task<IReadOnlyList<DrawerItem>> GetItemsAsync(
        Guid boxId,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => GetItems(boxId), cancellationToken);

    public Task<IReadOnlyList<DrawerItem>> GetAllItemsAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(GetAllItems, cancellationToken);

    public Task<IReadOnlyList<DrawerItem>> SearchItemsAsync(
        string query,
        int limit = 200,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => SearchItems(query, limit), cancellationToken);

    public Task<Box> CreateBoxAsync(
        string name,
        BoxType type,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => CreateBox(name, type), cancellationToken);

    public Task<DrawerItem> ImportPathAsync(
        Guid boxId,
        string sourcePath,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => ImportPath(boxId, sourcePath, gridColumn, gridRow), cancellationToken);

    public Task UpdateItemGridPositionAsync(
        Guid itemId,
        int? gridColumn,
        int? gridRow,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => UpdateItemGridPosition(itemId, gridColumn, gridRow), cancellationToken);

    public Task MoveItemToBoxAsync(
        Guid itemId,
        Guid targetBoxId,
        int? gridColumn = null,
        int? gridRow = null,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => MoveItemToBox(itemId, targetBoxId, gridColumn, gridRow), cancellationToken);

    public Task<string> ExportItemToDirectoryAsync(
        Guid itemId,
        string targetDirectory,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => ExportItemToDirectory(itemId, targetDirectory), cancellationToken);

    public Task<ItemDeleteResult> DeleteItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => DeleteItem(itemId), cancellationToken);

    public Task<BoxDeleteResult> DeleteBoxAsync(
        Guid boxId,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => DeleteBox(boxId), cancellationToken);

    public Task<string?> GetSettingAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => GetSetting(key), cancellationToken);

    public Task SetSettingAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => SetSetting(key, value), cancellationToken);

    public Task UpdateItemGridPositionsAsync(
        IReadOnlyDictionary<Guid, (int GridColumn, int GridRow)> positions,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => UpdateItemGridPositions(positions), cancellationToken);

    public Task<bool> DeleteSettingAsync(
        string key,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => DeleteSetting(key), cancellationToken);

    public Task CheckpointAsync(CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => Checkpoint(), cancellationToken);

    public Task RenameBoxAsync(
        Guid boxId,
        string newName,
        CancellationToken cancellationToken = default) =>
        RunExclusiveAsync(() => RenameBox(boxId, newName), cancellationToken);

    public async Task OpenItemAsync(
        Guid itemId,
        IFileLauncher launcher,
        CancellationToken cancellationToken = default)
    {
        var item = await RunExclusiveAsync(
            () => GetAllItems().FirstOrDefault(candidate => candidate.Id == itemId)
                ?? throw new InvalidOperationException("Item does not exist."),
            cancellationToken);
        var path = item.EffectivePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Item has no file path.");
        }

        await launcher.OpenAsync(path, cancellationToken);
    }

    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return operation();
        }, cancellationToken);
    }

    private static Task RunAsync(Action operation, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation();
        }, cancellationToken);
    }

    internal async Task<T> RunExclusiveAsync<T>(
        Func<T> operation,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            return await RunAsync(operation, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    internal async Task RunExclusiveAsync(Action operation, CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            await RunAsync(operation, cancellationToken);
        }
        finally
        {
            _mutationGate.Release();
        }
    }
}

// ============================================================================
// RustTodoService
// ============================================================================

/// <summary>
/// Wraps the Rust native TodoService via P/Invoke.
/// Implements the production <see cref="ITodoService"/> contract.
/// </summary>
public sealed class RustTodoService : ITodoService
{
    /// <summary>Maximum allowed todo title length. Mirrors upstream <c>TodoService.MaximumTitleLength</c>.</summary>
    public const int MaximumTitleLength = 200;

    private readonly RustDrawerService _owner;

    /// <summary>
    /// Keeps the owning drawer service alive and shares its native context.
    /// </summary>
    public RustTodoService(RustDrawerService owner)
    {
        _owner = owner;
    }

    public IReadOnlyList<TodoItem> GetTodos(Guid boxId)
    {
        var list = RustCore.Call<List<RustCore.FfiTodoItemDto>>(() =>
            RustCore.wd_get_todos(_owner.Context, boxId.ToString()));
        return list.Select(dto => dto.ToModel()).ToList();
    }

    public IReadOnlyList<TodoItem> GetArchivedTodos(Guid? boxId = null)
    {
        if (boxId is null)
        {
            var all = RustCore.Call<List<RustCore.FfiTodoItemDto>>(() =>
                RustCore.wd_get_archived_todos(_owner.Context, IntPtr.Zero));
            return all.Select(dto => dto.ToModel()).ToList();
        }

        var pointer = Marshal.StringToCoTaskMemUTF8(boxId.Value.ToString());
        try
        {
            var filtered = RustCore.Call<List<RustCore.FfiTodoItemDto>>(() =>
                RustCore.wd_get_archived_todos(_owner.Context, pointer));
            return filtered.Select(dto => dto.ToModel()).ToList();
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(pointer);
        }
    }

    public TodoItem AddTodo(Guid boxId, string title)
    {
        var dto = RustCore.Call<RustCore.FfiTodoItemDto>(() =>
            RustCore.wd_add_todo(_owner.Context, boxId.ToString(), title));
        return dto.ToModel();
    }

    public TodoItem SetCompleted(Guid todoId, bool isCompleted)
    {
        var dto = RustCore.Call<RustCore.FfiTodoItemDto>(() =>
            RustCore.wd_set_todo_completed(_owner.Context,
                todoId.ToString(),
                isCompleted ? 1 : 0));
        return dto.ToModel();
    }

    public void DeleteTodo(Guid todoId)
    {
        RustCore.CallVoid(() =>
            RustCore.wd_delete_todo(_owner.Context, todoId.ToString()));
    }

    public int ArchiveCompleted(Guid boxId)
    {
        return RustCore.Call<int>(() =>
            RustCore.wd_archive_completed(_owner.Context, boxId.ToString()));
    }

    public TodoItem RestoreArchived(Guid todoId)
    {
        var dto = RustCore.Call<RustCore.FfiTodoItemDto>(() =>
            RustCore.wd_restore_archived(_owner.Context, todoId.ToString()));
        return dto.ToModel();
    }

    public Task<IReadOnlyList<TodoItem>> GetTodosAsync(
        Guid boxId,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => GetTodos(boxId), cancellationToken);

    public Task<IReadOnlyList<TodoItem>> GetArchivedTodosAsync(
        Guid? boxId = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(() => GetArchivedTodos(boxId), cancellationToken);

    public Task<TodoItem> AddTodoAsync(
        Guid boxId,
        string title,
        CancellationToken cancellationToken = default) =>
        _owner.RunExclusiveAsync(() => AddTodo(boxId, title), cancellationToken);

    public Task<TodoItem> SetCompletedAsync(
        Guid todoId,
        bool isCompleted,
        CancellationToken cancellationToken = default) =>
        _owner.RunExclusiveAsync(() => SetCompleted(todoId, isCompleted), cancellationToken);

    public Task DeleteTodoAsync(Guid todoId, CancellationToken cancellationToken = default) =>
        _owner.RunExclusiveAsync(() => DeleteTodo(todoId), cancellationToken);

    public Task<int> ArchiveCompletedAsync(
        Guid boxId,
        CancellationToken cancellationToken = default) =>
        _owner.RunExclusiveAsync(() => ArchiveCompleted(boxId), cancellationToken);

    public Task<TodoItem> RestoreArchivedAsync(
        Guid todoId,
        CancellationToken cancellationToken = default) =>
        _owner.RunExclusiveAsync(() => RestoreArchived(todoId), cancellationToken);

    private static Task<T> RunAsync<T>(Func<T> operation, CancellationToken cancellationToken) =>
        Task.Run(operation, cancellationToken);

}

// ============================================================================
// RustUpdateService
// ============================================================================

/// <summary>
/// Wraps the Rust native UpdateService via P/Invoke.
/// Implements the production <see cref="IUpdateService"/> contract.
/// </summary>
public sealed class RustUpdateService : IUpdateService
{
    private readonly RustDrawerService _owner;
    private readonly IAppLogger? _logger;

    /// <summary>
    /// Keeps the owning drawer service alive and shares its native context.
    /// </summary>
    public RustUpdateService(RustDrawerService owner, IAppLogger? logger = null)
    {
        _owner = owner;
        _logger = logger;
    }

    public UpdateCheckResult CheckForUpdate(Version currentVersion)
    {
        var dto = RustCore.Call<RustCore.FfiUpdateCheckResultDto>(() =>
            RustCore.wd_check_update(_owner.Context, currentVersion.ToString()));
        return dto.ToModel();
    }

    public bool DownloadAndApplyUpdate(string downloadUrl, string? expectedSha256 = null)
    {
        if (expectedSha256 is null)
        {
            return RustCore.Call<bool>(() =>
                RustCore.wd_download_and_apply_update(_owner.Context, downloadUrl, IntPtr.Zero));
        }

        var pointer = Marshal.StringToCoTaskMemUTF8(expectedSha256);
        try
        {
            return RustCore.Call<bool>(() =>
                RustCore.wd_download_and_apply_update(_owner.Context, downloadUrl, pointer));
        }
        finally
        {
            Marshal.ZeroFreeCoTaskMemUTF8(pointer);
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion)
    {
        try
        {
            return await Task.Run(() => CheckForUpdate(currentVersion));
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "Rust core failed to check for updates.");
            return new UpdateCheckResult();
        }
    }

    public async Task<bool> DownloadAndApplyUpdateAsync(
        string downloadUrl,
        IProgress<int>? progress = null,
        string? expectedSha256 = null)
    {
        try
        {
            progress?.Report(0);
            var result = await Task.Run(() => DownloadAndApplyUpdate(downloadUrl, expectedSha256));
            if (result)
            {
                progress?.Report(100);
            }
            return result;
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "Rust core failed to download/apply update.");
            return false;
        }
    }

    public Task<int> CleanupLegacyUpdaterArtifactsAsync()
    {
        try
        {
            return Task.FromResult(RustCore.Call<int>(() =>
                RustCore.wd_cleanup_legacy_updater_artifacts(_owner.Context)));
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "Rust core failed to clean legacy updater artifacts.");
            return Task.FromResult(0);
        }
    }

    public Task<bool> ConfirmUpdateStartupAsync()
    {
        try
        {
            return Task.FromResult(RustCore.Call<bool>(() =>
                RustCore.wd_confirm_update_startup(_owner.Context)));
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "Rust core failed to confirm update startup.");
            return Task.FromResult(false);
        }
    }
}
