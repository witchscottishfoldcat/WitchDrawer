//! SQLite storage repository for WitchDrawer.
//!
//! Mirrors the C# `DrawerRepository` with synchronous `rusqlite` API.
//! Each method opens its own connection (matching the C# per-call pattern),
//! which avoids `Send` issues since `rusqlite::Connection` is `!Send`.
//! The FFI layer wraps calls in `tokio::task::spawn_blocking` for async ergonomics.

use std::fs;
use std::path::Path;
use std::time::Duration;

use chrono::{DateTime, Utc};
use rusqlite::{params, Connection, OpenFlags};
use uuid::Uuid;

use crate::models::{AppError, AppResult, BoxType, DrawerBox, DrawerItem, ItemKind, TodoItem};

// ── datetime helpers ────────────────────────────────────────

/// Format a UTC datetime for SQLite storage (RFC 3339).
fn to_db(dt: DateTime<Utc>) -> String {
    dt.to_rfc3339()
}

/// Parse a datetime string from SQLite back into `DateTime<Utc>`.
fn parse_dt(s: &str) -> Result<DateTime<Utc>, rusqlite::Error> {
    DateTime::parse_from_rfc3339(s)
        .map(|dt| dt.with_timezone(&Utc))
        .map_err(|e| {
            rusqlite::Error::InvalidParameterName(format!("Invalid datetime '{}': {}", s, e))
        })
}

/// Parse a UUID string from SQLite.
fn parse_uuid(s: &str) -> Result<Uuid, rusqlite::Error> {
    Uuid::parse_str(s)
        .map_err(|e| rusqlite::Error::InvalidParameterName(format!("Invalid UUID '{}': {}", s, e)))
}

/// Escape `%`, `_`, and `\` so a user query is treated as a literal in `LIKE`.
/// Mirrors the C# `DrawerRepository.EscapeLikePattern`.
fn escape_like_pattern(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('%', "\\%")
        .replace('_', "\\_")
}

// ── row readers ────────────────────────────────────────────

/// Read a `DrawerBox` from a row with columns:
/// Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt
fn read_box(row: &rusqlite::Row) -> rusqlite::Result<DrawerBox> {
    let id_str: String = row.get(0)?;
    let name: String = row.get(1)?;
    let type_int: i32 = row.get(2)?;
    let storage_path: Option<String> = row.get(3)?;
    let sort_order: i32 = row.get(4)?;
    let created_at_str: String = row.get(5)?;
    let updated_at_str: String = row.get(6)?;

    Ok(DrawerBox {
        id: parse_uuid(&id_str)?,
        name,
        box_type: BoxType::from_i32(type_int).ok_or_else(|| {
            rusqlite::Error::InvalidParameterName(format!("Unknown BoxType value: {}", type_int))
        })?,
        storage_path,
        sort_order,
        created_at: parse_dt(&created_at_str)?,
        updated_at: parse_dt(&updated_at_str)?,
    })
}

/// Read a `DrawerItem` from a row with columns:
/// Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath, SortOrder,
/// CreatedAt, UpdatedAt, GridColumn, GridRow
fn read_item(row: &rusqlite::Row) -> rusqlite::Result<DrawerItem> {
    let id_str: String = row.get(0)?;
    let box_id_str: String = row.get(1)?;
    let display_name: String = row.get(2)?;
    let item_kind_int: i32 = row.get(3)?;
    let source_path: Option<String> = row.get(4)?;
    let stored_path: Option<String> = row.get(5)?;
    let sort_order: i32 = row.get(6)?;
    let created_at_str: String = row.get(7)?;
    let updated_at_str: String = row.get(8)?;
    let grid_column: Option<i32> = row.get(9)?;
    let grid_row: Option<i32> = row.get(10)?;

    Ok(DrawerItem {
        id: parse_uuid(&id_str)?,
        box_id: parse_uuid(&box_id_str)?,
        display_name,
        item_kind: ItemKind::from_i32(item_kind_int).ok_or_else(|| {
            rusqlite::Error::InvalidParameterName(format!(
                "Unknown ItemKind value: {}",
                item_kind_int
            ))
        })?,
        source_path,
        stored_path,
        sort_order,
        created_at: parse_dt(&created_at_str)?,
        updated_at: parse_dt(&updated_at_str)?,
        grid_column,
        grid_row,
    })
}

/// Read a `TodoItem` from a row with columns:
/// Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt,
/// CompletedAt, IsArchived, ArchivedAt
fn read_todo(row: &rusqlite::Row) -> rusqlite::Result<TodoItem> {
    let id_str: String = row.get(0)?;
    let box_id_str: String = row.get(1)?;
    let title: String = row.get(2)?;
    let is_completed_int: i32 = row.get(3)?;
    let sort_order: i32 = row.get(4)?;
    let created_at_str: String = row.get(5)?;
    let updated_at_str: String = row.get(6)?;
    let completed_at_str: Option<String> = row.get(7)?;
    let is_archived_int: i32 = row.get(8)?;
    let archived_at_str: Option<String> = row.get(9)?;

    Ok(TodoItem {
        id: parse_uuid(&id_str)?,
        box_id: parse_uuid(&box_id_str)?,
        title,
        is_completed: is_completed_int != 0,
        sort_order,
        created_at: parse_dt(&created_at_str)?,
        updated_at: parse_dt(&updated_at_str)?,
        completed_at: completed_at_str.as_deref().map(parse_dt).transpose()?,
        is_archived: is_archived_int != 0,
        archived_at: archived_at_str.as_deref().map(parse_dt).transpose()?,
    })
}

// ── DrawerRepository ───────────────────────────────────────

/// SQLite-backed repository for Boxes, Items, Todos, and AppSettings.
#[derive(Clone)]
pub struct DrawerRepository {
    db_path: String,
}

impl DrawerRepository {
    /// Create a new repository targeting the given database file path.
    pub fn new(db_path: impl Into<String>) -> Self {
        Self {
            db_path: db_path.into(),
        }
    }

