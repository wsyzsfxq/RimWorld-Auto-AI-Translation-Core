-- AI Translation Network Cloud Worker schema
-- Designed for Cloudflare D1.
-- Use this for a brand-new empty D1 database.
-- For an existing production database, use migrations/ and d1-tools/ instead.

CREATE TABLE IF NOT EXISTS TranslationRegistry (
  RecordId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  Language TEXT NOT NULL,
  ModName TEXT NOT NULL DEFAULT '',
  LatestVersion TEXT NOT NULL DEFAULT 'Unknown',
  LastUpdated TEXT NOT NULL,
  ModLastUpdated TEXT NOT NULL,
  UploaderID TEXT NOT NULL DEFAULT 'Anonymous',
  Author TEXT NOT NULL DEFAULT 'AI Translation Network',
  TranslationType TEXT NOT NULL DEFAULT 'AI_Auto',
  IsVerified INTEGER NOT NULL DEFAULT 0,
  FileUrl TEXT NOT NULL,
  TargetModVersion TEXT NOT NULL DEFAULT 'Unknown',
  TranslationDate TEXT NOT NULL,
  IsSmartMerged INTEGER NOT NULL DEFAULT 0,
  MergedAiCount INTEGER NOT NULL DEFAULT 0,
  UpdateLog TEXT NOT NULL DEFAULT '',
  IsDeleted INTEGER NOT NULL DEFAULT 0,
  FileSha256 TEXT,
  FileSize INTEGER,
  DownloadCount INTEGER NOT NULL DEFAULT 0,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_registry_public
  ON TranslationRegistry (IsDeleted, Language, PackageId, LastUpdated);

CREATE INDEX IF NOT EXISTS idx_registry_record_lookup
  ON TranslationRegistry (RecordId, IsDeleted);

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

-- RWMod catalog tables from migrations/0004_add_rwmod_catalog_tables.sql
-- Safe for an existing D1 database.
-- Adds RWMod catalog tables only.
-- Existing TranslationRegistry, FeedbackReports, privilege-code rows, R2 files,
-- and existing API behavior are not updated, deleted, or replaced.

CREATE TABLE IF NOT EXISTS RWModMods (
  PackageId TEXT PRIMARY KEY,
  DisplayPackageId TEXT NOT NULL DEFAULT '',
  ModName TEXT NOT NULL DEFAULT '',
  Summary TEXT NOT NULL DEFAULT '',
  Author TEXT NOT NULL DEFAULT '',
  PrimaryWorkshopId TEXT NOT NULL DEFAULT '',
  SupportedGameVersionsJson TEXT NOT NULL DEFAULT '[]',
  LatestKnownVersion TEXT NOT NULL DEFAULT 'Unknown',
  LastWorkshopUpdated TEXT,
  LastRegistryUpdated TEXT,
  LocalizationStatus TEXT NOT NULL DEFAULT 'unknown',
  CompatibilityStatus TEXT NOT NULL DEFAULT 'unknown',
  PerformanceImpact TEXT NOT NULL DEFAULT 'unknown',
  TrustLevel TEXT NOT NULL DEFAULT 'unknown',
  Confidence TEXT NOT NULL DEFAULT 'low',
  IsListed INTEGER NOT NULL DEFAULT 1,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_rwmod_mods_name
  ON RWModMods (ModName);

CREATE INDEX IF NOT EXISTS idx_rwmod_mods_author
  ON RWModMods (Author);

CREATE INDEX IF NOT EXISTS idx_rwmod_mods_workshop
  ON RWModMods (PrimaryWorkshopId);

CREATE INDEX IF NOT EXISTS idx_rwmod_mods_status
  ON RWModMods (IsListed, LocalizationStatus, CompatibilityStatus, PerformanceImpact);

CREATE INDEX IF NOT EXISTS idx_rwmod_mods_registry_updated
  ON RWModMods (LastRegistryUpdated);

CREATE TABLE IF NOT EXISTS RWModModSources (
  SourceId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  SourceType TEXT NOT NULL DEFAULT 'other',
  Url TEXT NOT NULL,
  Label TEXT NOT NULL DEFAULT '',
  SourceOwner TEXT NOT NULL DEFAULT '',
  Language TEXT NOT NULL DEFAULT '',
  IsPrimary INTEGER NOT NULL DEFAULT 0,
  TrustLevel TEXT NOT NULL DEFAULT 'unknown',
  IsVisible INTEGER NOT NULL DEFAULT 1,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_rwmod_sources_package
  ON RWModModSources (PackageId, SourceType, IsVisible);

CREATE INDEX IF NOT EXISTS idx_rwmod_sources_url
  ON RWModModSources (Url);

CREATE TABLE IF NOT EXISTS RWModLocalizationStatus (
  LocalizationId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  Language TEXT NOT NULL,
  LocaleLabel TEXT NOT NULL DEFAULT '',
  Status TEXT NOT NULL DEFAULT 'unknown',
  SourceType TEXT NOT NULL DEFAULT 'unknown',
  TrustLevel TEXT NOT NULL DEFAULT 'unknown',
  Confidence TEXT NOT NULL DEFAULT 'low',
  RegistryRecordId TEXT,
  TranslationType TEXT NOT NULL DEFAULT '',
  IsVerified INTEGER NOT NULL DEFAULT 0,
  CoveragePercent INTEGER,
  TranslatedKeyCount INTEGER,
  MissingKeyCount INTEGER,
  OutdatedKeyCount INTEGER,
  TargetModVersion TEXT NOT NULL DEFAULT 'Unknown',
  SourceUrl TEXT NOT NULL DEFAULT '',
  ContributorName TEXT NOT NULL DEFAULT '',
  Notes TEXT NOT NULL DEFAULT '',
  IsVisible INTEGER NOT NULL DEFAULT 1,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_rwmod_localization_package_language
  ON RWModLocalizationStatus (PackageId, Language, IsVisible, Status);

CREATE INDEX IF NOT EXISTS idx_rwmod_localization_record
  ON RWModLocalizationStatus (RegistryRecordId);

CREATE INDEX IF NOT EXISTS idx_rwmod_localization_status
  ON RWModLocalizationStatus (Language, Status, TrustLevel, Confidence);

CREATE TABLE IF NOT EXISTS RWModDependencies (
  DependencyId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  DependencyPackageId TEXT NOT NULL DEFAULT '',
  DependencyName TEXT NOT NULL DEFAULT '',
  DependencyWorkshopId TEXT NOT NULL DEFAULT '',
  DependencyType TEXT NOT NULL DEFAULT 'required',
  GameVersion TEXT NOT NULL DEFAULT '',
  LoadOrderNote TEXT NOT NULL DEFAULT '',
  SourceType TEXT NOT NULL DEFAULT 'unknown',
  TrustLevel TEXT NOT NULL DEFAULT 'unknown',
  Confidence TEXT NOT NULL DEFAULT 'low',
  IsVisible INTEGER NOT NULL DEFAULT 1,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_rwmod_dependencies_package
  ON RWModDependencies (PackageId, DependencyType, IsVisible);

CREATE INDEX IF NOT EXISTS idx_rwmod_dependencies_dependency
  ON RWModDependencies (DependencyPackageId, DependencyWorkshopId);

CREATE TABLE IF NOT EXISTS RWModCompatibilityReports (
  CompatibilityId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  RelatedPackageId TEXT NOT NULL DEFAULT '',
  RelatedModName TEXT NOT NULL DEFAULT '',
  ReportType TEXT NOT NULL DEFAULT 'soft_conflict',
  Severity TEXT NOT NULL DEFAULT 'normal',
  PublicStatus TEXT NOT NULL DEFAULT 'unknown',
  GameVersion TEXT NOT NULL DEFAULT '',
  ModVersion TEXT NOT NULL DEFAULT '',
  ErrorPattern TEXT NOT NULL DEFAULT '',
  Summary TEXT NOT NULL DEFAULT '',
  Detail TEXT NOT NULL DEFAULT '',
  EvidenceUrl TEXT NOT NULL DEFAULT '',
  ReporterName TEXT NOT NULL DEFAULT 'Anonymous',
  SourceType TEXT NOT NULL DEFAULT 'player_report',
  TrustLevel TEXT NOT NULL DEFAULT 'player_report',
  Confidence TEXT NOT NULL DEFAULT 'low',
  VoteCount INTEGER NOT NULL DEFAULT 0,
  IsVerified INTEGER NOT NULL DEFAULT 0,
  IsPublic INTEGER NOT NULL DEFAULT 1,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ReviewedAt TEXT
);

CREATE INDEX IF NOT EXISTS idx_rwmod_compatibility_package
  ON RWModCompatibilityReports (PackageId, IsPublic, PublicStatus, Severity);

CREATE INDEX IF NOT EXISTS idx_rwmod_compatibility_related
  ON RWModCompatibilityReports (RelatedPackageId, ReportType);

CREATE INDEX IF NOT EXISTS idx_rwmod_compatibility_confidence
  ON RWModCompatibilityReports (TrustLevel, Confidence, IsVerified);

CREATE TABLE IF NOT EXISTS RWModPerformanceReports (
  PerformanceId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  Impact TEXT NOT NULL DEFAULT 'unknown',
  Severity TEXT NOT NULL DEFAULT 'normal',
  GameVersion TEXT NOT NULL DEFAULT '',
  ModVersion TEXT NOT NULL DEFAULT '',
  ModCount INTEGER,
  PawnCount INTEGER,
  ColonyAgeDays INTEGER,
  CpuModel TEXT NOT NULL DEFAULT '',
  Scenario TEXT NOT NULL DEFAULT 'other',
  MetricType TEXT NOT NULL DEFAULT 'player_report',
  TpsBefore REAL,
  TpsAfter REAL,
  LoadSeconds REAL,
  Summary TEXT NOT NULL DEFAULT '',
  Detail TEXT NOT NULL DEFAULT '',
  ReporterName TEXT NOT NULL DEFAULT 'Anonymous',
  SourceType TEXT NOT NULL DEFAULT 'player_report',
  TrustLevel TEXT NOT NULL DEFAULT 'player_report',
  Confidence TEXT NOT NULL DEFAULT 'low',
  VoteCount INTEGER NOT NULL DEFAULT 0,
  IsVerified INTEGER NOT NULL DEFAULT 0,
  IsPublic INTEGER NOT NULL DEFAULT 1,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  ReviewedAt TEXT
);

CREATE INDEX IF NOT EXISTS idx_rwmod_performance_package
  ON RWModPerformanceReports (PackageId, IsPublic, Impact, Severity);

CREATE INDEX IF NOT EXISTS idx_rwmod_performance_context
  ON RWModPerformanceReports (GameVersion, Scenario, Impact);

CREATE INDEX IF NOT EXISTS idx_rwmod_performance_confidence
  ON RWModPerformanceReports (TrustLevel, Confidence, IsVerified);

CREATE TABLE IF NOT EXISTS RWModGuideLinks (
  GuideId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  LinkType TEXT NOT NULL DEFAULT 'other',
  Url TEXT NOT NULL,
  Title TEXT NOT NULL DEFAULT '',
  AuthorName TEXT NOT NULL DEFAULT '',
  Language TEXT NOT NULL DEFAULT '',
  Platform TEXT NOT NULL DEFAULT '',
  IsOfficial INTEGER NOT NULL DEFAULT 0,
  TrustLevel TEXT NOT NULL DEFAULT 'unknown',
  IsVisible INTEGER NOT NULL DEFAULT 1,
  SortOrder INTEGER NOT NULL DEFAULT 0,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UpdatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_rwmod_guides_package
  ON RWModGuideLinks (PackageId, IsVisible, SortOrder);

CREATE INDEX IF NOT EXISTS idx_rwmod_guides_type
  ON RWModGuideLinks (LinkType, Language, Platform);

CREATE TABLE IF NOT EXISTS RWModAliases (
  AliasId TEXT PRIMARY KEY,
  PackageId TEXT NOT NULL,
  AliasText TEXT NOT NULL,
  AliasType TEXT NOT NULL DEFAULT 'other',
  Language TEXT NOT NULL DEFAULT '',
  SearchWeight INTEGER NOT NULL DEFAULT 10,
  SourceType TEXT NOT NULL DEFAULT 'unknown',
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_rwmod_aliases_text
  ON RWModAliases (AliasText);

CREATE INDEX IF NOT EXISTS idx_rwmod_aliases_package
  ON RWModAliases (PackageId, SearchWeight);

CREATE TABLE IF NOT EXISTS RWModModerationEvents (
  EventId TEXT PRIMARY KEY,
  TargetType TEXT NOT NULL,
  TargetId TEXT NOT NULL,
  PackageId TEXT NOT NULL DEFAULT '',
  ActorCodeId TEXT,
  Action TEXT NOT NULL,
  Reason TEXT NOT NULL DEFAULT '',
  MetadataJson TEXT NOT NULL DEFAULT '{}',
  IsPublic INTEGER NOT NULL DEFAULT 0,
  CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_rwmod_moderation_target
  ON RWModModerationEvents (TargetType, TargetId, CreatedAt);

CREATE INDEX IF NOT EXISTS idx_rwmod_moderation_package
  ON RWModModerationEvents (PackageId, CreatedAt);
