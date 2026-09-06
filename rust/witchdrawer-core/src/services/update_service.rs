//! Update service — checks GitHub releases for new versions and applies them.

use std::fs;
use std::io::Read as _;
use std::path::{Path, PathBuf};

use reqwest::Client;
use serde::Deserialize;
use sha2::{Digest, Sha256};

use crate::models::{AppError, AppResult, UpdateCheckResult};

const GITHUB_OWNER: &str = "witchscottishfoldcat";
const GITHUB_REPO: &str = "WitchDrawer";
const GITHUB_API_URL: &str =
    "https://api.github.com/repos/witchscottishfoldcat/WitchDrawer/releases/latest";
const GITHUB_RELEASE_PAGE: &str =
    "https://github.com/witchscottishfoldcat/WitchDrawer/releases/latest";
const VERSION_TAG_PREFIX: &str = "v";

const UPDATE_ROOT_FOLDER_NAME: &str = "WitchDrawerUpdate";
const STARTUP_SUCCESS_MARKER_FILE_NAME: &str = "startup-succeeded.marker";
const STARTUP_SUCCESS_MARKER_ENV: &str = "WITCHDRAWER_STARTUP_SUCCESS_MARKER";

/// Check whether `s` is a 32-char hex GUID in the N-format (no dashes).
fn is_n_guid(s: &str) -> bool {
    s.len() == 32 && s.chars().all(|c| c.is_ascii_hexdigit())
}

/// Compare two paths for equality, ignoring trailing separators and case
/// (Windows). Mirrors the C# `Path.GetFullPath(...)` + `OrdinalIgnoreCase`
/// comparison used by `TryResolveStartupSuccessMarkerPath`.
fn paths_same(a: &Path, b: &Path) -> bool {
    fn norm(p: &Path) -> String {
        let mut s = p.to_string_lossy().replace('/', "\\");
        while s.ends_with('\\') {
            s.pop();
        }
        s
    }
    norm(a).eq_ignore_ascii_case(&norm(b))
}

// ---------------------------------------------------------------------------
// GitHub API response types
// ---------------------------------------------------------------------------

#[derive(Debug, Deserialize)]
struct GitHubReleaseResponse {
    #[serde(default)]
    tag_name: String,
    #[serde(default)]
    body: String,
    #[serde(default)]
    html_url: String,
    #[serde(default)]
    assets: Vec<GitHubAsset>,
}

#[derive(Debug, Deserialize)]
struct GitHubAsset {
    #[serde(default)]
    name: String,
    #[serde(default)]
    browser_download_url: String,
}

// ---------------------------------------------------------------------------
// Simple SemVer-like version for comparison
// ---------------------------------------------------------------------------

#[derive(Debug, Clone, PartialEq, Eq)]
struct Version {
    major: u32,
    minor: u32,
    patch: u32,
    build: u32,
}

impl Version {
    fn parse(s: &str) -> Option<Self> {
        let parts: Vec<&str> = s.split('.').collect();
        if parts.is_empty() {
            return None;
        }
        let major = parts[0].parse::<u32>().ok()?;
        let minor = parts.get(1).and_then(|p| p.parse().ok()).unwrap_or(0);
        let patch = parts.get(2).and_then(|p| p.parse().ok()).unwrap_or(0);
        let build = parts.get(3).and_then(|p| p.parse().ok()).unwrap_or(0);
        Some(Self {
            major,
            minor,
            patch,
            build,
        })
    }
}

impl PartialOrd for Version {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

impl Ord for Version {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        self.major
            .cmp(&other.major)
            .then_with(|| self.minor.cmp(&other.minor))
            .then_with(|| self.patch.cmp(&other.patch))
            .then_with(|| self.build.cmp(&other.build))
    }
}

// ---------------------------------------------------------------------------
// UpdateService
// ---------------------------------------------------------------------------

pub struct UpdateService {
    client: Client,
}

impl Default for UpdateService {
    fn default() -> Self {
        Self::new()
    }
}

impl UpdateService {
    pub fn new() -> Self {
        let client = Client::builder()
            .user_agent("WitchDrawer")
            .build()
            .expect("Failed to create HTTP client");
        Self { client }
    }

