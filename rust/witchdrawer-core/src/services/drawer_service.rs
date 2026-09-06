//! Main drawer service — orchestrates boxes, items, file moves, and restores.
//!
//! `DrawerRepository` is expected to live in `crate::storage` and expose these
//! methods (all returning `AppResult<T>` and taking `&self`):
//!
//! ```text
//! fn initialize(&self) -> AppResult<()>
//! fn get_boxes(&self) -> AppResult<Vec<DrawerBox>>
//! fn get_box(&self, id: Uuid) -> AppResult<Option<DrawerBox>>
//! fn add_box(&self, b: &Box) -> AppResult<()>
//! fn update_box_sort_orders(&self, ids: &[Uuid]) -> AppResult<()>
//! fn update_box_name(&self, id: Uuid, name: &str) -> AppResult<()>
//! fn remove_box(&self, id: Uuid) -> AppResult<()>
//! fn get_next_box_sort_order(&self) -> AppResult<i32>
//! fn get_items(&self, box_id: Option<Uuid>) -> AppResult<Vec<DrawerItem>>
//! fn get_item(&self, id: Uuid) -> AppResult<Option<DrawerItem>>
//! fn add_item(&self, item: &DrawerItem) -> AppResult<()>
//! fn move_item_to_box(&self, item: &DrawerItem, target_box_id: Uuid,
//!     display_name: &str, source_path: Option<&str>, stored_path: Option<&str>,
//!     sort_order: i32, grid_column: Option<i32>, grid_row: Option<i32>) -> AppResult<()>
//! fn update_item_grid_position(&self, id: Uuid,
//!     grid_column: Option<i32>, grid_row: Option<i32>) -> AppResult<()>
//! fn remove_item(&self, id: Uuid) -> AppResult<()>
//! fn search_items(&self, query: &str, limit: i32) -> AppResult<Vec<DrawerItem>>
//! fn get_setting(&self, key: &str) -> AppResult<Option<String>>
//! fn set_setting(&self, key: &str, value: &str) -> AppResult<()>
//! ```

use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};

use chrono::Utc;
use uuid::Uuid;

use crate::models::{
    AppError, AppResult, BoxDeleteResult, BoxType, DrawerBox, DrawerItem, ItemDeleteResult,
    ItemKind,
};
use crate::storage::DrawerRepository;

use super::app_paths::AppPaths;
use super::file_name_service;
use super::file_ops;
use super::path_safety;

// ---------------------------------------------------------------------------
// Restore plan (private)
// ---------------------------------------------------------------------------

#[derive(Debug)]
struct RestorePlan {
    source_path: PathBuf,
    target_path: PathBuf,
    is_directory: bool,
    restored_to_original: bool,
    restored_to_desktop: bool,
}

/// Compare two filesystem paths for equality, ignoring trailing separators and
/// case on Windows. Mirrors the C# `Path.GetFullPath(...)` + `OrdinalIgnoreCase`
/// comparison used by `RepairStoredPathsAsync`.
fn path_eq(a: &str, b: &str) -> bool {
    norm_fs_path(a).eq_ignore_ascii_case(&norm_fs_path(b))
}

/// Normalise a path for comparison: forward slashes to backslashes, trim
/// trailing separators. Does not resolve `..` segments (storage paths do not
/// contain them).
fn norm_fs_path(p: &str) -> String {
    let mut s = p.replace('/', "\\");
    while s.ends_with('\\') {
        s.pop();
    }
    s
}

// ---------------------------------------------------------------------------
// DrawerService
// ---------------------------------------------------------------------------

pub struct DrawerService {
    paths: AppPaths,
    repository: DrawerRepository,
}

impl DrawerService {
    pub fn new(paths: AppPaths, repository: DrawerRepository) -> Self {
        Self { paths, repository }
    }

    // -- Initialisation -----------------------------------------------------

    pub fn initialize(&self) -> AppResult<()> {
        self.paths.ensure_created()?;
        self.repository.initialize()?;
        self.repair_stored_paths()?;
        self.ensure_default_boxes()
    }

    // -- Boxes --------------------------------------------------------------

    pub fn get_boxes(&self) -> AppResult<Vec<DrawerBox>> {
        self.repository.get_boxes()
    }

    pub fn create_box(&self, name: &str, box_type: BoxType) -> AppResult<DrawerBox> {
        let trimmed = name.trim();
        if trimmed.is_empty() {
            return Err(AppError::invalid_arg("Box name cannot be empty."));
        }

        let id = Uuid::new_v4();
        let now = Utc::now();

        let storage_path = if box_type == BoxType::Normal
            || box_type == BoxType::Pixel
            || box_type == BoxType::Drawer
        {
            let p = self
                .paths
                .boxes_directory()
                .join(format!("{:?}", id).replace('-', ""));
            fs::create_dir_all(&p)?;
            Some(p.to_string_lossy().to_string())
        } else {
            None
        };

        let sort_order = self.repository.get_next_box_sort_order()?;

        let b = DrawerBox {
            id,
            name: trimmed.to_string(),
            box_type,
            storage_path,
            sort_order,
            created_at: now,
            updated_at: now,
        };

        self.repository.add_box(&b)?;
        Ok(b)
    }

