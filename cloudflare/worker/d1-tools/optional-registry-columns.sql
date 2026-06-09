-- Optional TranslationRegistry upgrades.
--
-- Do NOT run this whole file blindly on production.
-- First run d1-tools/inspect-existing-registry.sql, then uncomment only the
-- ALTER TABLE lines for columns that are missing from PRAGMA table_info.
--
-- The current worker-v2.js keeps these columns optional so old uploads and
-- downloads can continue while you migrate safely.

-- ALTER TABLE TranslationRegistry ADD COLUMN FileSha256 TEXT;
-- ALTER TABLE TranslationRegistry ADD COLUMN FileSize INTEGER;
-- ALTER TABLE TranslationRegistry ADD COLUMN DownloadCount INTEGER NOT NULL DEFAULT 0;
-- ALTER TABLE TranslationRegistry ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;
-- ALTER TABLE TranslationRegistry ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP;

CREATE INDEX IF NOT EXISTS idx_registry_public
  ON TranslationRegistry (IsDeleted, Language, PackageId, LastUpdated);

CREATE INDEX IF NOT EXISTS idx_registry_record_lookup
  ON TranslationRegistry (RecordId, IsDeleted);
