-- Safe for an existing D1 database.
-- Adds player feedback / bug report tables only. Existing TranslationRegistry,
-- R2 files, and privilege-code rows are not updated, deleted, or replaced.

CREATE TABLE IF NOT EXISTS FeedbackReports (
  FeedbackId TEXT PRIMARY KEY,
  Category TEXT NOT NULL DEFAULT 'other',
  Status TEXT NOT NULL DEFAULT 'open',
  Severity TEXT NOT NULL DEFAULT 'normal',
  Title TEXT NOT NULL,
  Summary TEXT NOT NULL DEFAULT '',
  Body TEXT NOT NULL DEFAULT '',
  PackageId TEXT NOT NULL DEFAULT '',
  ModName TEXT NOT NULL DEFAULT '',
  Language TEXT NOT NULL DEFAULT '',
  GameVersion TEXT NOT NULL DEFAULT '',
  ModVersion TEXT NOT NULL DEFAULT '',
  ReporterName TEXT NOT NULL DEFAULT 'Anonymous',
  ReporterContact TEXT NOT NULL DEFAULT '',
  IpHash TEXT,
  UserAgent TEXT NOT NULL DEFAULT '',
  VoteCount INTEGER NOT NULL DEFAULT 0,
  CommentCount INTEGER NOT NULL DEFAULT 0,
  IsPublic INTEGER NOT NULL DEFAULT 1,
  IsPinned INTEGER NOT NULL DEFAULT 0,
  AssignedTo TEXT NOT NULL DEFAULT '',
  DuplicateOf TEXT NOT NULL DEFAULT '',
  ResolutionNote TEXT NOT NULL DEFAULT '',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  LastActivityAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ResolvedAt TEXT
);

CREATE INDEX IF NOT EXISTS idx_feedback_public
  ON FeedbackReports (IsPublic, Status, Category, LastActivityAt);

CREATE INDEX IF NOT EXISTS idx_feedback_package
  ON FeedbackReports (PackageId, Language, LastActivityAt);

CREATE INDEX IF NOT EXISTS idx_feedback_pinned
  ON FeedbackReports (IsPinned, LastActivityAt);

CREATE TABLE IF NOT EXISTS FeedbackEvents (
  EventId TEXT PRIMARY KEY,
  FeedbackId TEXT NOT NULL,
  ActorCodeId TEXT,
  Action TEXT NOT NULL,
  MetadataJson TEXT NOT NULL DEFAULT '{}',
  PublicNote TEXT NOT NULL DEFAULT '',
  IsPublic INTEGER NOT NULL DEFAULT 0,
  IpHash TEXT,
  UserAgent TEXT NOT NULL DEFAULT '',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_feedback_events_report
  ON FeedbackEvents (FeedbackId, CreatedAt);