    pub fn rename_box(&self, box_id: Uuid, new_name: &str) -> AppResult<()> {
        let trimmed = new_name.trim();
        if trimmed.is_empty() {
            return Err(AppError::invalid_arg("Box name cannot be empty."));
        }
        if self.repository.get_box(box_id)?.is_none() {
            return Err(AppError::not_found("Box does not exist."));
        }
        self.repository.update_box_name(box_id, trimmed)
    }

    pub fn reorder_boxes(&self, ordered_ids: &[Uuid]) -> AppResult<()> {
        if ordered_ids.is_empty() {
            return Err(AppError::invalid_arg(
                "Box order cannot contain duplicate ids.",
            ));
        }

        // Check for duplicates.
        let mut seen = HashSet::new();
        for id in ordered_ids {
            if !seen.insert(id) {
                return Err(AppError::invalid_arg(
                    "Box order cannot contain duplicate ids.",
                ));
            }
        }

        let existing = self.repository.get_boxes()?;
        let existing_ids: HashSet<Uuid> = existing.iter().map(|b| b.id).collect();

        if ordered_ids.len() != existing_ids.len()
            || ordered_ids.iter().any(|id| !existing_ids.contains(id))
        {
            return Err(AppError::invalid_arg(
                "Box order must contain every existing box exactly once.",
            ));
        }

        self.repository.update_box_sort_orders(ordered_ids)
    }

    // -- Items --------------------------------------------------------------

    pub fn get_items(&self, box_id: Uuid) -> AppResult<Vec<DrawerItem>> {
        self.prune_missing_stored_items(Some(box_id))?;
        self.repository.get_items(Some(box_id))
    }

    pub fn get_all_items(&self) -> AppResult<Vec<DrawerItem>> {
        self.prune_missing_stored_items(None)?;
        self.repository.get_items(None)
    }

    pub fn search_items(&self, query: &str, limit: i32) -> AppResult<Vec<DrawerItem>> {
        self.prune_missing_stored_items(None)?;
        self.repository.search_items(query.trim(), limit)
    }

    pub fn update_item_grid_position(
        &self,
        item_id: Uuid,
        grid_column: Option<i32>,
        grid_row: Option<i32>,
    ) -> AppResult<()> {
        self.repository
            .update_item_grid_position(item_id, grid_column, grid_row)
    }

    /// Batch-update multiple items' grid positions in one transaction.
    /// Mirrors the C# `DrawerService.UpdateItemGridPositionsAsync`.
    pub fn update_item_grid_positions(&self, positions: &[(Uuid, i32, i32)]) -> AppResult<()> {
        self.repository.update_item_grid_positions(positions)
    }

    // -- Import -------------------------------------------------------------

    pub fn import_path(
        &self,
        box_id: Uuid,
        source_path: &str,
        grid_column: Option<i32>,
        grid_row: Option<i32>,
    ) -> AppResult<DrawerItem> {
        let b = self
            .repository
            .get_box(box_id)?
            .ok_or_else(|| AppError::not_found("Box does not exist."))?;

        if b.box_type == BoxType::Todo {
            return Err(AppError::invalid_arg("Todo boxes do not accept files."));
        }

        let full_source = path_safety::get_full_existing_path(source_path)?;
        let is_directory = full_source.is_dir();
        let item_kind = if is_directory {
            ItemKind::Directory
        } else {
            ItemKind::File
        };

        let display_name = full_source
            .file_name()
            .unwrap_or_default()
            .to_string_lossy()
            .to_string();

        let sort_order = self.repository.get_next_item_sort_order(box_id)?;
        let now = Utc::now();

        if b.box_type == BoxType::Mapping {
            let item = DrawerItem {
                id: Uuid::new_v4(),
                box_id,
                display_name,
                item_kind,
                source_path: Some(full_source.to_string_lossy().to_string()),
                stored_path: None,
                sort_order,
                created_at: now,
                updated_at: now,
                grid_column,
                grid_row,
            };
            self.repository.add_item(&item)?;
            return Ok(item);
        }

        // Normal / Pixel box — move the file into box storage.
        let storage_root = b
            .storage_path
            .as_ref()
            .map(PathBuf::from)
            .unwrap_or_else(|| {
                self.paths
                    .boxes_directory()
                    .join(format!("{:?}", box_id).replace('-', ""))
            });
        path_safety::ensure_child_path(&self.paths.boxes_directory(), &storage_root)?;
        fs::create_dir_all(&storage_root)?;

        let target_path = file_name_service::get_unique_destination_path(
            &storage_root,
            &display_name,
            is_directory,
        )?;
        path_safety::ensure_child_path(&storage_root, &target_path)?;

        file_ops::move_file(&full_source, &target_path, is_directory)?;

        let item = DrawerItem {
            id: Uuid::new_v4(),
            box_id,
            display_name: target_path
                .file_name()
                .unwrap_or_default()
                .to_string_lossy()
                .to_string(),
            item_kind,
            source_path: Some(full_source.to_string_lossy().to_string()),
            stored_path: Some(target_path.to_string_lossy().to_string()),
            sort_order,
            created_at: now,
            updated_at: now,
            grid_column,
            grid_row,
        };

        if let Err(e) = self.repository.add_item(&item) {
            // Best-effort: move the file back.
            Self::try_compensate_move(&target_path, &full_source, is_directory);
            return Err(e);
        }

        Ok(item)
    }