    // -- Check for update ---------------------------------------------------

    pub async fn check_for_update(&self, current_version: &str) -> AppResult<UpdateCheckResult> {
        let current = Version::parse(current_version).unwrap_or(Version {
            major: 0,
            minor: 0,
            patch: 0,
            build: 0,
        });

        let response: GitHubReleaseResponse = match self.client.get(GITHUB_API_URL).send().await {
            Ok(resp) => match resp.json::<GitHubReleaseResponse>().await {
                Ok(r) => r,
                Err(_) => return Ok(UpdateCheckResult::default()),
            },
            Err(_) => return Ok(UpdateCheckResult::default()),
        };

        if response.tag_name.is_empty() {
            return Ok(UpdateCheckResult::default());
        }

        let tag_text = response
            .tag_name
            .strip_prefix(VERSION_TAG_PREFIX)
            .unwrap_or(&response.tag_name);

        let remote = match Version::parse(tag_text) {
            Some(v) => v,
            None => {
                tracing::info!("Failed to parse remote version tag: {}", response.tag_name);
                return Ok(UpdateCheckResult::default());
            }
        };

        let has_update = remote > current;
        let (download_url, expected_sha256) = self.resolve_asset(&response.assets).await;

        let url = if download_url.is_empty() {
            if response.html_url.is_empty() {
                GITHUB_RELEASE_PAGE.to_string()
            } else {
                response.html_url
            }
        } else {
            download_url
        };

        Ok(UpdateCheckResult {
            has_update,
            latest_version: format!(
                "{}.{}.{}.{}",
                remote.major, remote.minor, remote.patch, remote.build
            ),
            release_notes: Self::truncate_release_notes(&response.body, 500),
            download_url: url,
            expected_sha256,
        })
    }

    // -- Download & apply ---------------------------------------------------