    /// Open a fresh connection (matching the C# per-call pattern).
    fn create_connection(&self) -> AppResult<Connection> {
        let flags = OpenFlags::SQLITE_OPEN_READ_WRITE | OpenFlags::SQLITE_OPEN_CREATE;
        let conn = Connection::open_with_flags(&self.db_path, flags)?;
        conn.busy_timeout(Duration::from_secs(5))?;
        conn.execute_batch("PRAGMA foreign_keys = ON;")?;
        Ok(conn)
    }

    // ── Initialise ────────────────────────────────────────

    /// Create tables, indexes, and run migrations. Idempotent.
    pub fn initialize(&self) -> AppResult<()> {
        // Ensure parent directory exists
        if let Some(parent) = Path::new(&self.db_path).parent() {
            fs::create_dir_all(parent)?;
        }

        let conn = self.create_connection()?;

        // WAL mode (separate statement — must execute before table creation)
        conn.execute_batch("PRAGMA journal_mode = WAL;")?;

        conn.execute_batch(
            "
            CREATE TABLE IF NOT EXISTS Boxes (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Type INTEGER NOT NULL,
                StoragePath TEXT NULL,
                SortOrder INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Items (
                Id TEXT PRIMARY KEY,
                BoxId TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                ItemKind INTEGER NOT NULL,
                SourcePath TEXT NULL,
                StoredPath TEXT NULL,
                SortOrder INTEGER NOT NULL,
                GridColumn INTEGER NULL,
                GridRow INTEGER NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS AppSettings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Todos (
                Id TEXT PRIMARY KEY,
                BoxId TEXT NOT NULL,
                Title TEXT NOT NULL,
                IsCompleted INTEGER NOT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                ArchivedAt TEXT NULL,
                FOREIGN KEY(BoxId) REFERENCES Boxes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Items_BoxId ON Items(BoxId);
            CREATE INDEX IF NOT EXISTS IX_Items_DisplayName ON Items(DisplayName);
            ",
        )?;

        // ── migrations for older databases ──────────────
        Self::ensure_column(&conn, "Items", "GridColumn", "INTEGER NULL")?;
        Self::ensure_column(&conn, "Items", "GridRow", "INTEGER NULL")?;
        Self::ensure_column(&conn, "Todos", "BoxId", "TEXT NULL")?;
        Self::ensure_column(&conn, "Todos", "IsArchived", "INTEGER NOT NULL DEFAULT 0")?;
        Self::ensure_column(&conn, "Todos", "ArchivedAt", "TEXT NULL")?;

        conn.execute_batch(
            "
            CREATE INDEX IF NOT EXISTS IX_Todos_BoxStateSort
                ON Todos(BoxId, IsCompleted, SortOrder);
            CREATE INDEX IF NOT EXISTS IX_Todos_BoxArchiveStateSort
                ON Todos(BoxId, IsArchived, IsCompleted, SortOrder);
            ",
        )?;

        Ok(())
    }

    /// Add a column to `table` if it doesn't already exist (case-insensitive).
    fn ensure_column(
        conn: &Connection,
        table: &str,
        column: &str,
        definition: &str,
    ) -> AppResult<()> {
        let mut stmt = conn.prepare(&format!("PRAGMA table_info({});", table))?;
        let columns: Vec<String> = stmt
            .query_map([], |row| row.get::<_, String>(1))?
            .filter_map(|r| r.ok())
            .collect();

        if !columns.iter().any(|c| c.eq_ignore_ascii_case(column)) {
            conn.execute(
                &format!(
                    "ALTER TABLE {} ADD COLUMN {} {};",
                    table, column, definition
                ),
                [],
            )?;
        }
        Ok(())
    }

    // ── Boxes ─────────────────────────────────────────────

    /// Get all boxes ordered by SortOrder then Name.
    pub fn get_boxes(&self) -> AppResult<Vec<DrawerBox>> {
        let conn = self.create_connection()?;
        let mut stmt = conn.prepare(
            "SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt
             FROM Boxes
             ORDER BY SortOrder, Name;",
        )?;
        let rows = stmt.query_map([], read_box)?;
        rows.collect::<Result<Vec<_>, _>>().map_err(AppError::from)
    }

    /// Get a single box by id, or `None` if not found.
    pub fn get_box(&self, box_id: Uuid) -> AppResult<Option<DrawerBox>> {
        let conn = self.create_connection()?;
        let mut stmt = conn.prepare(
            "SELECT Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt
             FROM Boxes
             WHERE Id = ?1;",
        )?;
        let mut rows = stmt.query_map(params![box_id.to_string()], read_box)?;
        match rows.next() {
            Some(r) => Ok(Some(r?)),
            None => Ok(None),
        }
    }

    /// Insert a new box.
    pub fn add_box(&self, b: &DrawerBox) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "INSERT INTO Boxes (Id, Name, Type, StoragePath, SortOrder, CreatedAt, UpdatedAt)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7);",
            params![
                b.id.to_string(),
                b.name,
                b.box_type as i32,
                b.storage_path,
                b.sort_order,
                to_db(b.created_at),
                to_db(b.updated_at),
            ],
        )?;
        Ok(())
    }

    /// Rename a box and touch UpdatedAt.
    pub fn update_box_name(&self, box_id: Uuid, new_name: &str) -> AppResult<()> {
        let conn = self.create_connection()?;
        let now = to_db(Utc::now());
        conn.execute(
            "UPDATE Boxes SET Name = ?1, UpdatedAt = ?2 WHERE Id = ?3;",
            params![new_name, now, box_id.to_string()],
        )?;
        Ok(())
    }

    /// Re-order boxes. The index in `ordered_box_ids` becomes the new SortOrder.
    /// Wrapped in a transaction; rolls back if any id doesn't exist.
    pub fn update_box_sort_orders(&self, ordered_box_ids: &[Uuid]) -> AppResult<()> {
        let conn = self.create_connection()?;
        let tx = conn.unchecked_transaction()?;
        let now = to_db(Utc::now());

        {
            let mut stmt =
                tx.prepare("UPDATE Boxes SET SortOrder = ?1, UpdatedAt = ?2 WHERE Id = ?3;")?;
            for (index, id) in ordered_box_ids.iter().enumerate() {
                let changed = stmt.execute(params![index as i32, now, id.to_string()])?;
                if changed == 0 {
                    return Err(AppError::invalid_arg(
                        "Cannot reorder a box that does not exist.",
                    ));
                }
            }
        }
        tx.commit()?;
        Ok(())
    }

    /// Remove a box (and its todos explicitly, items cascade via FK).
    pub fn remove_box(&self, box_id: Uuid) -> AppResult<()> {
        let conn = self.create_connection()?;
        let tx = conn.unchecked_transaction()?;
        let id = box_id.to_string();

        tx.execute("DELETE FROM Todos WHERE BoxId = ?1;", params![id])?;
        tx.execute("DELETE FROM Boxes WHERE Id = ?1;", params![id])?;

        tx.commit()?;
        Ok(())
    }

    // ── Items ─────────────────────────────────────────────

    /// Get items, optionally filtered by box. Ordered by grid position then sort order.
    pub fn get_items(&self, box_id: Option<Uuid>) -> AppResult<Vec<DrawerItem>> {
        let conn = self.create_connection()?;
        let (sql, param) = match box_id {
            Some(bid) => (
                "SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath,
                        SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
                 FROM Items
                 WHERE BoxId = ?1
                 ORDER BY COALESCE(GridRow, 1000000), COALESCE(GridColumn, 1000000),
                          SortOrder, DisplayName;",
                Some(bid.to_string()),
            ),
            None => (
                "SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath,
                        SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
                 FROM Items
                 ORDER BY COALESCE(GridRow, 1000000), COALESCE(GridColumn, 1000000),
                          SortOrder, DisplayName;",
                None,
            ),
        };
        let mut stmt = conn.prepare(sql)?;
        let rows = if let Some(id) = param {
            stmt.query_map(params![id], read_item)?
        } else {
            stmt.query_map([], read_item)?
        };
        rows.collect::<Result<Vec<_>, _>>().map_err(AppError::from)
    }

    /// Full-text search across DisplayName, SourcePath, StoredPath.
    /// LIKE wildcards in the query are escaped so user input matches literally.
    pub fn search_items(&self, query: &str, limit: i32) -> AppResult<Vec<DrawerItem>> {
        let conn = self.create_connection()?;
        let like = format!("%{}%", escape_like_pattern(query));
        let mut stmt = conn.prepare(
            "SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath,
                    SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
             FROM Items
             WHERE ?1 = '' OR DisplayName LIKE ?2 ESCAPE '\\'
                OR SourcePath LIKE ?2 ESCAPE '\\' OR StoredPath LIKE ?2 ESCAPE '\\'
             ORDER BY COALESCE(GridRow, 1000000), COALESCE(GridColumn, 1000000),
                      SortOrder, DisplayName
             LIMIT ?3;",
        )?;
        let rows = stmt.query_map(params![query, like, limit], read_item)?;
        rows.collect::<Result<Vec<_>, _>>().map_err(AppError::from)
    }

    /// Get a single item by id.
    pub fn get_item(&self, item_id: Uuid) -> AppResult<Option<DrawerItem>> {
        let conn = self.create_connection()?;
        let mut stmt = conn.prepare(
            "SELECT Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath,
                    SortOrder, CreatedAt, UpdatedAt, GridColumn, GridRow
             FROM Items
             WHERE Id = ?1;",
        )?;
        let mut rows = stmt.query_map(params![item_id.to_string()], read_item)?;
        match rows.next() {
            Some(r) => Ok(Some(r?)),
            None => Ok(None),
        }
    }

    /// Insert a new item.
    pub fn add_item(&self, item: &DrawerItem) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "INSERT INTO Items
                 (Id, BoxId, DisplayName, ItemKind, SourcePath, StoredPath,
                  SortOrder, GridColumn, GridRow, CreatedAt, UpdatedAt)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11);",
            params![
                item.id.to_string(),
                item.box_id.to_string(),
                item.display_name,
                item.item_kind as i32,
                item.source_path,
                item.stored_path,
                item.sort_order,
                item.grid_column,
                item.grid_row,
                to_db(item.created_at),
                to_db(item.updated_at),
            ],
        )?;
        Ok(())
    }

    /// Update an item's grid position (GridColumn / GridRow) and touch UpdatedAt.
    pub fn update_item_grid_position(
        &self,
        item_id: Uuid,
        grid_column: Option<i32>,
        grid_row: Option<i32>,
    ) -> AppResult<()> {
        let conn = self.create_connection()?;
        let now = to_db(Utc::now());
        conn.execute(
            "UPDATE Items
             SET GridColumn = ?1, GridRow = ?2, UpdatedAt = ?3
             WHERE Id = ?4;",
            params![grid_column, grid_row, now, item_id.to_string()],
        )?;
        Ok(())
    }

    /// Move an item to a different box with new metadata.
    /// Matches the C# `MoveItemToBoxAsync(DrawerItem item, ...)` signature.
    #[allow(clippy::too_many_arguments)]
    pub fn move_item_to_box(
        &self,
        item: &DrawerItem,
        target_box_id: Uuid,
        display_name: &str,
        source_path: Option<&str>,
        stored_path: Option<&str>,
        sort_order: i32,
        grid_column: Option<i32>,
        grid_row: Option<i32>,
    ) -> AppResult<()> {
        let conn = self.create_connection()?;
        let now = to_db(Utc::now());
        conn.execute(
            "UPDATE Items
             SET BoxId = ?1, DisplayName = ?2, SourcePath = ?3, StoredPath = ?4,
                 SortOrder = ?5, GridColumn = ?6, GridRow = ?7, UpdatedAt = ?8
             WHERE Id = ?9;",
            params![
                target_box_id.to_string(),
                display_name,
                source_path,
                stored_path,
                sort_order,
                grid_column,
                grid_row,
                now,
                item.id.to_string(),
            ],
        )?;
        Ok(())
    }

    /// Delete an item by id.
    pub fn remove_item(&self, item_id: Uuid) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "DELETE FROM Items WHERE Id = ?1;",
            params![item_id.to_string()],
        )?;
        Ok(())
    }

    // ── Todos ─────────────────────────────────────────────

    /// Get active (non-archived) todos for a box, ordered by completion then sort order.
    pub fn get_todos(&self, box_id: Uuid) -> AppResult<Vec<TodoItem>> {
        let conn = self.create_connection()?;
        let mut stmt = conn.prepare(
            "SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt,
                    CompletedAt, IsArchived, ArchivedAt
             FROM Todos
             WHERE BoxId = ?1 AND IsArchived = 0
             ORDER BY IsCompleted, SortOrder, CreatedAt;",
        )?;
        let rows = stmt.query_map(params![box_id.to_string()], read_todo)?;
        rows.collect::<Result<Vec<_>, _>>().map_err(AppError::from)
    }

    /// Get archived todos, optionally filtered by box.
    pub fn get_archived_todos(&self, box_id: Option<Uuid>) -> AppResult<Vec<TodoItem>> {
        let conn = self.create_connection()?;
        let (sql, param) = match box_id {
            Some(bid) => (
                "SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt,
                        CompletedAt, IsArchived, ArchivedAt
                 FROM Todos
                 WHERE BoxId = ?1 AND IsArchived = 1
                 ORDER BY ArchivedAt DESC, UpdatedAt DESC;",
                Some(bid.to_string()),
            ),
            None => (
                "SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt,
                        CompletedAt, IsArchived, ArchivedAt
                 FROM Todos
                 WHERE IsArchived = 1
                 ORDER BY ArchivedAt DESC, UpdatedAt DESC;",
                None,
            ),
        };
        let mut stmt = conn.prepare(sql)?;
        let rows = if let Some(id) = param {
            stmt.query_map(params![id], read_todo)?
        } else {
            stmt.query_map([], read_todo)?
        };
        rows.collect::<Result<Vec<_>, _>>().map_err(AppError::from)
    }

    /// Get a single todo by id.
    pub fn get_todo(&self, todo_id: Uuid) -> AppResult<Option<TodoItem>> {
        let conn = self.create_connection()?;
        let mut stmt = conn.prepare(
            "SELECT Id, BoxId, Title, IsCompleted, SortOrder, CreatedAt, UpdatedAt,
                    CompletedAt, IsArchived, ArchivedAt
             FROM Todos
             WHERE Id = ?1;",
        )?;
        let mut rows = stmt.query_map(params![todo_id.to_string()], read_todo)?;
        match rows.next() {
            Some(r) => Ok(Some(r?)),
            None => Ok(None),
        }
    }

    /// Insert a new todo.
    pub fn add_todo(&self, todo: &TodoItem) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "INSERT INTO Todos
                 (Id, BoxId, Title, IsCompleted, IsArchived, SortOrder,
                  CreatedAt, UpdatedAt, CompletedAt, ArchivedAt)
             VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10);",
            params![
                todo.id.to_string(),
                todo.box_id.to_string(),
                todo.title,
                todo.is_completed as i32,
                todo.is_archived as i32,
                todo.sort_order,
                to_db(todo.created_at),
                to_db(todo.updated_at),
                todo.completed_at.map(to_db),
                todo.archived_at.map(to_db),
            ],
        )?;
        Ok(())
    }

    /// Update a todo's completion state.
    pub fn update_todo_completion(
        &self,
        todo_id: Uuid,
        is_completed: bool,
        completed_at: Option<DateTime<Utc>>,
        updated_at: DateTime<Utc>,
    ) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "UPDATE Todos
             SET IsCompleted = ?1, CompletedAt = ?2, UpdatedAt = ?3
             WHERE Id = ?4;",
            params![
                is_completed as i32,
                completed_at.map(to_db),
                to_db(updated_at),
                todo_id.to_string(),
            ],
        )?;
        Ok(())
    }

    /// Delete a todo by id.
    pub fn remove_todo(&self, todo_id: Uuid) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "DELETE FROM Todos WHERE Id = ?1;",
            params![todo_id.to_string()],
        )?;
        Ok(())
    }

    /// Archive all completed, non-archived todos in a box. Returns the count affected.
    pub fn archive_completed_todos(
        &self,
        box_id: Uuid,
        archived_at: DateTime<Utc>,
    ) -> AppResult<i32> {
        let conn = self.create_connection()?;
        let count = conn.execute(
            "UPDATE Todos
             SET IsArchived = 1, ArchivedAt = ?1, UpdatedAt = ?1
             WHERE BoxId = ?2
               AND IsCompleted = 1
               AND IsArchived = 0;",
            params![to_db(archived_at), box_id.to_string()],
        )?;
        Ok(count as i32)
    }

    /// Manually set a todo's archive state.
    pub fn update_todo_archive_state(
        &self,
        todo_id: Uuid,
        is_archived: bool,
        archived_at: Option<DateTime<Utc>>,
        updated_at: DateTime<Utc>,
    ) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "UPDATE Todos
             SET IsArchived = ?1, ArchivedAt = ?2, UpdatedAt = ?3
             WHERE Id = ?4;",
            params![
                is_archived as i32,
                archived_at.map(to_db),
                to_db(updated_at),
                todo_id.to_string(),
            ],
        )?;
        Ok(())
    }

    // ── AppSettings ───────────────────────────────────────

    /// Get a setting value by key, or `None` if not found.
    pub fn get_setting(&self, key: &str) -> AppResult<Option<String>> {
        let conn = self.create_connection()?;
        let mut stmt = conn.prepare("SELECT Value FROM AppSettings WHERE Key = ?1;")?;
        let mut rows = stmt.query_map(params![key], |row| row.get::<_, String>(0))?;
        match rows.next() {
            Some(r) => Ok(Some(r?)),
            None => Ok(None),
        }
    }

    /// Upsert a setting (insert or replace on key conflict).
    pub fn set_setting(&self, key: &str, value: &str) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute(
            "INSERT INTO AppSettings (Key, Value) VALUES (?1, ?2)
             ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;",
            params![key, value],
        )?;
        Ok(())
    }

    /// Delete a setting by key. Returns `true` if a row was removed.
    pub fn delete_setting(&self, key: &str) -> AppResult<bool> {
        let conn = self.create_connection()?;
        let affected = conn.execute("DELETE FROM AppSettings WHERE Key = ?1;", params![key])?;
        Ok(affected > 0)
    }

    /// Update a box's storage path and touch UpdatedAt.
    pub fn update_box_storage_path(&self, box_id: Uuid, storage_path: &str) -> AppResult<()> {
        let conn = self.create_connection()?;
        let now = to_db(Utc::now());
        let affected = conn.execute(
            "UPDATE Boxes SET StoragePath = ?1, UpdatedAt = ?2 WHERE Id = ?3;",
            params![storage_path, now, box_id.to_string()],
        )?;
        if affected != 1 {
            return Err(AppError::db_error("Box does not exist."));
        }
        Ok(())
    }

    /// Update an item's stored path and touch UpdatedAt.
    pub fn update_item_stored_path(&self, item_id: Uuid, stored_path: &str) -> AppResult<()> {
        let conn = self.create_connection()?;
        let now = to_db(Utc::now());
        let affected = conn.execute(
            "UPDATE Items SET StoredPath = ?1, UpdatedAt = ?2 WHERE Id = ?3;",
            params![stored_path, now, item_id.to_string()],
        )?;
        if affected != 1 {
            return Err(AppError::db_error("Item does not exist."));
        }
        Ok(())
    }

    /// Batch-update multiple items' grid positions in a single transaction.
    /// Mirrors the C# `UpdateItemGridPositionsAsync`.
    pub fn update_item_grid_positions(&self, positions: &[(Uuid, i32, i32)]) -> AppResult<()> {
        let conn = self.create_connection()?;
        let now = to_db(Utc::now());
        let tx = conn.unchecked_transaction()?;
        {
            let mut stmt = tx.prepare(
                "UPDATE Items SET GridColumn = ?1, GridRow = ?2, UpdatedAt = ?3
                 WHERE Id = ?4;",
            )?;
            for (item_id, col, row) in positions {
                stmt.execute(params![col, row, now, item_id.to_string()])?;
            }
        }
        tx.commit()?;
        Ok(())
    }

    /// Force a WAL checkpoint (TRUNCATE). Mirrors the C# `CheckpointAsync`.
    pub fn checkpoint(&self) -> AppResult<()> {
        let conn = self.create_connection()?;
        conn.execute_batch("PRAGMA wal_checkpoint(TRUNCATE);")?;
        Ok(())
    }

    // ── Sort-order helpers ────────────────────────────────

    /// Next sort order for a new box (max + 1, or 0 if empty).
    pub fn get_next_box_sort_order(&self) -> AppResult<i32> {
        let conn = self.create_connection()?;
        let val: i32 = conn.query_row(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Boxes;",
            [],
            |row| row.get(0),
        )?;
        Ok(val)
    }

    /// Next sort order for a new item inside a box.
    pub fn get_next_item_sort_order(&self, box_id: Uuid) -> AppResult<i32> {
        let conn = self.create_connection()?;
        let val: i32 = conn.query_row(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Items WHERE BoxId = ?1;",
            params![box_id.to_string()],
            |row| row.get(0),
        )?;
        Ok(val)
    }

    /// Next sort order for a new todo inside a box.
    pub fn get_next_todo_sort_order(&self, box_id: Uuid) -> AppResult<i32> {
        let conn = self.create_connection()?;
        let val: i32 = conn.query_row(
            "SELECT COALESCE(MAX(SortOrder), -1) + 1 FROM Todos WHERE BoxId = ?1;",
            params![box_id.to_string()],
            |row| row.get(0),
        )?;
        Ok(val)
    }
}