    // -- Move item between boxes --------------------------------------------

    pub fn move_item_to_box(
        &self,
        item_id: Uuid,
        target_box_id: Uuid,
        grid_column: Option<i32>,
        grid_row: Option<i32>,
    ) -> AppResult<()> {
        let item = self
            .repository
            .get_item(item_id)?
            .ok_or_else(|| AppError::not_found("Item does not exist."))?;

        let source_box = self
            .repository
            .get_box(item.box_id)?
            .ok_or_else(|| AppError::not_found("Source box does not exist."))?;

        let target_box = self
            .repository
            .get_box(target_box_id)?
            .ok_or_else(|| AppError::not_found("Target box does not exist."))?;

        if source_box.box_type == BoxType::Todo || target_box.box_type == BoxType::Todo {
            return Err(AppError::invalid_arg(
                "Files cannot be moved into or out of a todo box.",
            ));
        }

        // Same box — just update grid position.
        if item.box_id == target_box_id {
            return self
                .repository
                .update_item_grid_position(item_id, grid_column, grid_row);
        }

        let target_sort = self.repository.get_next_item_sort_order(target_box_id)?;
        let source_path = item.source_path.clone();
        let mut display_name = item.display_name.clone();
        let is_directory = item.item_kind == ItemKind::Directory;

        let stored_path: Option<String> = if target_box.box_type == BoxType::Mapping {
            // Stored items cannot move into a mapping box.
            if item
                .stored_path
                .as_deref()
                .map(|s| !s.trim().is_empty())
                .unwrap_or(false)
            {
                return Err(AppError::invalid_arg(
                    "Stored items cannot be moved into a mapping box.",
                ));
            }
            None
        } else {
            // Target is Normal/Pixel.
            if source_box.box_type == BoxType::Mapping {
                return Err(AppError::invalid_arg(
                    "Mapping references cannot be moved into a storage box.",
                ));
            }

            let effective = item
                .effective_path()
                .ok_or_else(|| AppError::invalid_arg("Item has no file path."))?;

            let full_source = path_safety::get_full_existing_path(effective)?;

            // If the item is stored, validate it's under Boxes.
            if item
                .stored_path
                .as_deref()
                .map(|s| !s.trim().is_empty())
                .unwrap_or(false)
            {
                path_safety::ensure_child_path(&self.paths.boxes_directory(), &full_source)?;
            }

            let storage_root = target_box
                .storage_path
                .as_ref()
                .map(PathBuf::from)
                .unwrap_or_else(|| {
                    self.paths
                        .boxes_directory()
                        .join(format!("{:?}", target_box_id).replace('-', ""))
                });
            path_safety::ensure_child_path(&self.paths.boxes_directory(), &storage_root)?;
            fs::create_dir_all(&storage_root)?;

            let target_path = file_name_service::get_unique_destination_path(
                &storage_root,
                &display_name,
                is_directory,
            )?;
            path_safety::ensure_child_path(&storage_root, &target_path)?;

            file_ops::move_file(&full_source, &target_path, is_directory)?;

            display_name = target_path
                .file_name()
                .unwrap_or_default()
                .to_string_lossy()
                .to_string();
            let stored_path = Some(target_path.to_string_lossy().to_string());

            let move_result = self.repository.move_item_to_box(
                &item,
                target_box_id,
                &display_name,
                source_path.as_deref(),
                stored_path.as_deref(),
                target_sort,
                grid_column,
                grid_row,
            );

            if let Err(e) = move_result {
                Self::try_compensate_move(
                    Path::new(stored_path.as_deref().unwrap_or("")),
                    &full_source,
                    is_directory,
                );
                return Err(e);
            }

            return Ok(());
        };

        // Mapping target path.
        self.repository.move_item_to_box(
            &item,
            target_box_id,
            &display_name,
            source_path.as_deref(),
            stored_path.as_deref(),
            target_sort,
            grid_column,
            grid_row,
        )
    }

