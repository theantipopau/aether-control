-- Portrait Mode skin id (default skin: existing installs keep current look).
ALTER TABLE app_settings ADD COLUMN portrait_skin TEXT NOT NULL DEFAULT 'Default';