// ═════════════════════════════════════════════════════════════
//  Tests
// ═════════════════════════════════════════════════════════════

#[cfg(test)]
mod tests {
    use super::*;
    use chrono::Utc;

    /// Helper: create a DrawerRepository backed by a temporary file.
    fn temp_repo() -> DrawerRepository {
        let dir = tempfile::tempdir().unwrap();
        let db_path = dir.path().join("test.db");
        let repo = DrawerRepository::new(db_path.to_str().unwrap().to_string());
        repo.initialize().unwrap();
        // Keep `dir` alive for the duration of the test by leaking it.
        // (Rust drops `dir` at end of block; we leak so the DB file persists.)
        std::mem::forget(dir);
        repo
    }

    fn now() -> DateTime<Utc> {
        Utc::now()
    }

    fn test_box(name: &str) -> DrawerBox {
        DrawerBox {
            id: Uuid::new_v4(),
            name: name.to_string(),
            box_type: BoxType::Normal,
            storage_path: None,
            sort_order: 0,
            created_at: now(),
            updated_at: now(),
        }
    }

    fn test_item(box_id: Uuid, name: &str) -> DrawerItem {
        DrawerItem {
            id: Uuid::new_v4(),
            box_id,
            display_name: name.to_string(),
            item_kind: ItemKind::File,
            source_path: Some(format!("/tmp/{}", name)),
            stored_path: None,
            sort_order: 0,
            created_at: now(),
            updated_at: now(),
            grid_column: None,
            grid_row: None,
        }
    }

