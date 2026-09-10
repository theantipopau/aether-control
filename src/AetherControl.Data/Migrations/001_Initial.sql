CREATE TABLE IF NOT EXISTS schema_version (
    version     INTEGER PRIMARY KEY,
    applied_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS app_settings (
    id                          INTEGER PRIMARY KEY CHECK (id = 1),
    theme                       TEXT NOT NULL DEFAULT 'Dark',
    accent                      TEXT NOT NULL DEFAULT 'Cyan',
    dashboard_refresh_ms        INTEGER NOT NULL DEFAULT 1000,
    start_with_windows          INTEGER NOT NULL DEFAULT 0,
    start_minimised_to_tray     INTEGER NOT NULL DEFAULT 1,
    minimise_to_tray_on_close   INTEGER NOT NULL DEFAULT 1,
    history_retention_days      INTEGER NOT NULL DEFAULT 90,
    logging_enabled             INTEGER NOT NULL DEFAULT 1,
    log_level                   TEXT NOT NULL DEFAULT 'Information'
);

INSERT OR IGNORE INTO app_settings (id) VALUES (1);

CREATE TABLE IF NOT EXISTS tray_metric_preferences (
    metric      TEXT PRIMARY KEY,
    enabled     INTEGER NOT NULL DEFAULT 0,
    sort_order  INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO tray_metric_preferences (metric, enabled, sort_order) VALUES
    ('CpuTemperature', 1, 0),
    ('GpuTemperature', 1, 1),
    ('RamUtilisationPercent', 1, 2),
    ('NetworkDownloadKbps', 0, 3);

CREATE TABLE IF NOT EXISTS portrait_layouts (
    id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    name                    TEXT NOT NULL,
    type                    TEXT NOT NULL,
    always_on_top           INTEGER NOT NULL DEFAULT 1,
    transparent_background  INTEGER NOT NULL DEFAULT 0,
    oled_friendly           INTEGER NOT NULL DEFAULT 0,
    refresh_rate_ms         INTEGER NOT NULL DEFAULT 1000,
    widget_layout_json      TEXT NOT NULL DEFAULT '[]',
    is_built_in             INTEGER NOT NULL DEFAULT 0
);

INSERT OR IGNORE INTO portrait_layouts (id, name, type, always_on_top, transparent_background, oled_friendly, refresh_rate_ms, widget_layout_json, is_built_in) VALUES
    (1, 'Compact', 'Compact', 1, 0, 0, 1000, '[]', 1),
    (2, 'Statistical', 'Statistical', 1, 0, 0, 1000, '[]', 1),
    (3, 'Showcase', 'Showcase', 0, 0, 0, 500, '[]', 1);

CREATE TABLE IF NOT EXISTS sensor_history (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp_utc   TEXT NOT NULL,
    metric          TEXT NOT NULL,
    value           REAL NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_sensor_history_metric_time
    ON sensor_history (metric, timestamp_utc);

CREATE TABLE IF NOT EXISTS sensor_history_rollup (
    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    bucket_start_utc    TEXT NOT NULL,
    resolution          TEXT NOT NULL,
    metric              TEXT NOT NULL,
    avg_value           REAL NOT NULL,
    min_value           REAL NOT NULL,
    max_value           REAL NOT NULL,
    sample_count        INTEGER NOT NULL,
    UNIQUE (bucket_start_utc, resolution, metric)
);

CREATE INDEX IF NOT EXISTS ix_rollup_metric_resolution_time
    ON sensor_history_rollup (metric, resolution, bucket_start_utc);

CREATE TABLE IF NOT EXISTS rgb_device_presets (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    device_id       TEXT NOT NULL,
    name            TEXT NOT NULL,
    color_hex       TEXT NOT NULL DEFAULT '#00E5FF',
    brightness      INTEGER NOT NULL DEFAULT 100,
    effect_name     TEXT NOT NULL DEFAULT 'Static'
);

CREATE TABLE IF NOT EXISTS startup_entry_overrides (
    command_key     TEXT PRIMARY KEY,
    is_enabled      INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS optimisation_run_log (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    task_id                     TEXT NOT NULL,
    ran_at_utc                  TEXT NOT NULL,
    success                     INTEGER NOT NULL,
    message                     TEXT NOT NULL DEFAULT '',
    bytes_reclaimed             INTEGER,
    restore_point_description   TEXT
);

CREATE TABLE IF NOT EXISTS plugins (
    id                  TEXT PRIMARY KEY,
    enabled             INTEGER NOT NULL DEFAULT 1,
    installed_at_utc    TEXT NOT NULL
);

INSERT OR IGNORE INTO schema_version (version, applied_at) VALUES (1, datetime('now'));