    /// Download the update package, verify its SHA-256 (when published), and
    /// hand the payload to a detached updater script.
    ///
    /// Mirrors the v1.1.9 C# flow:
    /// - each update gets its own `%TEMP%\WitchDrawerUpdate\{updateId}` root;
    /// - the zip is extracted into a `payload` subdirectory;
    /// - the updater `.bat` lives OUTSIDE the directory it removes, so cmd.exe
    ///   never loses its own script while cleaning the update payload;
    /// - all paths are passed to the script through environment variables and
    ///   progress is recorded in `%LocalAppData%\WitchDrawer\Logs\updater.log`.
    pub async fn download_and_apply_update(
        &self,
        download_url: &str,
        expected_sha256: Option<&str>,
    ) -> AppResult<bool> {
        if !Self::is_allowed_download_url(download_url) {
            tracing::info!("Rejected update download URL: {}", download_url);
            return Ok(false);
        }

        let update_id = uuid::Uuid::new_v4().simple().to_string();
        let temp_root = std::env::temp_dir()
            .join("WitchDrawerUpdate")
            .join(&update_id);
        let payload_dir = temp_root.join("payload");
        fs::create_dir_all(&payload_dir)?;
        let zip_path = temp_root.join("update.zip");

        // -- Download -------------------------------------------------------
        let response = self
            .client
            .get(download_url)
            .send()
            .await
            .map_err(|e| AppError::io_error(format!("Download failed: {}", e)))?;

        if !response.status().is_success() {
            let _ = fs::remove_dir_all(&temp_root);
            return Err(AppError::io_error(format!(
                "Download returned status {}",
                response.status()
            )));
        }

        let bytes = response
            .bytes()
            .await
            .map_err(|e| AppError::io_error(format!("Read error: {}", e)))?;
        fs::write(&zip_path, &bytes)?;

        // -- SHA-256 verification -------------------------------------------
        if let Some(expected) = expected_sha256 {
            let actual = Self::compute_sha256_hex(&zip_path)?;
            if !actual.eq_ignore_ascii_case(expected) {
                tracing::info!(
                    "Update hash mismatch. expected={} actual={}",
                    expected,
                    actual
                );
                let _ = fs::remove_dir_all(&temp_root);
                return Ok(false);
            }
        } else {
            tracing::info!(
                "Update asset has no published SHA-256; continuing with URL allowlist only."
            );
        }

        // -- Extract payload -------------------------------------------------
        let zip_file = match fs::File::open(&zip_path) {
            Ok(f) => f,
            Err(e) => {
                let _ = fs::remove_dir_all(&temp_root);
                return Err(AppError::io_error(format!(
                    "Failed to open update zip: {}",
                    e
                )));
            }
        };
        let mut archive = match zip::ZipArchive::new(zip_file) {
            Ok(a) => a,
            Err(e) => {
                let _ = fs::remove_dir_all(&temp_root);
                return Err(AppError::io_error(format!("Failed to open zip: {}", e)));
            }
        };
        if let Err(e) = archive.extract(&payload_dir) {
            let _ = fs::remove_dir_all(&temp_root);
            return Err(AppError::io_error(format!("Failed to extract zip: {}", e)));
        }

        // -- Validate the running executable is present in the payload -------
        let app_executable_path = match std::env::current_exe() {
            Ok(p) => p,
            Err(_) => {
                let _ = fs::remove_dir_all(&temp_root);
                tracing::info!(
                    "Cannot apply update because the current executable path is unavailable."
                );
                return Ok(false);
            }
        };
        let app_executable_path = match std::fs::canonicalize(&app_executable_path) {
            Ok(p) => p,
            Err(_) => app_executable_path,
        };
        let app_dir = match app_executable_path.parent() {
            Some(p) => p.to_path_buf(),
            None => {
                let _ = fs::remove_dir_all(&temp_root);
                tracing::info!("Cannot apply update because the application directory is invalid.");
                return Ok(false);
            }
        };
        let executable_name = match app_executable_path.file_name().and_then(|n| n.to_str()) {
            Some(n) => n.to_string(),
            None => {
                let _ = fs::remove_dir_all(&temp_root);
                tracing::info!("Cannot apply update because the executable name is unavailable.");
                return Ok(false);
            }
        };

        if !payload_dir.join(&executable_name).exists() {
            tracing::info!("Downloaded update does not contain {}.", executable_name);
            let _ = fs::remove_dir_all(&temp_root);
            return Ok(false);
        }

        // -- Write the updater script outside the removed directory ----------
        let updater_path =
            std::env::temp_dir().join(format!("WitchDrawerUpdater-{}.bat", update_id));
        let update_log_path = local_app_data_dir()
            .map(|base| base.join("WitchDrawer").join("Logs").join("updater.log"))
            .unwrap_or_else(|| std::env::temp_dir().join("WitchDrawer-updater.log"));
        if let Some(log_dir) = update_log_path.parent() {
            let _ = fs::create_dir_all(log_dir);
        }

        let script = build_updater_script();
        if let Err(e) = fs::write(&updater_path, script) {
            let _ = fs::remove_dir_all(&temp_root);
            let _ = fs::remove_file(&updater_path);
            return Err(AppError::io_error(format!(
                "Failed to write updater script: {}",
                e
            )));
        }

        // -- Launch the updater (Windows only) -------------------------------
        #[cfg(target_os = "windows")]
        {
            let mut start_info = create_updater_start_info(
                &updater_path,
                &temp_root,
                &payload_dir,
                &app_dir,
                &app_executable_path,
                &executable_name,
                &update_log_path,
            );

            let spawned = start_info.spawn();

            if let Err(e) = spawned {
                let _ = fs::remove_dir_all(&temp_root);
                let _ = fs::remove_file(&updater_path);
                tracing::info!("Failed to start the update helper process: {}", e);
                return Ok(false);
            }
        }

        #[cfg(not(target_os = "windows"))]
        {
            // Non-Windows: nothing to launch; keep the computed path used.
            let _ = update_log_path;
        }

        Ok(true)
    }

