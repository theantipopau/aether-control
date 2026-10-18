-- First-run onboarding flag (default unseen: existing installs get the welcome dialog once).
ALTER TABLE app_settings ADD COLUMN has_seen_onboarding INTEGER NOT NULL DEFAULT 0;
