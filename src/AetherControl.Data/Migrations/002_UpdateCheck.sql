-- Opt-in startup update check (default off: no network call the user didn't ask for).
ALTER TABLE app_settings ADD COLUMN check_for_updates_on_startup INTEGER NOT NULL DEFAULT 0;
