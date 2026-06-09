-- Safe for an existing D1 database.
-- Adds translation group key applications and private attachment metadata.
-- Existing TranslationRegistry, FeedbackReports, R2 objects, and privilege-code
-- rows are not updated, deleted, or replaced.

CREATE TABLE IF NOT EXISTS GroupKeyApplications (
  ApplicationId TEXT PRIMARY KEY,
  Status TEXT NOT NULL DEFAULT 'open',
  ApplicantName TEXT NOT NULL DEFAULT '',
  Contact TEXT NOT NULL DEFAULT '',
  GroupName TEXT NOT NULL DEFAULT '',
  GameVersion TEXT NOT NULL DEFAULT '',
  SteamWorkshopUrl TEXT NOT NULL DEFAULT '',
  PortfolioText TEXT NOT NULL DEFAULT '',
  ProofText TEXT NOT NULL DEFAULT '',
  ReviewerNote TEXT NOT NULL DEFAULT '',
  IssuedCodeId TEXT,
  IpHash TEXT,
  UserAgent TEXT NOT NULL DEFAULT '',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ReviewedAt TEXT,
  IssuedAt TEXT
);

CREATE INDEX IF NOT EXISTS idx_group_key_applications_status
  ON GroupKeyApplications (Status, CreatedAt);

CREATE TABLE IF NOT EXISTS GroupKeyApplicationEvents (
  EventId TEXT PRIMARY KEY,
  ApplicationId TEXT NOT NULL,
  ActorCodeId TEXT,
  Action TEXT NOT NULL,
  MetadataJson TEXT NOT NULL DEFAULT '{}',
  PublicNote TEXT NOT NULL DEFAULT '',
  IpHash TEXT,
  UserAgent TEXT NOT NULL DEFAULT '',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_group_key_application_events
  ON GroupKeyApplicationEvents (ApplicationId, CreatedAt);

CREATE TABLE IF NOT EXISTS RequestAttachments (
  AttachmentId TEXT PRIMARY KEY,
  OwnerType TEXT NOT NULL,
  OwnerId TEXT NOT NULL,
  FileName TEXT NOT NULL DEFAULT '',
  ContentType TEXT NOT NULL DEFAULT 'application/octet-stream',
  SizeBytes INTEGER NOT NULL DEFAULT 0,
  R2Key TEXT NOT NULL,
  IsPublic INTEGER NOT NULL DEFAULT 0,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_request_attachments_owner
  ON RequestAttachments (OwnerType, OwnerId, CreatedAt);
