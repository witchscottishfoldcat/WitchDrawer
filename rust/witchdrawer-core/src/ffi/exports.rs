//! C ABI exports. Every exported operation is unsafe to call from Rust because
//! raw pointers must originate from this library and string pointers must be
//! valid, NUL-terminated UTF-8 for the duration of the call.
#![allow(clippy::missing_safety_doc)]

use std::ffi::{CStr, CString};
use std::os::raw::c_char;
use std::panic;
use std::path::PathBuf;

use crate::logging::FileLogger;
use crate::models::*;
use crate::services::app_paths::AppPaths;
use crate::services::{DrawerService, TodoService, UpdateService};
use crate::storage::DrawerRepository;

// ---------------------------------------------------------------------------
// Context – holds everything an FFI session needs
// ---------------------------------------------------------------------------

/// Opaque context passed across the FFI boundary.
/// Created by `wd_init`, freed by `wd_dispose`.
pub struct Context {
    pub drawer: DrawerService,
    pub todo: TodoService,
    pub update: UpdateService,
    #[allow(dead_code)]
    pub logger: FileLogger,
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/// Unsafely read a C string pointer into a Rust `String`.
/// Returns `None` if the pointer is null or not valid UTF-8.
unsafe fn cstr_to_string(ptr: *const c_char) -> Option<String> {
    if ptr.is_null() {
        return None;
    }
    CStr::from_ptr(ptr).to_str().ok().map(|s| s.to_string())
}

/// Convert a Rust `String` into an owned C string pointer (caller must free
/// with `wd_free_string`).
fn to_cstring_ptr(s: String) -> *mut c_char {
    match CString::new(s) {
        Ok(cs) => cs.into_raw(),
        Err(_) => {
            let err = FfiResponse::<()>::failure("Response contained NUL byte");
            CString::new(err.to_json()).unwrap().into_raw()
        }
    }
}

/// Build a success JSON and return it as a C string pointer.
fn ffi_ok<T: serde::Serialize>(data: T) -> *mut c_char {
    to_cstring_ptr(FfiResponse::success(data).to_json())
}

/// Build a failure JSON and return it as a C string pointer.
fn ffi_err(msg: &str) -> *mut c_char {
    to_cstring_ptr(FfiResponse::<()>::failure(msg).to_json())
}

/// Run a closure inside `catch_unwind` and return the result.
/// If the closure panics, return a JSON error.
fn ffi_catch<F>(f: F) -> *mut c_char
where
    F: FnOnce() -> *mut c_char,
{
    match panic::catch_unwind(panic::AssertUnwindSafe(f)) {
        Ok(ptr) => ptr,
        Err(_) => ffi_err("Internal panic"),
    }
}

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn wd_init(data_dir: *const c_char) -> *mut Context {
    match panic::catch_unwind(panic::AssertUnwindSafe(|| {
        let dir = unsafe { cstr_to_string(data_dir) }.unwrap_or_else(|| {
            std::env::current_dir()
                .map(|p| p.to_string_lossy().into_owned())
                .unwrap_or_else(|_| ".".to_string())
        });

        let data_path = PathBuf::from(&dir);
        let _ = std::fs::create_dir_all(&data_path);

        let log_dir = data_path.join("logs");
        let logger = FileLogger::new(&log_dir, 30);

        let db_path = data_path
            .join("witchdrawer.db")
            .to_string_lossy()
            .into_owned();
        let repo = DrawerRepository::new(&db_path);

        let paths = AppPaths::new(data_path.clone());
        let drawer = DrawerService::new(paths, repo.clone());
        if drawer.initialize().is_err() {
            return std::ptr::null_mut();
        }
        let todo_svc = TodoService::new(repo.clone());
        let update_svc = UpdateService::new();

        let ctx = std::boxed::Box::new(Context {
            drawer,
            todo: todo_svc,
            update: update_svc,
            logger,
        });
        std::boxed::Box::into_raw(ctx)
    })) {
        Ok(ptr) => ptr,
        Err(_) => std::ptr::null_mut(),
    }
}

#[no_mangle]
pub unsafe extern "C" fn wd_dispose(ctx: *mut Context) {
    if !ctx.is_null() {
        unsafe {
            let _ = std::boxed::Box::from_raw(ctx);
        }
    }
}

// ---------------------------------------------------------------------------
// String management
// ---------------------------------------------------------------------------

/// Free a string previously returned by any `wd_*` function.
///
/// # Safety
/// `ptr` must have been returned by a prior FFI call from this library.
#[no_mangle]
pub unsafe extern "C" fn wd_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        unsafe {
            let _ = CString::from_raw(ptr);
        }
    }
}