    // -- Export -------------------------------------------------------------

    pub fn export_item_to_directory(
        &self,
        item_id: Uuid,
        target_directory: &str,
    ) -> AppResult<String> {
        let item = self
            .repository
            .get_item(item_id)?
            .ok_or_else(|| AppError::not_found("Item does not exist."))?;

        let stored = item
            .stored_path
            .as_deref()
            .filter(|s| !s.trim().is_empty())
            .ok_or_else(|| AppError::invalid_arg("Only stored items can be exported."))?;

        let source = path_safety::get_full_existing_path(stored)?;
        path_safety::ensure_child_path(&self.paths.boxes_directory(), &source)?;

        let full_target_dir = Path::new(target_directory)
            .canonicalize()
            .unwrap_or_else(|_| {
                let _ = fs::create_dir_all(target_directory);
                PathBuf::from(target_directory)
            });
        fs::create_dir_all(&full_target_dir)?;

        let display = if item.display_name.trim().is_empty() {
            source
                .file_name()
                .unwrap_or_default()
                .to_string_lossy()
                .to_string()
        } else {
            item.display_name.clone()
        };

        let is_directory = item.item_kind == ItemKind::Directory;
        let target = file_name_service::get_unique_destination_path(
            &full_target_dir,
            &display,
            is_directory,
        )?;
        path_safety::ensure_child_path(&full_target_dir, &target)?;

        file_ops::move_file(&source, &target, is_directory)?;

        if let Err(e) = self.repository.remove_item(item_id) {
            Self::try_compensate_move(&target, &source, is_directory);
            return Err(e);
        }

        Ok(target.to_string_lossy().to_string())
    }

    // -- Delete -------------------------------------------------------------

    pub fn delete_item(&self, item_id: Uuid) -> AppResult<ItemDeleteResult> {
        let item = self
            .repository
            .get_item(item_id)?
            .ok_or_else(|| AppError::not_found("Item does not exist."))?;

        // Mapping items (no stored path) — just remove the DB record.
        if item
            .stored_path
            .as_deref()
            .map(|s| s.trim().is_empty())
            .unwrap_or(true)
        {
            self.repository.remove_item(item_id)?;
            return Ok(ItemDeleteResult {
                item_id: item.id.to_string(),
                display_name: item.display_name.clone(),
                was_stored_item: false,
                restored_path: None,
                restored_to_original: false,
                restored_to_desktop: false,
                status_message: format!(
                    "\u{5DF2}\u{79FB}\u{9664}\u{5F15}\u{7528} {}",
                    item.display_name
                ),
            });
        }

        let restore = self.restore_stored_item(&item, None)?;

        if let Err(e) = self.repository.remove_item(item_id) {
            // Best effort: put the file back.
            if let (Some(ref sp), Some(ref rp)) = (item.stored_path, &restore.restored_path) {
                if !sp.trim().is_empty() && !rp.is_empty() {
                    let is_dir = item.item_kind == ItemKind::Directory;
                    Self::try_compensate_move(Path::new(rp), Path::new(sp), is_dir);
                }
            }
            return Err(e);
        }

        Ok(restore)
    }