    /// Remove legacy `update.zip` / `updater.bat` residue from the application
    /// directory (older updater versions left these files behind).
    /// Returns the number of removed artifacts.
    pub fn cleanup_legacy_updater_artifacts(app_directory: &Path) -> u32 {
        let mut removed_count = 0;
        for file_name in ["update.zip", "updater.bat"] {
            let candidate = app_directory.join(file_name);
            if candidate.is_file() {
                match fs::remove_file(&candidate) {
                    Ok(()) => removed_count += 1,
                    Err(e) => {
                        tracing::warn!(
                            "Failed to remove legacy updater artifact {}: {}",
                            candidate.display(),
                            e
                        );
                    }
                }
            }
        }
        removed_count
    }

    /// Confirm that the application started successfully after a self-update.
    /// Writes the startup-success marker to the path supplied via the
    /// `WITCHDRAWER_STARTUP_SUCCESS_MARKER` environment variable (set by the
    /// updater). The path is validated to be inside the temp update session
    /// directory before writing. Mirrors the C# `ConfirmUpdateStartupAsync`.
    pub fn confirm_update_startup(&self) -> AppResult<bool> {
        let marker_path = match std::env::var(STARTUP_SUCCESS_MARKER_ENV) {
            Ok(p) if !p.trim().is_empty() => p,
            _ => return Ok(false),
        };

        let full = match std::path::absolute(Path::new(&marker_path)) {
            Ok(p) => p,
            Err(e) => {
                return Err(AppError::io_error(format!(
                    "invalid startup marker path: {}",
                    e
                )))
            }
        };

        // The marker file must be named `startup-succeeded.marker`.
        let file_name = full.file_name().and_then(|n| n.to_str()).unwrap_or("");
        if !file_name.eq_ignore_ascii_case(STARTUP_SUCCESS_MARKER_FILE_NAME) {
            return Ok(false);
        }

        // Its parent directory must be `<temp>/WitchDrawerUpdate/<uuid-N>`.
        let session_dir = match full.parent() {
            Some(d) => d,
            None => return Ok(false),
        };
        let session_name = session_dir
            .file_name()
            .and_then(|n| n.to_str())
            .unwrap_or("");
        if !is_n_guid(session_name) || !session_dir.exists() {
            return Ok(false);
        }
        let expected_parent = std::env::temp_dir().join(UPDATE_ROOT_FOLDER_NAME);
        if !session_dir
            .parent()
            .map(|p| paths_same(p, &expected_parent))
            .unwrap_or(false)
        {
            return Ok(false);
        }

        let timestamp = chrono::Utc::now().to_rfc3339();
        fs::write(&full, timestamp)
            .map_err(|e| AppError::io_error(format!("failed to write startup marker: {}", e)))?;
        Ok(true)
    }

    // =======================================================================
    // Internal helpers
    // =======================================================================

    /// Check whether a download URL points to an allowed host.
    pub fn is_allowed_download_url(url: &str) -> bool {
        let parsed = match reqwest::Url::parse(url) {
            Ok(u) => u,
            Err(_) => return false,
        };

        if parsed.scheme() != "https" {
            return false;
        }

        let host = match parsed.host_str() {
            Some(h) => h.to_lowercase(),
            None => return false,
        };

        if host == "github.com" {
            let path = parsed.path().to_lowercase();
            return path.contains(&format!(
                "/{}/{}/",
                GITHUB_OWNER.to_lowercase(),
                GITHUB_REPO.to_lowercase()
            ));
        }

        if host == "objects.githubusercontent.com"
            || host == "release-assets.githubusercontent.com"
            || host == "uploads.github.com"
            || host.ends_with(".githubusercontent.com")
        {
            return true;
        }

        false
    }