    fn test_todo(box_id: Uuid, title: &str) -> TodoItem {
        TodoItem {
            id: Uuid::new_v4(),
            box_id,
            title: title.to_string(),
            is_completed: false,
            sort_order: 0,
            created_at: now(),
            updated_at: now(),
            completed_at: None,
            is_archived: false,
            archived_at: None,
        }
    }

    // ── Init ────────────────────────────────────────────

    #[test]
    fn init_db_is_idempotent() {
        let repo = temp_repo();
        repo.initialize().unwrap();
        repo.initialize().unwrap();
        let boxes = repo.get_boxes().unwrap();
        assert!(boxes.is_empty());
    }

    // ── Boxes ───────────────────────────────────────────

    #[test]
    fn add_and_get_box() {
        let repo = temp_repo();
        let b = test_box("My Box");
        let id = b.id;

        repo.add_box(&b).unwrap();
        let fetched = repo.get_box(id).unwrap().unwrap();
        assert_eq!(fetched.name, "My Box");
        assert_eq!(fetched.box_type, BoxType::Normal);
    }

    #[test]
    fn get_box_returns_none_for_missing() {
        let repo = temp_repo();
        assert!(repo.get_box(Uuid::new_v4()).unwrap().is_none());
    }

    #[test]
    fn get_boxes_ordered() {
        let repo = temp_repo();
        let mut b1 = test_box("Zebra");
        b1.sort_order = 2;
        let mut b2 = test_box("Alpha");
        b2.sort_order = 1;
        let mut b3 = test_box("Middle");
        b3.sort_order = 1; // tie-breaker: name

        repo.add_box(&b1).unwrap();
        repo.add_box(&b2).unwrap();
        repo.add_box(&b3).unwrap();

        let boxes = repo.get_boxes().unwrap();
        assert_eq!(boxes.len(), 3);
        assert_eq!(boxes[0].name, "Alpha");
        assert_eq!(boxes[1].name, "Middle");
        assert_eq!(boxes[2].name, "Zebra");
    }

