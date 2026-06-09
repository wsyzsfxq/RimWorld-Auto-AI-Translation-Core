-- Seed RWMod catalog tables from TranslationRegistry.
-- Safe scope:
-- - reads TranslationRegistry
-- - upserts RWModMods
-- - upserts RWModLocalizationStatus rows whose id starts with registry:
-- - does not mutate TranslationRegistry, FeedbackReports, privilege tables, or R2 files
--
-- Run d1-tools/preview-rwmod-seed-from-registry.sql first and inspect the output.
-- Run after migrations/0004_add_rwmod_catalog_tables.sql.

WITH ActiveRegistry AS (
  SELECT
    RecordId,
    lower(trim(PackageId)) AS NormalizedPackageId,
    trim(PackageId) AS DisplayPackageId,
    trim(ModName) AS ModName,
    trim(Author) AS Author,
    trim(Language) AS Language,
    trim(TranslationType) AS TranslationType,
    COALESCE(IsVerified, 0) AS IsVerified,
    trim(TargetModVersion) AS TargetModVersion,
    trim(LatestVersion) AS LatestVersion,
    NULLIF(trim(LastUpdated), '') AS LastUpdated,
    NULLIF(trim(ModLastUpdated), '') AS ModLastUpdated,
    NULLIF(trim(TranslationDate), '') AS TranslationDate,
    trim(UploaderID) AS UploaderID
  FROM TranslationRegistry
  WHERE COALESCE(IsDeleted, 0) <> 1
    AND PackageId IS NOT NULL
    AND trim(PackageId) <> ''
),
RankedRegistry AS (
  SELECT
    *,
    ROW_NUMBER() OVER (
      PARTITION BY NormalizedPackageId
      ORDER BY
        IsVerified DESC,
        COALESCE(LastUpdated, TranslationDate, '') DESC,
        RecordId DESC
    ) AS PackageRank,
    MAX(IsVerified) OVER (PARTITION BY NormalizedPackageId) AS HasVerifiedRecord,
    MAX(CASE WHEN TargetModVersion LIKE '%1.5%' THEN 1 ELSE 0 END) OVER (PARTITION BY NormalizedPackageId) AS HasGameVersion15,
    MAX(CASE WHEN TargetModVersion LIKE '%1.6%' THEN 1 ELSE 0 END) OVER (PARTITION BY NormalizedPackageId) AS HasGameVersion16,
    MAX(COALESCE(LastUpdated, TranslationDate, '')) OVER (PARTITION BY NormalizedPackageId) AS LatestRegistryUpdate
  FROM ActiveRegistry
)
INSERT INTO RWModMods (
  PackageId,
  DisplayPackageId,
  ModName,
  Summary,
  Author,
  PrimaryWorkshopId,
  SupportedGameVersionsJson,
  LatestKnownVersion,
  LastWorkshopUpdated,
  LastRegistryUpdated,
  LocalizationStatus,
  CompatibilityStatus,
  PerformanceImpact,
  TrustLevel,
  Confidence,
  IsListed,
  CreatedAt,
  UpdatedAt
)
SELECT
  NormalizedPackageId,
  DisplayPackageId,
  COALESCE(NULLIF(ModName, ''), DisplayPackageId),
  '',
  COALESCE(NULLIF(Author, ''), ''),
  '',
  CASE
    WHEN HasGameVersion15 = 1 AND HasGameVersion16 = 1 THEN '["1.5","1.6"]'
    WHEN HasGameVersion16 = 1 THEN '["1.6"]'
    WHEN HasGameVersion15 = 1 THEN '["1.5"]'
    ELSE '[]'
  END,
  COALESCE(NULLIF(LatestVersion, ''), NULLIF(TargetModVersion, ''), 'Unknown'),
  ModLastUpdated,
  LatestRegistryUpdate,
  'partial',
  'unknown',
  'unknown',
  'cloud_record',
  CASE WHEN HasVerifiedRecord = 1 THEN 'medium' ELSE 'low' END,
  1,
  CURRENT_TIMESTAMP,
  CURRENT_TIMESTAMP
FROM RankedRegistry
WHERE PackageRank = 1
ON CONFLICT(PackageId) DO UPDATE SET
  DisplayPackageId = CASE
    WHEN RWModMods.DisplayPackageId = '' THEN excluded.DisplayPackageId
    ELSE RWModMods.DisplayPackageId
  END,
  ModName = CASE
    WHEN RWModMods.ModName = '' OR RWModMods.TrustLevel IN ('unknown', 'cloud_record', 'inferred') THEN excluded.ModName
    ELSE RWModMods.ModName
  END,
  Author = CASE
    WHEN RWModMods.Author = '' OR RWModMods.TrustLevel IN ('unknown', 'cloud_record', 'inferred') THEN excluded.Author
    ELSE RWModMods.Author
  END,
  SupportedGameVersionsJson = CASE
    WHEN RWModMods.SupportedGameVersionsJson IN ('', '[]') THEN excluded.SupportedGameVersionsJson
    ELSE RWModMods.SupportedGameVersionsJson
  END,
  LatestKnownVersion = CASE
    WHEN RWModMods.LatestKnownVersion IN ('', 'Unknown', 'unknown') THEN excluded.LatestKnownVersion
    ELSE RWModMods.LatestKnownVersion
  END,
  LastWorkshopUpdated = COALESCE(RWModMods.LastWorkshopUpdated, excluded.LastWorkshopUpdated),
  LastRegistryUpdated = CASE
    WHEN excluded.LastRegistryUpdated > COALESCE(RWModMods.LastRegistryUpdated, '') THEN excluded.LastRegistryUpdated
    ELSE RWModMods.LastRegistryUpdated
  END,
  LocalizationStatus = CASE
    WHEN RWModMods.LocalizationStatus IN ('', 'unknown', 'missing') THEN excluded.LocalizationStatus
    ELSE RWModMods.LocalizationStatus
  END,
  CompatibilityStatus = CASE
    WHEN RWModMods.CompatibilityStatus = '' THEN 'unknown'
    ELSE RWModMods.CompatibilityStatus
  END,
  PerformanceImpact = CASE
    WHEN RWModMods.PerformanceImpact = '' THEN 'unknown'
    ELSE RWModMods.PerformanceImpact
  END,
  TrustLevel = CASE
    WHEN RWModMods.TrustLevel IN ('', 'unknown', 'cloud_record', 'inferred') THEN excluded.TrustLevel
    ELSE RWModMods.TrustLevel
  END,
  Confidence = CASE
    WHEN RWModMods.Confidence IN ('', 'low', 'unknown')
      AND excluded.Confidence = 'medium'
      AND RWModMods.TrustLevel IN ('', 'unknown', 'cloud_record', 'inferred') THEN 'medium'
    WHEN RWModMods.Confidence = '' THEN excluded.Confidence
    ELSE RWModMods.Confidence
  END,
  UpdatedAt = CURRENT_TIMESTAMP;