    /// Find the best asset in the release assets list.
    async fn resolve_asset(&self, assets: &[GitHubAsset]) -> (String, Option<String>) {
        if assets.is_empty() {
            return (String::new(), None);
        }

        let arch_keyword = if cfg!(target_arch = "aarch64") {
            "arm64"
        } else {
            "x64"
        };

        // Prefer .zip files matching the architecture.
        let zip_assets: Vec<&GitHubAsset> = assets
            .iter()
            .filter(|a| a.name.to_lowercase().ends_with(".zip"))
            .collect();

        let best = zip_assets
            .iter()
            .find(|a| a.name.to_lowercase().contains(arch_keyword))
            .copied()
            .or_else(|| zip_assets.first().copied())
            .or_else(|| {
                assets
                    .iter()
                    .find(|a| a.name.to_lowercase().contains(arch_keyword))
            })
            .or_else(|| assets.first());

        let match_asset = match best {
            Some(a) => a,
            None => return (String::new(), None),
        };

        if match_asset.browser_download_url.is_empty() {
            return (String::new(), None);
        }

        if !Self::is_allowed_download_url(&match_asset.browser_download_url) {
            tracing::info!(
                "Rejected release asset URL: {}",
                match_asset.browser_download_url
            );
            return (String::new(), None);
        }

        let sha256 = self.try_resolve_sha256(assets, match_asset).await;
        (match_asset.browser_download_url.clone(), sha256)
    }

    /// Try to find a SHA-256 checksum for the package asset.
    async fn try_resolve_sha256(
        &self,
        assets: &[GitHubAsset],
        package: &GitHubAsset,
    ) -> Option<String> {
        let pkg_lower = package.name.to_lowercase();

        // Companion .sha256 / .sha256.txt
        let companion = assets.iter().find(|a| {
            let n = a.name.to_lowercase();
            n == format!("{}.sha256", pkg_lower) || n == format!("{}.sha256.txt", pkg_lower)
        });

        if let Some(c) = companion {
            if Self::is_allowed_download_url(&c.browser_download_url) {
                return self
                    .read_sha256_from_asset(&c.browser_download_url, &package.name)
                    .await;
            }
        }

        // SHA256SUMS / checksums.txt
        let checksums = assets.iter().find(|a| {
            let n = a.name.to_lowercase();
            n == "sha256sums" || n == "checksums.txt" || n.ends_with(".sha256sums")
        });

        if let Some(c) = checksums {
            if Self::is_allowed_download_url(&c.browser_download_url) {
                return self
                    .read_sha256_from_asset(&c.browser_download_url, &package.name)
                    .await;
            }
        }

        None
    }

    /// Parse a checksum text file and extract the hash for `package_name`.
    async fn read_sha256_from_asset(&self, url: &str, package_name: &str) -> Option<String> {
        let text = self.client.get(url).send().await.ok()?.text().await.ok()?;

        for raw_line in text.lines() {
            let line = raw_line.trim();
            if line.is_empty() || line.starts_with('#') {
                continue;
            }

            let parts: Vec<&str> = line.split_whitespace().collect();
            if parts.is_empty() {
                continue;
            }

            let candidate_hash = parts[0].trim_start_matches('*');
            if !is_valid_sha256_hex(candidate_hash) {
                continue;
            }

            if parts.len() == 1 {
                return Some(candidate_hash.to_lowercase());
            }

            let file_name = parts.last().unwrap().trim_start_matches('*');
            if file_name.eq_ignore_ascii_case(package_name)
                || Path::new(file_name)
                    .file_name()
                    .and_then(|f| f.to_str())
                    .map(|f| f.eq_ignore_ascii_case(package_name))
                    .unwrap_or(false)
            {
                return Some(candidate_hash.to_lowercase());
            }
        }

        None
    }

    /// Compute the lowercase hex SHA-256 of a file.
    fn compute_sha256_hex(path: &Path) -> AppResult<String> {
        let mut file = fs::File::open(path)?;
        let mut hasher = Sha256::new();
        let mut buffer = vec![0u8; 81920];
        loop {
            let n = file.read(&mut buffer)?;
            if n == 0 {
                break;
            }
            hasher.update(&buffer[..n]);
        }
        Ok(hex::encode(hasher.finalize()))
    }

    /// Truncate release notes to `max_len` characters.
    fn truncate_release_notes(body: &str, max_len: usize) -> String {
        let clean = body.replace("\r\n", "\n").trim().to_string();
        if clean.len() <= max_len {
            clean
        } else {
            format!("{}...", &clean[..max_len])
        }
    }
}

/// Check if a string is a valid 64-character hex SHA-256 hash.
fn is_valid_sha256_hex(s: &str) -> bool {
    s.len() == 64 && s.chars().all(|c| c.is_ascii_hexdigit())
}