    pub fn delete_box(&self, box_id: Uuid) -> AppResult<BoxDeleteResult> {
        let b = self
            .repository
            .get_box(box_id)?
            .ok_or_else(|| AppError::not_found("Box does not exist."))?;

        // Mapping / Todo boxes — just remove the record.
        if b.box_type == BoxType::Mapping || b.box_type == BoxType::Todo {
            self.repository.remove_box(box_id)?;
            return Ok(BoxDeleteResult {
                box_id: b.id.to_string(),
                box_name: b.name.clone(),
                box_type: b.box_type as i32,
                box_removed: true,
                restored_count: 0,
                failed_count: 0,
                failures: vec![],
                status_message: if b.box_type == BoxType::Mapping {
                    format!(
                        "\u{5DF2}\u{5220}\u{9664} {}\u{FF0C}\u{5F15}\u{7528}\u{5DF2}\u{79FB}\u{9664}",
                        b.name
                    )
                } else {
                    format!(
                        "\u{5DF2}\u{5220}\u{9664} {}\u{FF0C}\u{5F85}\u{529E}\u{4E8B}\u{9879}\u{5DF2}\u{6E05}\u{9664}",
                        b.name
                    )
                },
            });
        }

        // Normal / Pixel boxes — restore each stored item.
        let items = self.repository.get_items(Some(box_id))?;
        let mut reserved: HashSet<String> = HashSet::new();
        let mut restored_count: i32 = 0;
        let mut failures: Vec<String> = vec![];

        for item in &items {
            if item
                .stored_path
                .as_deref()
                .map(|s| s.trim().is_empty())
                .unwrap_or(true)
            {
                if let Err(e) = self.repository.remove_item(item.id) {
                    failures.push(format!("{}: {}", item.display_name, e));
                }
                continue;
            }

            match self.restore_stored_item(item, Some(&mut reserved)) {
                Ok(_) => {
                    if let Err(e) = self.repository.remove_item(item.id) {
                        failures.push(format!("{}: {}", item.display_name, e));
                    } else {
                        restored_count += 1;
                    }
                }
                Err(e) => {
                    failures.push(format!("{}: {}", item.display_name, e));
                }
            }
        }

        if !failures.is_empty() {
            return Ok(BoxDeleteResult {
                box_id: b.id.to_string(),
                box_name: b.name.clone(),
                box_type: b.box_type as i32,
                box_removed: false,
                restored_count,
                failed_count: failures.len() as i32,
                failures,
                status_message:
                    "\u{5220}\u{9664}\u{672A}\u{5B8C}\u{6210}\u{FF1C}\u{5DF2}\u{4FDD}\u{7559}"
                        .to_string(),
            });
        }

        self.repository.remove_box(box_id)?;
        self.try_delete_box_storage_directory(&b);

        Ok(BoxDeleteResult {
            box_id: b.id.to_string(),
            box_name: b.name.clone(),
            box_type: b.box_type as i32,
            box_removed: true,
            restored_count,
            failed_count: 0,
            failures: vec![],
            status_message: if restored_count > 0 {
                format!(
                    "\u{5DF2}\u{5220}\u{9664} {}\u{FF0C}\u{5DF2}\u{539F}\u{8FD8} {} \u{9879}",
                    b.name, restored_count
                )
            } else {
                format!("\u{5DF2}\u{5220}\u{9664} {}", b.name)
            },
        })
    }

    // -- Settings -----------------------------------------------------------

    pub fn get_setting(&self, key: &str) -> AppResult<Option<String>> {
        self.repository.get_setting(key)
    }

    pub fn set_setting(&self, key: &str, value: &str) -> AppResult<()> {
        self.repository.set_setting(key, value)
    }

    pub fn delete_setting(&self, key: &str) -> AppResult<bool> {
        self.repository.delete_setting(key)
    }

    pub fn checkpoint(&self) -> AppResult<()> {
        self.repository.checkpoint()
    }

    // -- Open item ----------------------------------------------------------

    pub fn open_item(
        &self,
        item_id: Uuid,
        launcher: &dyn Fn(&str) -> AppResult<()>,
    ) -> AppResult<()> {
        let item = self
            .repository
            .get_item(item_id)?
            .ok_or_else(|| AppError::not_found("Item does not exist."))?;

        let path = item
            .effective_path()
            .ok_or_else(|| AppError::invalid_arg("Item has no file path."))?;

        launcher(path)
    }

    // =======================================================================
    // Private helpers
    // =======================================================================

    /// Remove items whose stored file no longer exists on disk.
    fn prune_missing_stored_items(&self, box_id: Option<Uuid>) -> AppResult<()> {
        let items = self.repository.get_items(box_id)?;
        let missing: Vec<Uuid> = items
            .iter()
            .filter(|i| {
                i.stored_path
                    .as_deref()
                    .map(|sp| !sp.trim().is_empty() && !Path::new(sp).exists())
                    .unwrap_or(false)
            })
            .map(|i| i.id)
            .collect();

        for id in missing {
            let _ = self.repository.remove_item(id);
        }
        Ok(())
    }

    /// Create the default "普通收纳盒" and "映射收纳盒" when the DB is empty.
    fn ensure_default_boxes(&self) -> AppResult<()> {
        let boxes = self.repository.get_boxes()?;
        if !boxes.is_empty() {
            return Ok(());
        }
        self.create_box("\u{666E}\u{901A}\u{6536}\u{7EB3}\u{76D2}", BoxType::Normal)?;
        self.create_box("\u{6620}\u{5C04}\u{6536}\u{7EB3}\u{76D2}", BoxType::Mapping)?;
        Ok(())
    }

    /// After a data-directory move, repoint box storage paths and item stored
    /// paths to the new `BoxesDirectory`. Mirrors the C# `RepairStoredPathsAsync`.
    fn repair_stored_paths(&self) -> AppResult<()> {
        let boxes = self.repository.get_boxes()?;
        let boxes_dir = self.paths.boxes_directory();

        for b in boxes {
            if !matches!(
                b.box_type,
                BoxType::Normal | BoxType::Pixel | BoxType::Drawer
            ) {
                continue;
            }
            let expected = boxes_dir.join(format!("{:?}", b.id).replace('-', ""));
            if !expected.exists() {
                fs::create_dir_all(&expected)?;
            }
            let expected_str = expected.to_string_lossy().to_string();
            let current_str = b
                .storage_path
                .clone()
                .unwrap_or_else(|| expected_str.clone());
            if !path_eq(&current_str, &expected_str) {
                self.repository
                    .update_box_storage_path(b.id, &expected_str)?;
            }

            let items = self.repository.get_items(Some(b.id))?;
            for item in items {
                let stored = match &item.stored_path {
                    Some(s) if !s.trim().is_empty() => s.clone(),
                    _ => continue,
                };
                let name = Path::new(&stored)
                    .file_name()
                    .map(|n| n.to_string_lossy().to_string())
                    .unwrap_or_default();
                if name.is_empty() {
                    continue;
                }
                let expected_item_str = expected.join(&name).to_string_lossy().to_string();
                if !Path::new(&expected_item_str).exists() || path_eq(&stored, &expected_item_str) {
                    continue;
                }
                self.repository
                    .update_item_stored_path(item.id, &expected_item_str)?;
            }
        }
        Ok(())
    }

