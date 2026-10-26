-- RTSS overlay output (Phase 72). Off by default: writing into another app's overlay is opt-in.
ALTER TABLE app_settings ADD COLUMN rtss_overlay_enabled INTEGER NOT NULL DEFAULT 0;
