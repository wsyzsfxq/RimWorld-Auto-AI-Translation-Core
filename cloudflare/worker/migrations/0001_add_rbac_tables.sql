-- Safe for an existing D1 database.
-- Creates only new RBAC/audit tables and indexes. Existing TranslationRegistry
-- rows are not updated, deleted, or replaced.

CREATE TABLE IF NOT EXISTS PrivilegeCodes (
  CodeId TEXT PRIMARY KEY,
  CodeHash TEXT NOT NULL UNIQUE,
  Label TEXT NOT NULL,
  OwnerName TEXT NOT NULL DEFAULT '',
  GroupName TEXT NOT NULL DEFAULT '',
  Role TEXT NOT NULL,
  ScopesJson TEXT NOT NULL,
  IsActive INTEGER NOT NULL DEFAULT 1,
  ExpiresAt TEXT,
  CreatedBy TEXT NOT NULL DEFAULT 'master',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  RevokedBy TEXT,
  RevokedAt TEXT,
  LastUsedAt TEXT,
  UsageCount INTEGER NOT NULL DEFAULT 0,
  Notes TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_privilege_codes_active
  ON PrivilegeCodes (IsActive, Role, ExpiresAt);

CREATE TABLE IF NOT EXISTS PrivilegeCodeEvents (
  EventId TEXT PRIMARY KEY,
  CodeId TEXT,
  ActorCodeId TEXT,
  Action TEXT NOT NULL,
  IpHash TEXT,
  UserAgent TEXT,
  MetadataJson TEXT NOT NULL DEFAULT '{}',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_privilege_events_code
  ON PrivilegeCodeEvents (CodeId, CreatedAt);

CREATE TABLE IF NOT EXISTS ModerationActions (
  ActionId TEXT PRIMARY KEY,
  RecordId TEXT,
  AdminId TEXT,
  Action TEXT NOT NULL,
  Reason TEXT NOT NULL DEFAULT '',
  MetadataJson TEXT NOT NULL DEFAULT '{}',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_moderation_record
  ON ModerationActions (RecordId, CreatedAt);

CREATE TABLE IF NOT EXISTS UploadEvents (
  EventId TEXT PRIMARY KEY,
  RecordId TEXT,
  PackageId TEXT,
  Language TEXT,
  UploaderIdHash TEXT,
  IpHash TEXT,
  UserAgent TEXT,
  Status TEXT NOT NULL,
  ErrorMessage TEXT NOT NULL DEFAULT '',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_upload_events_package
  ON UploadEvents (PackageId, Language, CreatedAt);

CREATE TABLE IF NOT EXISTS DownloadEvents (
  EventId TEXT PRIMARY KEY,
  RecordId TEXT,
  PackageId TEXT,
  Language TEXT,
  IpHash TEXT,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_download_events_package
  ON DownloadEvents (PackageId, Language, CreatedAt);