    #[test]
    fn update_box_name() {
        let repo = temp_repo();
        let b = test_box("Old Name");
        let id = b.id;
        repo.add_box(&b).unwrap();

        repo.update_box_name(id, "New Name").unwrap();
        let fetched = repo.get_box(id).unwrap().unwrap();
        assert_eq!(fetched.name, "New Name");
    }

    #[test]
    fn update_box_sort_orders() {
        let repo = temp_repo();
        let b1 = test_box("A");
        let b2 = test_box("B");
        let b3 = test_box("C");
        repo.add_box(&b1).unwrap();
        repo.add_box(&b2).unwrap();
        repo.add_box(&b3).unwrap();

        repo.update_box_sort_orders(&[b3.id, b1.id, b2.id]).unwrap();

        let boxes = repo.get_boxes().unwrap();
        assert_eq!(boxes[0].name, "C");
        assert_eq!(boxes[1].name, "A");
        assert_eq!(boxes[2].name, "B");
    }

    #[test]
    fn update_box_sort_orders_fails_for_missing_id() {
        let repo = temp_repo();
        let b = test_box("A");
        repo.add_box(&b).unwrap();

        let bad = Uuid::new_v4();
        let result = repo.update_box_sort_orders(&[bad]);
        assert!(result.is_err());
    }