// ---------------------------------------------------------------------------
// Box operations
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn wd_get_boxes(ctx: *mut Context) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        match ctx.drawer.get_boxes() {
            Ok(boxes) => ffi_ok(boxes.iter().map(FfiBox::from).collect::<Vec<_>>()),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_create_box(
    ctx: *mut Context,
    name: *const c_char,
    box_type: i32,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let name = match unsafe { cstr_to_string(name) } {
            Some(s) => s,
            None => return ffi_err("invalid name"),
        };
        let bt = match BoxType::from_i32(box_type) {
            Some(t) => t,
            None => return ffi_err("invalid box type"),
        };
        match ctx.drawer.create_box(&name, bt) {
            Ok(b) => ffi_ok(FfiBox::from(&b)),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_update_box_name(
    ctx: *mut Context,
    box_id: *const c_char,
    new_name: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let id_str = match unsafe { cstr_to_string(box_id) } {
            Some(s) => s,
            None => return ffi_err("invalid box_id"),
        };
        let id = match parse_uuid(&id_str) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let name = match unsafe { cstr_to_string(new_name) } {
            Some(s) => s,
            None => return ffi_err("invalid new_name"),
        };
        match ctx.drawer.rename_box(id, &name) {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_reorder_boxes(
    ctx: *mut Context,
    json_array_of_ids: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let json_str = match unsafe { cstr_to_string(json_array_of_ids) } {
            Some(s) => s,
            None => return ffi_err("invalid json"),
        };
        let ids: Vec<String> = match serde_json::from_str(&json_str) {
            Ok(v) => v,
            Err(e) => return ffi_err(&format!("invalid JSON array: {}", e)),
        };
        let uuids: Vec<uuid::Uuid> = match ids.iter().map(|s| uuid::Uuid::parse_str(s)).collect() {
            Ok(v) => v,
            Err(e) => return ffi_err(&format!("invalid UUID in array: {}", e)),
        };
        match ctx.drawer.reorder_boxes(&uuids) {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_delete_box(ctx: *mut Context, box_id: *const c_char) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let id_str = match unsafe { cstr_to_string(box_id) } {
            Some(s) => s,
            None => return ffi_err("invalid box_id"),
        };
        let id = match parse_uuid(&id_str) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        match ctx.drawer.delete_box(id) {
            Ok(result) => ffi_ok(result),
            Err(e) => ffi_err(&e.message),
        }
    })
}

// ---------------------------------------------------------------------------
// Item operations
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn wd_get_items(
    ctx: *mut Context,
    box_id_or_null: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let box_id = if box_id_or_null.is_null() {
            None
        } else {
            let s = match unsafe { cstr_to_string(box_id_or_null) } {
                Some(v) => v,
                None => return ffi_err("invalid box_id"),
            };
            if s.is_empty() {
                None
            } else {
                match parse_uuid(&s) {
                    Ok(u) => Some(u),
                    Err(e) => return ffi_err(&e.message),
                }
            }
        };
        let result = match box_id {
            Some(id) => ctx.drawer.get_items(id),
            None => ctx.drawer.get_all_items(),
        };
        match result {
            Ok(items) => ffi_ok(items.iter().map(FfiDrawerItem::from).collect::<Vec<_>>()),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_search_items(
    ctx: *mut Context,
    query: *const c_char,
    limit: i32,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let q = match unsafe { cstr_to_string(query) } {
            Some(s) => s,
            None => return ffi_err("invalid query"),
        };
        match ctx.drawer.search_items(&q, limit) {
            Ok(items) => ffi_ok(items.iter().map(FfiDrawerItem::from).collect::<Vec<_>>()),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_import_path(
    ctx: *mut Context,
    box_id: *const c_char,
    source_path: *const c_char,
    grid_col: i32,
    grid_row: i32,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let id_str = match unsafe { cstr_to_string(box_id) } {
            Some(s) => s,
            None => return ffi_err("invalid box_id"),
        };
        let id = match parse_uuid(&id_str) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let path = match unsafe { cstr_to_string(source_path) } {
            Some(s) => s,
            None => return ffi_err("invalid source_path"),
        };
        let col = if grid_col < 0 { None } else { Some(grid_col) };
        let row = if grid_row < 0 { None } else { Some(grid_row) };
        match ctx.drawer.import_path(id, &path, col, row) {
            Ok(item) => ffi_ok(FfiDrawerItem::from(&item)),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_move_item_to_box(
    ctx: *mut Context,
    item_id: *const c_char,
    target_box_id: *const c_char,
    grid_col: i32,
    grid_row: i32,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let iid = match unsafe { cstr_to_string(item_id) } {
            Some(s) => s,
            None => return ffi_err("invalid item_id"),
        };
        let item_uuid = match parse_uuid(&iid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let tid = match unsafe { cstr_to_string(target_box_id) } {
            Some(s) => s,
            None => return ffi_err("invalid target_box_id"),
        };
        let target_uuid = match parse_uuid(&tid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let col = if grid_col < 0 { None } else { Some(grid_col) };
        let row = if grid_row < 0 { None } else { Some(grid_row) };
        match ctx
            .drawer
            .move_item_to_box(item_uuid, target_uuid, col, row)
        {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_delete_item(ctx: *mut Context, item_id: *const c_char) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let iid = match unsafe { cstr_to_string(item_id) } {
            Some(s) => s,
            None => return ffi_err("invalid item_id"),
        };
        let item_uuid = match parse_uuid(&iid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        match ctx.drawer.delete_item(item_uuid) {
            Ok(result) => ffi_ok(result),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_export_item(
    ctx: *mut Context,
    item_id: *const c_char,
    target_dir: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let iid = match unsafe { cstr_to_string(item_id) } {
            Some(s) => s,
            None => return ffi_err("invalid item_id"),
        };
        let item_uuid = match parse_uuid(&iid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let dir = match unsafe { cstr_to_string(target_dir) } {
            Some(s) => s,
            None => return ffi_err("invalid target_dir"),
        };
        match ctx.drawer.export_item_to_directory(item_uuid, &dir) {
            Ok(path) => ffi_ok(path),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_update_grid_pos(
    ctx: *mut Context,
    item_id: *const c_char,
    grid_col: i32,
    grid_row: i32,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let iid = match unsafe { cstr_to_string(item_id) } {
            Some(s) => s,
            None => return ffi_err("invalid item_id"),
        };
        let item_uuid = match parse_uuid(&iid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let col = if grid_col < 0 { None } else { Some(grid_col) };
        let row = if grid_row < 0 { None } else { Some(grid_row) };
        match ctx.drawer.update_item_grid_position(item_uuid, col, row) {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

// ---------------------------------------------------------------------------
// Settings operations
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn wd_get_setting(ctx: *mut Context, key: *const c_char) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let key = match unsafe { cstr_to_string(key) } {
            Some(value) => value,
            None => return ffi_err("invalid setting key"),
        };
        match ctx.drawer.get_setting(&key) {
            Ok(value) => ffi_ok(value),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_set_setting(
    ctx: *mut Context,
    key: *const c_char,
    value: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let key = match unsafe { cstr_to_string(key) } {
            Some(value) => value,
            None => return ffi_err("invalid setting key"),
        };
        let value = match unsafe { cstr_to_string(value) } {
            Some(value) => value,
            None => return ffi_err("invalid setting value"),
        };
        match ctx.drawer.set_setting(&key, &value) {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

/// Batch-update multiple items' grid positions.
/// `json_positions` is a JSON array of `{"id": "<uuid>", "col": N, "row": N}`.
#[no_mangle]
pub unsafe extern "C" fn wd_update_grid_positions(
    ctx: *mut Context,
    json_positions: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let json_str = match unsafe { cstr_to_string(json_positions) } {
            Some(s) => s,
            None => return ffi_err("invalid json"),
        };
        #[derive(serde::Deserialize)]
        struct Pos {
            id: String,
            col: i32,
            row: i32,
        }
        let positions: Vec<Pos> = match serde_json::from_str(&json_str) {
            Ok(v) => v,
            Err(e) => return ffi_err(&format!("invalid JSON array: {}", e)),
        };
        let mut out = Vec::with_capacity(positions.len());
        for p in positions {
            match uuid::Uuid::parse_str(&p.id) {
                Ok(u) => out.push((u, p.col, p.row)),
                Err(e) => return ffi_err(&format!("invalid UUID '{}': {}", p.id, e)),
            }
        }
        match ctx.drawer.update_item_grid_positions(&out) {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_delete_setting(ctx: *mut Context, key: *const c_char) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let key = match unsafe { cstr_to_string(key) } {
            Some(value) => value,
            None => return ffi_err("invalid setting key"),
        };
        match ctx.drawer.delete_setting(&key) {
            Ok(b) => ffi_ok(serde_json::Value::Bool(b)),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_checkpoint(ctx: *mut Context) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        match ctx.drawer.checkpoint() {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

// ---------------------------------------------------------------------------
// Todo operations
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn wd_get_todos(ctx: *mut Context, box_id: *const c_char) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let id_str = match unsafe { cstr_to_string(box_id) } {
            Some(s) => s,
            None => return ffi_err("invalid box_id"),
        };
        let id = match parse_uuid(&id_str) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        match ctx.todo.get_todos(id) {
            Ok(todos) => ffi_ok(todos.iter().map(FfiTodoItem::from).collect::<Vec<_>>()),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_get_archived_todos(
    ctx: *mut Context,
    box_id_or_null: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let box_id = if box_id_or_null.is_null() {
            None
        } else {
            let value = match unsafe { cstr_to_string(box_id_or_null) } {
                Some(value) => value,
                None => return ffi_err("invalid box_id"),
            };
            match parse_uuid(&value) {
                Ok(id) => Some(id),
                Err(e) => return ffi_err(&e.message),
            }
        };
        match ctx.todo.get_archived_todos(box_id) {
            Ok(todos) => ffi_ok(todos.iter().map(FfiTodoItem::from).collect::<Vec<_>>()),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_add_todo(
    ctx: *mut Context,
    box_id: *const c_char,
    title: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let id_str = match unsafe { cstr_to_string(box_id) } {
            Some(s) => s,
            None => return ffi_err("invalid box_id"),
        };
        let id = match parse_uuid(&id_str) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let t = match unsafe { cstr_to_string(title) } {
            Some(s) => s,
            None => return ffi_err("invalid title"),
        };
        match ctx.todo.add_todo(id, &t) {
            Ok(todo) => ffi_ok(FfiTodoItem::from(&todo)),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_set_todo_completed(
    ctx: *mut Context,
    todo_id: *const c_char,
    is_completed: i32,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let tid = match unsafe { cstr_to_string(todo_id) } {
            Some(s) => s,
            None => return ffi_err("invalid todo_id"),
        };
        let id = match parse_uuid(&tid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        let completed = is_completed != 0;
        match ctx.todo.set_completed(id, completed) {
            Ok(todo) => ffi_ok(FfiTodoItem::from(&todo)),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_delete_todo(ctx: *mut Context, todo_id: *const c_char) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let tid = match unsafe { cstr_to_string(todo_id) } {
            Some(s) => s,
            None => return ffi_err("invalid todo_id"),
        };
        let id = match parse_uuid(&tid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        match ctx.todo.delete_todo(id) {
            Ok(()) => ffi_ok(serde_json::Value::Null),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_archive_completed(
    ctx: *mut Context,
    box_id: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let id_str = match unsafe { cstr_to_string(box_id) } {
            Some(s) => s,
            None => return ffi_err("invalid box_id"),
        };
        let id = match parse_uuid(&id_str) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        match ctx.todo.archive_completed(id) {
            Ok(count) => ffi_ok(count),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_restore_archived(
    ctx: *mut Context,
    todo_id: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let tid = match unsafe { cstr_to_string(todo_id) } {
            Some(s) => s,
            None => return ffi_err("invalid todo_id"),
        };
        let id = match parse_uuid(&tid) {
            Ok(u) => u,
            Err(e) => return ffi_err(&e.message),
        };
        match ctx.todo.restore_archived(id) {
            Ok(todo) => ffi_ok(FfiTodoItem::from(&todo)),
            Err(e) => ffi_err(&e.message),
        }
    })
}

// ---------------------------------------------------------------------------
// Update operations
// ---------------------------------------------------------------------------

#[no_mangle]
pub unsafe extern "C" fn wd_check_update(
    ctx: *mut Context,
    current_version: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let ver = unsafe { cstr_to_string(current_version) }.unwrap_or_else(|| "0.0.0".to_string());
        let runtime = match tokio::runtime::Runtime::new() {
            Ok(runtime) => runtime,
            Err(e) => return ffi_err(&format!("Failed to create async runtime: {e}")),
        };
        match runtime.block_on(ctx.update.check_for_update(&ver)) {
            Ok(result) => ffi_ok(result),
            Err(e) => ffi_err(&e.message),
        }
    })
}

#[no_mangle]
pub unsafe extern "C" fn wd_download_and_apply_update(
    ctx: *mut Context,
    download_url: *const c_char,
    expected_sha256_or_null: *const c_char,
) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let url = match unsafe { cstr_to_string(download_url) } {
            Some(value) => value,
            None => return ffi_err("invalid download_url"),
        };
        let expected_sha256 = if expected_sha256_or_null.is_null() {
            None
        } else {
            match unsafe { cstr_to_string(expected_sha256_or_null) } {
                Some(value) => Some(value),
                None => return ffi_err("invalid expected_sha256"),
            }
        };
        let runtime = match tokio::runtime::Runtime::new() {
            Ok(runtime) => runtime,
            Err(e) => return ffi_err(&format!("Failed to create async runtime: {e}")),
        };
        match runtime.block_on(
            ctx.update
                .download_and_apply_update(&url, expected_sha256.as_deref()),
        ) {
            Ok(result) => ffi_ok(result),
            Err(e) => ffi_err(&e.message),
        }
    })
}

/// Remove legacy `update.zip` / `updater.bat` residue from the application
/// directory. Returns the number of removed artifacts.
#[no_mangle]
pub unsafe extern "C" fn wd_cleanup_legacy_updater_artifacts(ctx: *mut Context) -> *mut c_char {
    ffi_catch(|| {
        // Validate the context is alive; the cleanup itself is static.
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        let _ = ctx;

        let app_directory = match std::env::current_exe()
            .ok()
            .and_then(|p| p.parent().map(|p| p.to_path_buf()))
        {
            Some(dir) => dir,
            None => return ffi_err("failed to resolve application directory"),
        };

        let removed_count = UpdateService::cleanup_legacy_updater_artifacts(&app_directory);
        ffi_ok(removed_count)
    })
}

/// Confirm the app started successfully after a self-update by writing the
/// startup-success marker (validated to live inside the temp update session).
#[no_mangle]
pub unsafe extern "C" fn wd_confirm_update_startup(ctx: *mut Context) -> *mut c_char {
    ffi_catch(|| {
        let ctx = match unsafe { ctx.as_ref() } {
            Some(c) => c,
            None => return ffi_err("null context"),
        };
        match ctx.update.confirm_update_startup() {
            Ok(confirmed) => ffi_ok(serde_json::Value::Bool(confirmed)),
            Err(e) => ffi_err(&e.message),
        }
    })
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CString;

    /// Helper: create a CString from a &str, leak the pointer (test only).
    fn make_cstr(s: &str) -> *const c_char {
        CString::new(s).unwrap().into_raw()
    }

    /// Helper: read back a *mut c_char as a String, then free it.
    unsafe fn read_and_free(ptr: *mut c_char) -> String {
        assert!(!ptr.is_null(), "expected non-null pointer");
        let s = CString::from_raw(ptr);
        s.to_string_lossy().into_owned()
    }

    #[test]
    fn test_init_and_dispose() {
        let dir = tempfile::tempdir().unwrap();
        let data_dir = make_cstr(dir.path().to_str().unwrap());
        let ctx = unsafe { wd_init(data_dir) };
        assert!(!ctx.is_null());
        unsafe { wd_dispose(ctx) };
        // Free the leaked CString
        unsafe {
            let _ = CString::from_raw(data_dir as *mut c_char);
        }
    }

    #[test]
    fn init_creates_schema_and_returns_box_dtos() {
        let dir = tempfile::tempdir().unwrap();
        let data_dir = CString::new(dir.path().to_str().unwrap()).unwrap();
        let ctx = unsafe { wd_init(data_dir.as_ptr()) };
        assert!(!ctx.is_null());

        let json = unsafe { read_and_free(wd_get_boxes(ctx)) };
        let response: serde_json::Value = serde_json::from_str(&json).unwrap();

        assert_eq!(response["ok"], true);
        let boxes = response["data"].as_array().unwrap();
        assert_eq!(boxes.len(), 2);
        assert!(boxes[0]["type"].is_number());
        assert!(boxes[0].get("box_type").is_none());

        unsafe { wd_dispose(ctx) };
    }

    #[test]
    fn import_returns_drawer_item_dto() {
        let dir = tempfile::tempdir().unwrap();
        let data_dir = CString::new(dir.path().join("data").to_str().unwrap()).unwrap();
        let ctx = unsafe { wd_init(data_dir.as_ptr()) };
        assert!(!ctx.is_null());

        let boxes_json = unsafe { read_and_free(wd_get_boxes(ctx)) };
        let boxes_response: serde_json::Value = serde_json::from_str(&boxes_json).unwrap();
        let normal_box_id = boxes_response["data"]
            .as_array()
            .unwrap()
            .iter()
            .find(|b| b["type"] == 0)
            .unwrap()["id"]
            .as_str()
            .unwrap()
            .to_string();

        let source_path = dir.path().join("source.txt");
        std::fs::write(&source_path, "data").unwrap();
        let box_id = CString::new(normal_box_id).unwrap();
        let source = CString::new(source_path.to_str().unwrap()).unwrap();
        let json = unsafe {
            read_and_free(wd_import_path(
                ctx,
                box_id.as_ptr(),
                source.as_ptr(),
                -1,
                -1,
            ))
        };
        let response: serde_json::Value = serde_json::from_str(&json).unwrap();

        assert_eq!(response["ok"], true);
        assert!(response["data"]["item_kind"].is_number());

        unsafe { wd_dispose(ctx) };
    }

    #[test]
    fn test_null_context_returns_error() {
        let result = unsafe { wd_get_boxes(std::ptr::null_mut()) };
        let json = unsafe { read_and_free(result) };
        assert!(json.contains("\"ok\":false"));
        assert!(json.contains("null context"));
    }

    #[test]
    fn test_create_box_invalid_type() {
        let dir = tempfile::tempdir().unwrap();
        let data_dir = make_cstr(dir.path().to_str().unwrap());
        let ctx = unsafe { wd_init(data_dir) };

        let name = make_cstr("Test Box");
        let result = unsafe { wd_create_box(ctx, name, 99) };
        let json = unsafe { read_and_free(result) };
        assert!(json.contains("\"ok\":false"));
        assert!(json.contains("invalid box type"));

        unsafe { wd_dispose(ctx) };
        unsafe {
            let _ = CString::from_raw(data_dir as *mut c_char);
        }
        unsafe {
            let _ = CString::from_raw(name as *mut c_char);
        }
    }

    #[test]
    fn test_free_string_null() {
        // Should not panic
        unsafe {
            wd_free_string(std::ptr::null_mut());
        }
    }

    #[test]
    fn test_get_todos_null_context() {
        let box_id = CString::new("fake-id").unwrap();
        let result = unsafe { wd_get_todos(std::ptr::null_mut(), box_id.as_ptr()) };
        let json = unsafe { read_and_free(result) };
        assert!(json.contains("\"ok\":false"));
    }

    #[test]
    fn test_check_update_null_context() {
        let version = CString::new("1.0.0").unwrap();
        let result = unsafe { wd_check_update(std::ptr::null_mut(), version.as_ptr()) };
        let json = unsafe { read_and_free(result) };
        assert!(json.contains("\"ok\":false"));
    }

    #[test]
    fn test_cleanup_legacy_updater_artifacts() {
        let dir = tempfile::tempdir().unwrap();
        let data_dir = make_cstr(dir.path().to_str().unwrap());
        let ctx = unsafe { wd_init(data_dir) };
        assert!(!ctx.is_null());

        let json = unsafe { read_and_free(wd_cleanup_legacy_updater_artifacts(ctx)) };
        let response: serde_json::Value = serde_json::from_str(&json).unwrap();
        // No residue in a fresh temp workspace; ok with zero removed.
        assert_eq!(response["ok"], true);
        assert!(response["data"].is_number());

        unsafe { wd_dispose(ctx) };
        unsafe {
            let _ = CString::from_raw(data_dir as *mut c_char);
        }
    }

    #[test]
    fn test_cleanup_legacy_updater_artifacts_null_context() {
        let result = unsafe { wd_cleanup_legacy_updater_artifacts(std::ptr::null_mut()) };
        let json = unsafe { read_and_free(result) };
        assert!(json.contains("\"ok\":false"));
        assert!(json.contains("null context"));
    }
}
