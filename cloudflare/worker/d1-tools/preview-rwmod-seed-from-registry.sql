-- Preview the first RWMod catalog rows that can be seeded from TranslationRegistry.
-- Read-only: this file does not insert, update, delete, drop, or alter data.
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
SeedMods AS (
  SELECT
    NormalizedPackageId AS PackageId,
    COUNT(*) AS ActiveRegistryRecords,
    COUNT(DISTINCT Language) AS Languages,
    MAX(IsVerified) AS HasVerifiedRecord,
    MAX(CASE WHEN TargetModVersion LIKE '%1.5%' THEN 1 ELSE 0 END) AS HasGameVersion15,
    MAX(CASE WHEN TargetModVersion LIKE '%1.6%' THEN 1 ELSE 0 END) AS HasGameVersion16,
    MAX(COALESCE(LastUpdated, TranslationDate, '')) AS LastRegistryUpdated
  FROM ActiveRegistry
  GROUP BY NormalizedPackageId
)
SELECT 'active_registry_records' AS Metric, COUNT(*) AS Value
FROM ActiveRegistry
UNION ALL
SELECT 'distinct_seedable_packages' AS Metric, COUNT(*) AS Value
FROM SeedMods
UNION ALL
SELECT 'packages_with_verified_registry_record' AS Metric, COUNT(*) AS Value
FROM SeedMods
WHERE HasVerifiedRecord = 1
UNION ALL
SELECT 'packages_already_in_rwmod_catalog' AS Metric, COUNT(*) AS Value
FROM SeedMods sm
INNER JOIN RWModMods m ON m.PackageId = sm.PackageId
UNION ALL
SELECT 'registry_localization_rows_already_seeded' AS Metric, COUNT(*) AS Value
FROM ActiveRegistry ar
INNER JOIN RWModLocalizationStatus ls ON ls.LocalizationId = 'registry:' || ar.RecordId;

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
SELECT
  rr.NormalizedPackageId AS PackageId,
  rr.DisplayPackageId,
  COALESCE(NULLIF(rr.ModName, ''), rr.DisplayPackageId) AS ModName,
  COALESCE(NULLIF(rr.Author, ''), '') AS Author,
  CASE
    WHEN rr.HasGameVersion15 = 1 AND rr.HasGameVersion16 = 1 THEN '["1.5","1.6"]'
    WHEN rr.HasGameVersion16 = 1 THEN '["1.6"]'
    WHEN rr.HasGameVersion15 = 1 THEN '["1.5"]'
    ELSE '[]'
  END AS SupportedGameVersionsJson,
  COALESCE(NULLIF(rr.LatestVersion, ''), NULLIF(rr.TargetModVersion, ''), 'Unknown') AS LatestKnownVersion,
  rr.ModLastUpdated AS LastWorkshopUpdated,
  rr.LatestRegistryUpdate AS LastRegistryUpdated,
  'partial' AS LocalizationStatus,
  'unknown' AS CompatibilityStatus,
  'unknown' AS PerformanceImpact,
  'cloud_record' AS TrustLevel,
  CASE WHEN rr.HasVerifiedRecord = 1 THEN 'medium' ELSE 'low' END AS Confidence,
  CASE WHEN existing.PackageId IS NULL THEN 'insert' ELSE 'upsert_refresh' END AS SeedAction
FROM RankedRegistry rr
LEFT JOIN RWModMods existing ON existing.PackageId = rr.NormalizedPackageId
WHERE rr.PackageRank = 1
ORDER BY rr.LatestRegistryUpdate DESC, rr.NormalizedPackageId
LIMIT 100;

WITH ActiveRegistry AS (
  SELECT
    RecordId,
    lower(trim(PackageId)) AS NormalizedPackageId,
    trim(PackageId) AS DisplayPackageId,
    trim(ModName) AS ModName,
    trim(Language) AS Language,
    trim(TranslationType) AS TranslationType,
    COALESCE(IsVerified, 0) AS IsVerified,
    trim(TargetModVersion) AS TargetModVersion,
    NULLIF(trim(LastUpdated), '') AS LastUpdated,
    NULLIF(trim(TranslationDate), '') AS TranslationDate,
    trim(UploaderID) AS UploaderID
  FROM TranslationRegistry
  WHERE COALESCE(IsDeleted, 0) <> 1
    AND PackageId IS NOT NULL
    AND trim(PackageId) <> ''
)
SELECT
  'registry:' || ar.RecordId AS LocalizationId,
  ar.NormalizedPackageId AS PackageId,
  ar.Language,
  CASE
    WHEN ar.Language = 'ChineseSimplified' THEN 'Simplified Chinese'
    WHEN ar.Language = 'ChineseTraditional' THEN 'Traditional Chinese'
    WHEN ar.Language = 'Japanese' THEN 'Japanese'
    WHEN ar.Language = 'Korean' THEN 'Korean'
    WHEN ar.Language = 'Russian' THEN 'Russian'
    WHEN ar.Language = 'Ukrainian' THEN 'Ukrainian'
    WHEN ar.Language = 'English' THEN 'English'
    ELSE ar.Language
  END AS LocaleLabel,
  'partial' AS Status,
  'ai_translation_core' AS SourceType,
  'cloud_record' AS TrustLevel,
  CASE WHEN ar.IsVerified = 1 THEN 'medium' ELSE 'low' END AS Confidence,
  ar.RecordId AS RegistryRecordId,
  ar.TranslationType,
  ar.IsVerified,
  COALESCE(NULLIF(ar.TargetModVersion, ''), 'Unknown') AS TargetModVersion,
  CASE WHEN ar.UploaderID = 'Anonymous' THEN '' ELSE ar.UploaderID END AS ContributorName,
  CASE WHEN existing.LocalizationId IS NULL THEN 'insert' ELSE 'upsert_refresh' END AS SeedAction
FROM ActiveRegistry ar
LEFT JOIN RWModLocalizationStatus existing ON existing.LocalizationId = 'registry:' || ar.RecordId
ORDER BY COALESCE(ar.LastUpdated, ar.TranslationDate, '') DESC, ar.NormalizedPackageId, ar.Language
LIMIT 100;