    /// Best-effort compensation: move a file back if a DB write fails.
    fn try_compensate_move(moved: &Path, original: &Path, is_dir: bool) {
        let exists = if is_dir {
            moved.is_dir()
        } else {
            moved.is_file()
        };
        if exists {
            let _ = file_ops::move_file(moved, original, is_dir);
        }
    }

    /// Restore a stored item to its original location or the desktop.
    fn restore_stored_item(
        &self,
        item: &DrawerItem,
        reserved: Option<&mut HashSet<String>>,
    ) -> AppResult<ItemDeleteResult> {
        let plan = self.create_restore_plan(item, reserved)?;
        file_ops::move_file(&plan.source_path, &plan.target_path, plan.is_directory)?;

        Ok(ItemDeleteResult {
            item_id: item.id.to_string(),
            display_name: item.display_name.clone(),
            was_stored_item: true,
            restored_path: Some(plan.target_path.to_string_lossy().to_string()),
            restored_to_original: plan.restored_to_original,
            restored_to_desktop: plan.restored_to_desktop,
            status_message: if plan.restored_to_desktop {
                format!(
                    "\u{5DF2}\u{539F}\u{8FD8} {} \u{5230}\u{684C}\u{9762}\u{FF08}\u{539F}\u{4F4D}\u{7F6E}\u{4E0D}\u{53EF}\u{7528}\u{FF09}",
                    item.display_name
                )
            } else {
                format!(
                    "\u{5DF2}\u{539F}\u{8FD8} {} \u{5230}\u{539F}\u{4F4D}\u{7F6E}",
                    item.display_name
                )
            },
        })
    }

    fn create_restore_plan(
        &self,
        item: &DrawerItem,
        reserved: Option<&mut HashSet<String>>,
    ) -> AppResult<RestorePlan> {
        let stored = item
            .stored_path
            .as_deref()
            .filter(|s| !s.trim().is_empty())
            .ok_or_else(|| {
                AppError::invalid_arg("Mapping items do not have stored files to restore.")
            })?;

        let stored_path = path_safety::get_full_existing_path(stored)?;
        path_safety::ensure_child_path(&self.paths.boxes_directory(), &stored_path)?;

        let is_directory = stored_path.is_dir();
        let original_name = Self::resolve_restore_file_name(item, &stored_path);

        // Try the original source directory first.
        if let Some(orig_dir) =
            Self::try_get_existing_original_directory(item.source_path.as_deref())
        {
            let target = Self::get_reserved_unique_destination_path(
                &orig_dir,
                &original_name,
                is_directory,
                reserved,
            )?;
            path_safety::ensure_child_path(&orig_dir, &target)?;
            return Ok(RestorePlan {
                source_path: stored_path,
                target_path: target,
                is_directory,
                restored_to_original: true,
                restored_to_desktop: false,
            });
        }

        // Fallback: desktop.
        let desktop = Self::get_desktop_directory()?;
        fs::create_dir_all(&desktop)?;
        let target = Self::get_reserved_unique_destination_path(
            &desktop,
            &original_name,
            is_directory,
            reserved,
        )?;
        path_safety::ensure_child_path(&desktop, &target)?;

        Ok(RestorePlan {
            source_path: stored_path,
            target_path: target,
            is_directory,
            restored_to_original: false,
            restored_to_desktop: true,
        })
    }

    /// Determine the file name to use when restoring.
    fn resolve_restore_file_name(item: &DrawerItem, stored_path: &Path) -> String {
        // Try source path first.
        if let Some(ref sp) = item.source_path {
            if let Ok(full) = Path::new(sp).canonicalize() {
                if let Some(name) = full.file_name() {
                    let n = name.to_string_lossy().to_string();
                    if !n.is_empty() {
                        return n;
                    }
                }
            } else {
                // Even if canonicalize fails, try file_name on the raw path.
                if let Some(name) = Path::new(sp).file_name() {
                    let n = name.to_string_lossy().to_string();
                    if !n.is_empty() {
                        return n;
                    }
                }
            }
        }

        // Then display name.
        if !item.display_name.trim().is_empty() {
            return item.display_name.clone();
        }

        // Finally stored path.
        if let Some(name) = stored_path.file_name() {
            let n = name.to_string_lossy().to_string();
            if !n.is_empty() {
                return n;
            }
        }

        // Absolute fallback.
        "restored_file".to_string()
    }