WITH ActiveRegistry AS (
  SELECT
    RecordId,
    lower(trim(PackageId)) AS NormalizedPackageId,
    trim(Language) AS Language,
    trim(TranslationType) AS TranslationType,
    COALESCE(IsVerified, 0) AS IsVerified,
    trim(TargetModVersion) AS TargetModVersion,
    trim(UploaderID) AS UploaderID
  FROM TranslationRegistry
  WHERE COALESCE(IsDeleted, 0) <> 1
    AND PackageId IS NOT NULL
    AND trim(PackageId) <> ''
)
INSERT INTO RWModLocalizationStatus (
  LocalizationId,
  PackageId,
  Language,
  LocaleLabel,
  Status,
  SourceType,
  TrustLevel,
  Confidence,
  RegistryRecordId,
  TranslationType,
  IsVerified,
  CoveragePercent,
  TranslatedKeyCount,
  MissingKeyCount,
  OutdatedKeyCount,
  TargetModVersion,
  SourceUrl,
  ContributorName,
  Notes,
  IsVisible,
  CreatedAt,
  UpdatedAt
)
SELECT
  'registry:' || RecordId,
  NormalizedPackageId,
  Language,
  CASE
    WHEN Language = 'ChineseSimplified' THEN 'Simplified Chinese'
    WHEN Language = 'ChineseTraditional' THEN 'Traditional Chinese'
    WHEN Language = 'Japanese' THEN 'Japanese'
    WHEN Language = 'Korean' THEN 'Korean'
    WHEN Language = 'Russian' THEN 'Russian'
    WHEN Language = 'Ukrainian' THEN 'Ukrainian'
    WHEN Language = 'English' THEN 'English'
    ELSE Language
  END,
  'partial',
  'ai_translation_core',
  'cloud_record',
  CASE WHEN IsVerified = 1 THEN 'medium' ELSE 'low' END,
  RecordId,
  TranslationType,
  IsVerified,
  NULL,
  NULL,
  NULL,
  NULL,
  COALESCE(NULLIF(TargetModVersion, ''), 'Unknown'),
  '',
  CASE WHEN UploaderID = 'Anonymous' THEN '' ELSE UploaderID END,
  'Seeded from TranslationRegistry. Coverage requires review before promotion.',
  1,
  CURRENT_TIMESTAMP,
  CURRENT_TIMESTAMP
FROM ActiveRegistry
WHERE 1 = 1
ON CONFLICT(LocalizationId) DO UPDATE SET
  PackageId = excluded.PackageId,
  Language = excluded.Language,
  LocaleLabel = excluded.LocaleLabel,
  Status = CASE
    WHEN RWModLocalizationStatus.Status IN ('', 'unknown', 'partial')
      AND RWModLocalizationStatus.SourceType IN ('', 'unknown', 'ai_translation_core') THEN excluded.Status
    ELSE RWModLocalizationStatus.Status
  END,
  SourceType = 'ai_translation_core',
  TrustLevel = CASE
    WHEN RWModLocalizationStatus.TrustLevel IN ('', 'unknown', 'cloud_record') THEN excluded.TrustLevel
    ELSE RWModLocalizationStatus.TrustLevel
  END,
  Confidence = CASE
    WHEN RWModLocalizationStatus.Confidence IN ('', 'low', 'unknown')
      AND excluded.Confidence = 'medium' THEN 'medium'
    WHEN RWModLocalizationStatus.Confidence = '' THEN excluded.Confidence
    ELSE RWModLocalizationStatus.Confidence
  END,
  RegistryRecordId = excluded.RegistryRecordId,
  TranslationType = excluded.TranslationType,
  IsVerified = excluded.IsVerified,
  TargetModVersion = excluded.TargetModVersion,
  SourceUrl = '',
  ContributorName = CASE
    WHEN RWModLocalizationStatus.ContributorName = '' THEN excluded.ContributorName
    ELSE RWModLocalizationStatus.ContributorName
  END,
  Notes = CASE
    WHEN RWModLocalizationStatus.Notes = ''
      OR RWModLocalizationStatus.SourceType IN ('', 'unknown', 'ai_translation_core') THEN excluded.Notes
    ELSE RWModLocalizationStatus.Notes
  END,
  IsVisible = 1,
  UpdatedAt = CURRENT_TIMESTAMP;