// ---------------------------------------------------------------------------
// Updater script & process helpers (module-level, mirror C# internal statics)
// ---------------------------------------------------------------------------

/// Build the detached updater batch script. All paths are read from
/// environment variables so the script itself never hard-codes a directory.
pub(crate) fn build_updater_script() -> String {
    r#"@echo off
setlocal
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update started.
timeout /t 2 /nobreak >nul

taskkill /im "%WITCHDRAWER_EXE_NAME%" /f >nul 2>&1
timeout /t 1 /nobreak >nul

xcopy "%WITCHDRAWER_PAYLOAD%\*" "%WITCHDRAWER_APP_DIR%" /e /y /i >>"%WITCHDRAWER_UPDATE_LOG%" 2>&1
if errorlevel 1 goto update_failed

del /q "%WITCHDRAWER_APP_DIR%\update.zip" "%WITCHDRAWER_APP_DIR%\updater.bat" >nul 2>&1

start "" /b /d "%WITCHDRAWER_APP_DIR%" "%WITCHDRAWER_APP_EXE%" >nul 2>&1
if errorlevel 1 goto update_failed

>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update completed.
cd /d "%TEMP%"
rmdir /s /q "%WITCHDRAWER_UPDATE_ROOT%" >nul 2>&1
start "" /b "%ComSpec%" /d /c del /q "%~f0" >nul 2>&1 & exit /b 0

:update_failed
>>"%WITCHDRAWER_UPDATE_LOG%" echo [%date% %time%] Update failed with exit code %errorlevel%.
exit /b 1
"#
    .to_string()
}

/// Build a `cmd.exe` command that runs the updater script with all paths
/// passed through environment variables.
#[cfg(target_os = "windows")]
pub(crate) fn create_updater_start_info(
    updater_path: &Path,
    temp_root: &Path,
    payload_directory: &Path,
    app_directory: &Path,
    app_executable_path: &Path,
    executable_name: &str,
    update_log_path: &Path,
) -> std::process::Command {
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x08000000;

    let mut command = std::process::Command::new("cmd.exe");
    command
        .args(["/d", "/s", "/c", &format!("\"{}\"", updater_path.display())])
        .current_dir(updater_path.parent().unwrap_or_else(|| Path::new(".")))
        .env("WITCHDRAWER_UPDATE_ROOT", temp_root)
        .env("WITCHDRAWER_PAYLOAD", payload_directory)
        .env("WITCHDRAWER_APP_DIR", app_directory)
        .env("WITCHDRAWER_APP_EXE", app_executable_path)
        .env("WITCHDRAWER_EXE_NAME", executable_name)
        .env("WITCHDRAWER_UPDATE_LOG", update_log_path)
        .creation_flags(CREATE_NO_WINDOW);
    command
}

/// Resolve `%LocalAppData%` (Windows) or fall back to `$HOME/.local/share`.
fn local_app_data_dir() -> Option<PathBuf> {
    #[cfg(target_os = "windows")]
    {
        if let Some(path) = std::env::var_os("LOCALAPPDATA") {
            return Some(PathBuf::from(path));
        }
    }

    if let Some(home) = std::env::var_os("HOME") {
        return Some(PathBuf::from(home).join(".local").join("share"));
    }

    None
}

impl Default for UpdateCheckResult {
    fn default() -> Self {
        Self {
            has_update: false,
            latest_version: "0.0.0.0".to_string(),
            release_notes: String::new(),
            download_url: String::new(),
            expected_sha256: None,
        }
    }
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn version_parse_basic() {
        let v = Version::parse("1.2.3").unwrap();
        assert_eq!(
            v,
            Version {
                major: 1,
                minor: 2,
                patch: 3,
                build: 0
            }
        );
    }

    #[test]
    fn version_parse_four_part() {
        let v = Version::parse("1.2.3.4").unwrap();
        assert_eq!(
            v,
            Version {
                major: 1,
                minor: 2,
                patch: 3,
                build: 4
            }
        );
    }