    #[test]
    fn remove_box_cascades_items_and_todos() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let item = test_item(b.id, "Item");
        repo.add_item(&item).unwrap();

        let td = test_todo(b.id, "Todo");
        repo.add_todo(&td).unwrap();

        repo.remove_box(b.id).unwrap();

        assert!(repo.get_box(b.id).unwrap().is_none());
        assert!(repo.get_item(item.id).unwrap().is_none());
        assert!(repo.get_todo(td.id).unwrap().is_none());
    }

    #[test]
    fn remove_box_with_explicit_todo_deletion() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        let td = test_todo(b.id, "Todo");
        repo.add_todo(&td).unwrap();

        repo.remove_box(b.id).unwrap();
        assert!(repo.get_todo(td.id).unwrap().is_none());
        assert!(repo.get_box(b.id).unwrap().is_none());
    }

    // ── Items ───────────────────────────────────────────

    #[test]
    fn add_and_get_item() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let mut item = test_item(b.id, "File.txt");
        item.grid_column = Some(2);
        item.grid_row = Some(3);
        let id = item.id;
        repo.add_item(&item).unwrap();

        let fetched = repo.get_item(id).unwrap().unwrap();
        assert_eq!(fetched.display_name, "File.txt");
        assert_eq!(fetched.grid_column, Some(2));
        assert_eq!(fetched.grid_row, Some(3));
        assert_eq!(fetched.source_path, Some("/tmp/File.txt".to_string()));
    }

    #[test]
    fn get_items_by_box() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let i1 = test_item(b.id, "A.txt");
        let i2 = test_item(b.id, "B.txt");
        let b2 = test_box("Other");
        repo.add_box(&b2).unwrap();
        let i3 = test_item(b2.id, "C.txt");

        repo.add_item(&i1).unwrap();
        repo.add_item(&i2).unwrap();
        repo.add_item(&i3).unwrap();

        let items = repo.get_items(Some(b.id)).unwrap();
        assert_eq!(items.len(), 2);
        let names: Vec<&str> = items.iter().map(|i| i.display_name.as_str()).collect();
        assert!(names.contains(&"A.txt"));
        assert!(names.contains(&"B.txt"));
    }

    #[test]
    fn get_items_all() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        let i1 = test_item(b.id, "A.txt");
        let i2 = test_item(b.id, "B.txt");
        repo.add_item(&i1).unwrap();
        repo.add_item(&i2).unwrap();

        let items = repo.get_items(None).unwrap();
        assert_eq!(items.len(), 2);
    }

    #[test]
    fn get_item_returns_none_for_missing() {
        let repo = temp_repo();
        assert!(repo.get_item(Uuid::new_v4()).unwrap().is_none());
    }

    #[test]
    fn search_items() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let mut i1 = test_item(b.id, "readme.md");
        i1.source_path = Some("/docs/readme.md".to_string());
        let mut i2 = test_item(b.id, "image.png");
        i2.source_path = Some("/img/photo.png".to_string());
        let i3 = test_item(b.id, "config.toml");
        repo.add_item(&i1).unwrap();
        repo.add_item(&i2).unwrap();
        repo.add_item(&i3).unwrap();

        let results = repo.search_items("readme", 200).unwrap();
        assert_eq!(results.len(), 1);
        assert_eq!(results[0].display_name, "readme.md");

        let results = repo.search_items("photo", 200).unwrap();
        assert_eq!(results.len(), 1);

        let results = repo.search_items("", 200).unwrap();
        assert_eq!(results.len(), 3);

        let results = repo.search_items("nonexistent", 200).unwrap();
        assert!(results.is_empty());
    }

    #[test]
    fn search_items_respects_limit() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        for i in 0..10 {
            let item = test_item(b.id, &format!("match_{}.txt", i));
            repo.add_item(&item).unwrap();
        }

        let results = repo.search_items("match", 3).unwrap();
        assert_eq!(results.len(), 3);
    }

    #[test]
    fn update_item_grid_position() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        let item = test_item(b.id, "A.txt");
        let id = item.id;
        repo.add_item(&item).unwrap();

        repo.update_item_grid_position(id, Some(5), Some(7))
            .unwrap();
        let fetched = repo.get_item(id).unwrap().unwrap();
        assert_eq!(fetched.grid_column, Some(5));
        assert_eq!(fetched.grid_row, Some(7));

        repo.update_item_grid_position(id, None, None).unwrap();
        let fetched = repo.get_item(id).unwrap().unwrap();
        assert!(fetched.grid_column.is_none());
        assert!(fetched.grid_row.is_none());
    }

    #[test]
    fn move_item_to_box() {
        let repo = temp_repo();
        let b1 = test_box("Source");
        let b2 = test_box("Target");
        repo.add_box(&b1).unwrap();
        repo.add_box(&b2).unwrap();

        let item = test_item(b1.id, "Movable");
        let id = item.id;
        repo.add_item(&item).unwrap();

        repo.move_item_to_box(
            &item,
            b2.id,
            "Moved",
            Some("/new/path"),
            None,
            0,
            Some(1),
            Some(2),
        )
        .unwrap();

        let fetched = repo.get_item(id).unwrap().unwrap();
        assert_eq!(fetched.box_id, b2.id);
        assert_eq!(fetched.display_name, "Moved");
        assert_eq!(fetched.source_path, Some("/new/path".to_string()));
        assert_eq!(fetched.grid_column, Some(1));
        assert_eq!(fetched.grid_row, Some(2));

        let source_items = repo.get_items(Some(b1.id)).unwrap();
        assert!(source_items.is_empty());
    }

    #[test]
    fn remove_item() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        let item = test_item(b.id, "Delete me");
        let id = item.id;
        repo.add_item(&item).unwrap();

        repo.remove_item(id).unwrap();
        assert!(repo.get_item(id).unwrap().is_none());
    }

    // ── Todos ───────────────────────────────────────────

    #[test]
    fn add_and_get_todo() {
        let repo = temp_repo();
        let b = test_box("Todo Box");
        repo.add_box(&b).unwrap();

        let td = test_todo(b.id, "Buy milk");
        let id = td.id;
        repo.add_todo(&td).unwrap();

        let fetched = repo.get_todo(id).unwrap().unwrap();
        assert_eq!(fetched.title, "Buy milk");
        assert!(!fetched.is_completed);
        assert!(!fetched.is_archived);
    }

    #[test]
    fn get_todo_returns_none_for_missing() {
        let repo = temp_repo();
        assert!(repo.get_todo(Uuid::new_v4()).unwrap().is_none());
    }

    #[test]
    fn get_todos_excludes_archived() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let td1 = test_todo(b.id, "Active");
        let mut td2 = test_todo(b.id, "Archived");
        td2.is_archived = true;
        td2.archived_at = Some(now());

        repo.add_todo(&td1).unwrap();
        repo.add_todo(&td2).unwrap();

        let todos = repo.get_todos(b.id).unwrap();
        assert_eq!(todos.len(), 1);
        assert_eq!(todos[0].title, "Active");
    }

    #[test]
    fn get_todos_ordered_by_completion_then_sort() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let mut td1 = test_todo(b.id, "Done");
        td1.is_completed = true;
        td1.sort_order = 1;
        let mut td2 = test_todo(b.id, "Pending");
        td2.is_completed = false;
        td2.sort_order = 0;

        repo.add_todo(&td1).unwrap();
        repo.add_todo(&td2).unwrap();

        let todos = repo.get_todos(b.id).unwrap();
        assert_eq!(todos[0].title, "Pending");
        assert_eq!(todos[1].title, "Done");
    }

    #[test]
    fn get_archived_todos() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let mut td1 = test_todo(b.id, "Archived");
        td1.is_archived = true;
        td1.archived_at = Some(now());
        let td2 = test_todo(b.id, "Active");

        repo.add_todo(&td1).unwrap();
        repo.add_todo(&td2).unwrap();

        let archived = repo.get_archived_todos(None).unwrap();
        assert_eq!(archived.len(), 1);
        assert_eq!(archived[0].title, "Archived");
    }

    #[test]
    fn get_archived_todos_filtered_by_box() {
        let repo = temp_repo();
        let b1 = test_box("A");
        let b2 = test_box("B");
        repo.add_box(&b1).unwrap();
        repo.add_box(&b2).unwrap();

        let mut td1 = test_todo(b1.id, "Archived A");
        td1.is_archived = true;
        td1.archived_at = Some(now());
        let mut td2 = test_todo(b2.id, "Archived B");
        td2.is_archived = true;
        td2.archived_at = Some(now());

        repo.add_todo(&td1).unwrap();
        repo.add_todo(&td2).unwrap();

        let archived = repo.get_archived_todos(Some(b1.id)).unwrap();
        assert_eq!(archived.len(), 1);
        assert_eq!(archived[0].title, "Archived A");
    }

    #[test]
    fn update_todo_completion() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        let td = test_todo(b.id, "Task");
        let id = td.id;
        repo.add_todo(&td).unwrap();

        let completed = now();
        repo.update_todo_completion(id, true, Some(completed), now())
            .unwrap();

        let fetched = repo.get_todo(id).unwrap().unwrap();
        assert!(fetched.is_completed);
        assert!(fetched.completed_at.is_some());

        repo.update_todo_completion(id, false, None, now()).unwrap();
        let fetched = repo.get_todo(id).unwrap().unwrap();
        assert!(!fetched.is_completed);
        assert!(fetched.completed_at.is_none());
    }

    #[test]
    fn remove_todo() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        let td = test_todo(b.id, "Delete me");
        let id = td.id;
        repo.add_todo(&td).unwrap();

        repo.remove_todo(id).unwrap();
        assert!(repo.get_todo(id).unwrap().is_none());
    }

    #[test]
    fn archive_completed_todos() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let mut td1 = test_todo(b.id, "Completed 1");
        td1.is_completed = true;
        let mut td2 = test_todo(b.id, "Completed 2");
        td2.is_completed = true;
        let mut td3 = test_todo(b.id, "Pending");
        td3.is_completed = false;
        let mut td4 = test_todo(b.id, "Already archived");
        td4.is_completed = true;
        td4.is_archived = true;

        repo.add_todo(&td1).unwrap();
        repo.add_todo(&td2).unwrap();
        repo.add_todo(&td3).unwrap();
        repo.add_todo(&td4).unwrap();

        let count = repo.archive_completed_todos(b.id, now()).unwrap();
        assert_eq!(count, 2);

        let active = repo.get_todos(b.id).unwrap();
        assert_eq!(active.len(), 1);
        assert_eq!(active[0].title, "Pending");

        let archived = repo.get_archived_todos(None).unwrap();
        assert_eq!(archived.len(), 3);
    }

    #[test]
    fn update_todo_archive_state() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        let td = test_todo(b.id, "Task");
        let id = td.id;
        repo.add_todo(&td).unwrap();

        let archive_time = now();
        repo.update_todo_archive_state(id, true, Some(archive_time), now())
            .unwrap();

        let fetched = repo.get_todo(id).unwrap().unwrap();
        assert!(fetched.is_archived);
        assert!(fetched.archived_at.is_some());

        repo.update_todo_archive_state(id, false, None, now())
            .unwrap();
        let fetched = repo.get_todo(id).unwrap().unwrap();
        assert!(!fetched.is_archived);
        assert!(fetched.archived_at.is_none());
    }

    // ── Settings ────────────────────────────────────────

    #[test]
    fn get_set_setting() {
        let repo = temp_repo();

        assert!(repo.get_setting("theme").unwrap().is_none());

        repo.set_setting("theme", "dark").unwrap();
        assert_eq!(repo.get_setting("theme").unwrap().unwrap(), "dark");

        repo.set_setting("theme", "light").unwrap();
        assert_eq!(repo.get_setting("theme").unwrap().unwrap(), "light");
    }

    // ── Sort order helpers ──────────────────────────────

    #[test]
    fn next_box_sort_order_empty() {
        let repo = temp_repo();
        assert_eq!(repo.get_next_box_sort_order().unwrap(), 0);
    }

    #[test]
    fn next_box_sort_order_after_inserts() {
        let repo = temp_repo();
        let mut b1 = test_box("A");
        b1.sort_order = 5;
        repo.add_box(&b1).unwrap();

        let next = repo.get_next_box_sort_order().unwrap();
        assert_eq!(next, 6);
    }

    #[test]
    fn next_item_sort_order() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        assert_eq!(repo.get_next_item_sort_order(b.id).unwrap(), 0);

        let mut item = test_item(b.id, "First");
        item.sort_order = 3;
        repo.add_item(&item).unwrap();

        assert_eq!(repo.get_next_item_sort_order(b.id).unwrap(), 4);
    }

    #[test]
    fn next_todo_sort_order() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();
        assert_eq!(repo.get_next_todo_sort_order(b.id).unwrap(), 0);

        let mut td = test_todo(b.id, "Task");
        td.sort_order = 7;
        repo.add_todo(&td).unwrap();

        assert_eq!(repo.get_next_todo_sort_order(b.id).unwrap(), 8);
    }

    // ── Edge cases ──────────────────────────────────────

    #[test]
    fn box_with_storage_path() {
        let repo = temp_repo();
        let mut b = test_box("Mapped");
        b.box_type = BoxType::Mapping;
        b.storage_path = Some("/home/user/mapped".to_string());
        let id = b.id;
        repo.add_box(&b).unwrap();

        let fetched = repo.get_box(id).unwrap().unwrap();
        assert_eq!(fetched.box_type, BoxType::Mapping);
        assert_eq!(fetched.storage_path, Some("/home/user/mapped".to_string()));
    }

    #[test]
    fn item_with_all_nullable_fields() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let mut item = test_item(b.id, "Nulls");
        item.source_path = None;
        item.stored_path = None;
        item.grid_column = None;
        item.grid_row = None;
        let id = item.id;
        repo.add_item(&item).unwrap();

        let fetched = repo.get_item(id).unwrap().unwrap();
        assert!(fetched.source_path.is_none());
        assert!(fetched.stored_path.is_none());
        assert!(fetched.grid_column.is_none());
        assert!(fetched.grid_row.is_none());
    }

    #[test]
    fn todo_with_completion_and_archive_times() {
        let repo = temp_repo();
        let b = test_box("Box");
        repo.add_box(&b).unwrap();

        let completed = now();
        let archived = now();
        let mut td = test_todo(b.id, "Full");
        td.is_completed = true;
        td.completed_at = Some(completed);
        td.is_archived = true;
        td.archived_at = Some(archived);
        let id = td.id;
        repo.add_todo(&td).unwrap();

        let fetched = repo.get_todo(id).unwrap().unwrap();
        assert!(fetched.is_completed);
        assert!(fetched.completed_at.is_some());
        assert!(fetched.is_archived);
        assert!(fetched.archived_at.is_some());
    }

    #[test]
    fn different_box_types() {
        let repo = temp_repo();

        let types = [
            (BoxType::Normal, "Normal"),
            (BoxType::Mapping, "Mapping"),
            (BoxType::Pixel, "Pixel"),
            (BoxType::Todo, "Todo"),
        ];

        for (bt, name) in &types {
            let mut b = test_box(name);
            b.box_type = *bt;
            let id = b.id;
            repo.add_box(&b).unwrap();
            let fetched = repo.get_box(id).unwrap().unwrap();
            assert_eq!(fetched.box_type, *bt);
        }

        assert_eq!(repo.get_boxes().unwrap().len(), 4);
    }

    #[test]
    fn item_effective_path() {
        let b = Uuid::new_v4();
        let mut item = test_item(b, "A");
        item.stored_path = Some("/stored/path".to_string());
        item.source_path = Some("/source/path".to_string());
        assert_eq!(item.effective_path(), Some("/stored/path"));

        item.stored_path = None;
        assert_eq!(item.effective_path(), Some("/source/path"));
    }
}