    /// Check if the original source directory still exists.
    fn try_get_existing_original_directory(source_path: Option<&str>) -> Option<PathBuf> {
        let sp = source_path?;
        let parent = Path::new(sp).parent()?.canonicalize().ok()?;
        if parent.is_dir() {
            Some(parent)
        } else {
            None
        }
    }

    /// Resolve the user's desktop directory.
    fn get_desktop_directory() -> AppResult<PathBuf> {
        // Try common env vars.
        if let Ok(desktop) = std::env::var("USERPROFILE") {
            let p = PathBuf::from(desktop).join("Desktop");
            if p.is_dir() {
                return Ok(p);
            }
        }
        if let Ok(home) = std::env::var("HOME") {
            let p = PathBuf::from(home).join("Desktop");
            if p.is_dir() {
                return Ok(p);
            }
        }
        // XDG fallback.
        if let Ok(data) = std::env::var("XDG_DATA_HOME") {
            let p = PathBuf::from(data).join("Desktop");
            if p.is_dir() {
                return Ok(p);
            }
        }

        Err(AppError::io_error(
            "Desktop directory is not available for restore fallback.",
        ))
    }

    /// Generate a unique path inside `directory`, also checking `reserved`.
    fn get_reserved_unique_destination_path(
        directory: &Path,
        file_name: &str,
        is_dir: bool,
        mut reserved: Option<&mut HashSet<String>>,
    ) -> AppResult<PathBuf> {
        let target = file_name_service::get_unique_destination_path(directory, file_name, is_dir)?;

        if let Some(ref mut res) = reserved {
            let normalized = target.canonicalize().unwrap_or_else(|_| target.clone());
            let key = normalized.to_string_lossy().to_string();
            if res.insert(key) {
                return Ok(target);
            }

            // Collision in reserved set — keep trying.
            let stem = if is_dir {
                file_name.to_string()
            } else {
                Path::new(file_name)
                    .file_stem()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_string()
            };
            let ext = if is_dir {
                String::new()
            } else {
                Path::new(file_name)
                    .extension()
                    .map(|e| format!(".{}", e.to_string_lossy()))
                    .unwrap_or_default()
            };

            for idx in 1..10_000 {
                let candidate = directory.join(format!("{} ({}){}", stem, idx, ext));
                let exists = if is_dir {
                    candidate.is_dir()
                } else {
                    candidate.is_file()
                };
                let norm = candidate
                    .canonicalize()
                    .unwrap_or_else(|_| candidate.clone());
                let key = norm.to_string_lossy().to_string();
                if !exists && res.insert(key) {
                    return Ok(candidate);
                }
            }

            return Err(AppError::io_error(format!(
                "Could not find a unique destination for {}.",
                file_name
            )));
        }

        Ok(target)
    }