    #[test]
    fn version_compare() {
        let a = Version::parse("1.2.3").unwrap();
        let b = Version::parse("1.2.4").unwrap();
        assert!(a < b);
        assert!(b > a);
    }

    #[test]
    fn version_compare_equal() {
        let a = Version::parse("2.0.0").unwrap();
        let b = Version::parse("2.0.0").unwrap();
        assert_eq!(a, b);
    }

    #[test]
    fn version_compare_major() {
        let a = Version::parse("1.9.9").unwrap();
        let b = Version::parse("2.0.0").unwrap();
        assert!(a < b);
    }

    #[test]
    fn allowed_url_github() {
        assert!(UpdateService::is_allowed_download_url(
            "https://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0/app.zip"
        ));
    }

    #[test]
    fn allowed_url_objects_githubusercontent() {
        assert!(UpdateService::is_allowed_download_url(
            "https://objects.githubusercontent.com/github-production-release-asset-2e65be/12345/abc.zip"
        ));
    }

    #[test]
    fn allowed_url_release_assets_githubusercontent() {
        assert!(UpdateService::is_allowed_download_url(
            "https://release-assets.githubusercontent.com/12345/abc.zip"
        ));
    }

    #[test]
    fn allowed_url_subdomain_githubusercontent() {
        assert!(UpdateService::is_allowed_download_url(
            "https://uploads.github.com/witchscottishfoldcat/WitchDrawer/releases/1/assets/abc.zip"
        ));
    }

    #[test]
    fn rejected_url_http() {
        assert!(!UpdateService::is_allowed_download_url(
            "http://github.com/witchscottishfoldcat/WitchDrawer/releases/download/v1.0/app.zip"
        ));
    }

    #[test]
    fn rejected_url_unknown_host() {
        assert!(!UpdateService::is_allowed_download_url(
            "https://evil.com/malware.zip"
        ));
    }

    #[test]
    fn rejected_url_malformed() {
        assert!(!UpdateService::is_allowed_download_url("not a url"));
    }

    #[test]
    fn sha256_hex_validation() {
        assert!(is_valid_sha256_hex(&"a".repeat(64)));
        assert!(!is_valid_sha256_hex("not_a_hash"));
        assert!(!is_valid_sha256_hex("abc"));
        assert!(!is_valid_sha256_hex(&"a".repeat(63)));
        assert!(!is_valid_sha256_hex(&"a".repeat(65)));
    }

    #[test]
    fn truncate_notes() {
        let long = "a".repeat(600);
        let result = UpdateService::truncate_release_notes(&long, 500);
        assert_eq!(result.len(), 503); // 500 + "..."
        assert!(result.ends_with("..."));

        let short = "short";
        assert_eq!(UpdateService::truncate_release_notes(short, 500), "short");
    }

    #[test]
    fn truncate_notes_crlf() {
        let input = "line1\r\nline2\r\nline3";
        let result = UpdateService::truncate_release_notes(input, 500);
        assert_eq!(result, "line1\nline2\nline3");
    }

