mod box_model;
mod drawer_item;
mod results;
mod todo_item;

pub use box_model::*;
pub use drawer_item::*;
pub use results::*;
pub use todo_item::*;

use serde::{Deserialize, Serialize};
use uuid::Uuid;

/// 对应 C# BoxType 枚举
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[repr(i32)]
pub enum BoxType {
    Normal = 0,
    Mapping = 1,
    Pixel = 2,
    Todo = 3,
    Drawer = 4,
}

impl BoxType {
    pub fn from_i32(v: i32) -> Option<Self> {
        match v {
            0 => Some(Self::Normal),
            1 => Some(Self::Mapping),
            2 => Some(Self::Pixel),
            3 => Some(Self::Todo),
            4 => Some(Self::Drawer),
            _ => None,
        }
    }
}

/// 对应 C# ItemKind 枚举
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[repr(i32)]
pub enum ItemKind {
    File = 0,
    Directory = 1,
}

impl ItemKind {
    pub fn from_i32(v: i32) -> Option<Self> {
        match v {
            0 => Some(Self::File),
            1 => Some(Self::Directory),
            _ => None,
        }
    }
}

/// 通用错误类型
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AppError {
    pub code: i32,
    pub message: String,
}

impl AppError {
    pub fn new(code: i32, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
        }
    }

    pub fn not_found(msg: impl Into<String>) -> Self {
        Self::new(1, msg)
    }

    pub fn invalid_arg(msg: impl Into<String>) -> Self {
        Self::new(2, msg)
    }

    pub fn io_error(msg: impl Into<String>) -> Self {
        Self::new(3, msg)
    }

    pub fn db_error(msg: impl Into<String>) -> Self {
        Self::new(4, msg)
    }
}

impl std::fmt::Display for AppError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "[{}] {}", self.code, self.message)
    }
}

impl std::error::Error for AppError {}

impl From<rusqlite::Error> for AppError {
    fn from(e: rusqlite::Error) -> Self {
        AppError::db_error(e.to_string())
    }
}

impl From<std::io::Error> for AppError {
    fn from(e: std::io::Error) -> Self {
        AppError::io_error(e.to_string())
    }
}

pub type AppResult<T> = Result<T, AppError>;

/// 用于 FFI 的 JSON 响应包装
#[derive(Debug, Serialize, Deserialize)]
pub struct FfiResponse<T: Serialize> {
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub data: Option<T>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
}

impl<T: Serialize> FfiResponse<T> {
    pub fn success(data: T) -> Self {
        Self {
            ok: true,
            data: Some(data),
            error: None,
        }
    }

    pub fn failure(msg: impl Into<String>) -> Self {
        Self {
            ok: false,
            data: None,
            error: Some(msg.into()),
        }
    }

    pub fn to_json(&self) -> String {
        serde_json::to_string(self)
            .unwrap_or_else(|_| r#"{"ok":false,"error":"serialization failed"}"#.to_string())
    }
}

pub fn parse_uuid(s: &str) -> AppResult<Uuid> {
    Uuid::parse_str(s).map_err(|e| AppError::invalid_arg(format!("Invalid UUID: {}", e)))
}