    /// Best-effort: delete the box storage directory if it is empty.
    fn try_delete_box_storage_directory(&self, b: &DrawerBox) {
        let storage = b
            .storage_path
            .as_ref()
            .map(PathBuf::from)
            .unwrap_or_else(|| {
                self.paths
                    .boxes_directory()
                    .join(format!("{:?}", b.id).replace('-', ""))
            });

        if let Ok(full) = storage.canonicalize() {
            if path_safety::ensure_child_path(&self.paths.boxes_directory(), &full).is_err() {
                return;
            }
            if full.is_dir() {
                // Only delete if empty.
                if fs::read_dir(&full)
                    .map(|mut e| e.next().is_none())
                    .unwrap_or(false)
                {
                    let _ = fs::remove_dir(&full);
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    fn create_service(temp: &TempDir) -> (DrawerService, DrawerRepository, AppPaths) {
        let data_root = temp.path().join("app-data");
        let paths = AppPaths::new(data_root);
        let repository = DrawerRepository::new(paths.database_path().to_string_lossy().to_string());
        let service = DrawerService::new(paths.clone(), repository.clone());
        service.initialize().unwrap();
        (service, repository, paths)
    }

    #[test]
    fn normal_box_import_and_delete_restores_to_original_directory() {
        let temp = TempDir::new().unwrap();
        let (service, _, _) = create_service(&temp);
        let normal_box = service
            .get_boxes()
            .unwrap()
            .into_iter()
            .find(|b| b.box_type == BoxType::Normal)
            .unwrap();

        let source_directory = temp.path().join("source");
        fs::create_dir_all(&source_directory).unwrap();
        let source_path = source_directory.join("notes.txt");
        fs::write(&source_path, "important").unwrap();

        let imported = service
            .import_path(normal_box.id, source_path.to_str().unwrap(), None, None)
            .unwrap();

        assert!(!source_path.exists());
        assert!(Path::new(imported.stored_path.as_deref().unwrap()).is_file());

        let result = service.delete_item(imported.id).unwrap();

        assert!(result.restored_to_original);
        assert!(!result.restored_to_desktop);
        assert_eq!(
            Path::new(result.restored_path.as_deref().unwrap())
                .canonicalize()
                .unwrap(),
            source_path.canonicalize().unwrap()
        );
        assert_eq!(fs::read_to_string(&source_path).unwrap(), "important");
    }

    #[test]
    fn drawer_box_uses_stored_file_safety_and_restores_on_delete() {
        let temp = TempDir::new().unwrap();
        let (service, _, _) = create_service(&temp);

        let source_directory = temp.path().join("drawer-source");
        fs::create_dir_all(&source_directory).unwrap();
        let source_path = source_directory.join("drawer-item.txt");
        fs::write(&source_path, "hello").unwrap();

        let drawer_box = service.create_box("抽屉盒 1", BoxType::Drawer).unwrap();
        let imported = service
            .import_path(drawer_box.id, source_path.to_str().unwrap(), None, None)
            .unwrap();

        assert!(drawer_box.storage_path.is_some());
        assert!(imported.stored_path.is_some());
        assert!(!source_path.exists());
        assert!(Path::new(imported.stored_path.as_deref().unwrap()).is_file());
        let boxes_root = temp
            .path()
            .join("app-data")
            .join("Boxes")
            .canonicalize()
            .unwrap_or_else(|_| temp.path().join("app-data").join("Boxes"));
        let stored = Path::new(imported.stored_path.as_deref().unwrap())
            .canonicalize()
            .unwrap();
        assert!(
            stored.starts_with(&boxes_root),
            "stored path {:?} should be under boxes root {:?}",
            stored,
            boxes_root
        );

        let result = service.delete_box(drawer_box.id).unwrap();

        assert!(result.box_removed);
        assert_eq!(result.restored_count, 1);
        assert!(source_path.exists());
        assert!(!Path::new(imported.stored_path.as_deref().unwrap()).exists());
    }

    #[test]
    fn import_rejects_box_storage_outside_boxes_root_without_creating_it() {
        let temp = TempDir::new().unwrap();
        let (service, repository, _) = create_service(&temp);
        let external_storage = temp.path().join("outside-storage");
        let now = Utc::now();
        let external_box = DrawerBox {
            id: Uuid::new_v4(),
            name: "Unsafe".to_string(),
            box_type: BoxType::Normal,
            storage_path: Some(external_storage.to_string_lossy().to_string()),
            sort_order: 100,
            created_at: now,
            updated_at: now,
        };
        repository.add_box(&external_box).unwrap();

        let source_path = temp.path().join("source.txt");
        fs::write(&source_path, "data").unwrap();

        let result =
            service.import_path(external_box.id, source_path.to_str().unwrap(), None, None);

        assert!(result.is_err());
        assert!(source_path.exists());
        assert!(!external_storage.exists());
    }

    #[test]
    fn mapping_box_only_stores_and_removes_reference() {
        let temp = TempDir::new().unwrap();
        let (service, _, _) = create_service(&temp);
        let mapping_box = service
            .get_boxes()
            .unwrap()
            .into_iter()
            .find(|b| b.box_type == BoxType::Mapping)
            .unwrap();
        let source_path = temp.path().join("mapped.txt");
        fs::write(&source_path, "mapped-data").unwrap();

        let imported = service
            .import_path(mapping_box.id, source_path.to_str().unwrap(), None, None)
            .unwrap();

        assert!(source_path.is_file());
        assert!(imported.stored_path.is_none());

        let result = service.delete_item(imported.id).unwrap();

        assert!(!result.was_stored_item);
        assert_eq!(fs::read_to_string(source_path).unwrap(), "mapped-data");
    }

    #[test]
    fn deleting_box_never_deletes_storage_outside_boxes_root() {
        let temp = TempDir::new().unwrap();
        let (service, repository, _) = create_service(&temp);
        let external_storage = temp.path().join("outside-empty-directory");
        fs::create_dir_all(&external_storage).unwrap();
        let now = Utc::now();
        let external_box = DrawerBox {
            id: Uuid::new_v4(),
            name: "Unsafe".to_string(),
            box_type: BoxType::Normal,
            storage_path: Some(external_storage.to_string_lossy().to_string()),
            sort_order: 100,
            created_at: now,
            updated_at: now,
        };
        repository.add_box(&external_box).unwrap();

        let result = service.delete_box(external_box.id).unwrap();

        assert!(result.box_removed);
        assert!(external_storage.is_dir());
    }
}