    #[test]
    fn updater_start_info_runs_hidden_cmd_from_script_directory_with_env_paths() {
        let temp_root = std::env::temp_dir()
            .join("WitchDrawer StartInfo Tests")
            .join(uuid::Uuid::new_v4().simple().to_string());
        let payload_directory = temp_root.join("payload");
        let app_directory = Path::new(r"D:\应用\WitchDrawer");
        let updater_path = temp_root.join("updater.bat");
        let app_executable_path = app_directory.join("WitchDrawer.App.exe");
        let log_path = Path::new(r"C:\Users\Test\AppData\Local\WitchDrawer\Logs\updater.log");

        let start_info = create_updater_start_info(
            &updater_path,
            &temp_root,
            &payload_directory,
            app_directory,
            &app_executable_path,
            "WitchDrawer.App.exe",
            log_path,
        );

        // cmd.exe + /d /s /c ""updater.bat"" (same shape as C# Arguments).
        let args: Vec<String> = start_info
            .get_args()
            .map(|s| s.to_string_lossy().into_owned())
            .collect();
        assert_eq!(
            args,
            vec![
                "/d".to_string(),
                "/s".to_string(),
                "/c".to_string(),
                format!("\"{}\"", updater_path.display())
            ]
        );

        // WorkingDirectory = script's directory.
        let expected_dir = updater_path.parent().unwrap();
        assert_eq!(start_info.get_current_dir(), Some(expected_dir));

        // All paths travel through environment variables, nothing hard-coded.
        let envs: std::collections::HashMap<String, String> = start_info
            .get_envs()
            .filter_map(|(key, value)| match (key.to_str(), value) {
                (Some(key_str), Some(value_os)) => value_os
                    .to_str()
                    .map(|value_str| (key_str.to_string(), value_str.to_string())),
                _ => None,
            })
            .collect();
        assert_eq!(envs["WITCHDRAWER_UPDATE_ROOT"], temp_root.to_string_lossy());
        assert_eq!(
            envs["WITCHDRAWER_PAYLOAD"],
            payload_directory.to_string_lossy()
        );
        assert_eq!(envs["WITCHDRAWER_APP_DIR"], app_directory.to_string_lossy());
        assert_eq!(
            envs["WITCHDRAWER_APP_EXE"],
            app_executable_path.to_string_lossy()
        );
        assert_eq!(envs["WITCHDRAWER_EXE_NAME"], "WitchDrawer.App.exe");
        assert_eq!(envs["WITCHDRAWER_UPDATE_LOG"], log_path.to_string_lossy());

        let _ = std::fs::remove_dir_all(&temp_root);
    }

    #[test]
    fn updater_script_copies_payload_and_cleans_legacy_artifacts() {
        let script = build_updater_script();

        // Copies payload (not the whole update root).
        assert!(script.contains("xcopy \"%WITCHDRAWER_PAYLOAD%\\*\" \"%WITCHDRAWER_APP_DIR%\""));
        assert!(!script.contains("xcopy \"%WITCHDRAWER_UPDATE_ROOT%\\*\""));

        // Cleans legacy residue.
        assert!(script.contains(
            "del /q \"%WITCHDRAWER_APP_DIR%\\update.zip\" \"%WITCHDRAWER_APP_DIR%\\updater.bat\""
        ));

        // Restarts the app from its own directory.
        assert!(
            script.contains("start \"\" /b /d \"%WITCHDRAWER_APP_DIR%\" \"%WITCHDRAWER_APP_EXE%\"")
        );

        // Removes the update root and self-deletes.
        assert!(script.contains("rmdir /s /q \"%WITCHDRAWER_UPDATE_ROOT%\""));
        assert!(script.contains("del /q \"%~f0\""));
    }

    #[test]
    fn cleanup_legacy_updater_artifacts_deletes_only_known_residue() {
        let test_root = std::env::temp_dir()
            .join("WitchDrawer Legacy Cleanup Tests")
            .join(uuid::Uuid::new_v4().simple().to_string());
        let app_directory = test_root.join("app");
        fs::create_dir_all(&app_directory).unwrap();

        let zip_path = app_directory.join("update.zip");
        let updater_path = app_directory.join("updater.bat");
        let app_path = app_directory.join("WitchDrawer.App.exe");
        fs::write(&zip_path, "legacy").unwrap();
        fs::write(&updater_path, "legacy").unwrap();
        fs::write(&app_path, "keep").unwrap();

        let removed_count = UpdateService::cleanup_legacy_updater_artifacts(&app_directory);

        assert_eq!(removed_count, 2);
        assert!(!zip_path.exists());
        assert!(!updater_path.exists());
        assert!(app_path.exists());

        let _ = fs::remove_dir_all(&test_root);
    }

    #[test]
    fn cleanup_legacy_updater_artifacts_ignores_missing_files() {
        let test_root = std::env::temp_dir()
            .join("WitchDrawer Legacy Cleanup Tests")
            .join(uuid::Uuid::new_v4().simple().to_string());
        let app_directory = test_root.join("app");
        fs::create_dir_all(&app_directory).unwrap();

        let removed_count = UpdateService::cleanup_legacy_updater_artifacts(&app_directory);

        assert_eq!(removed_count, 0);

        let _ = fs::remove_dir_all(&test_root);
    }
}
