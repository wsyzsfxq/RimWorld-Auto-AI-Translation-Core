-- Preview translation registry records that exceed the public history limits.
-- This does not modify data.
--
-- Limits:
-- - Official_Group: keep latest 3 per PackageId + Language
-- - Manual: keep latest 2 per PackageId + Language
-- - AI_Auto: keep latest 1 per PackageId + Language

WITH RankedRecords AS (
  SELECT
    RecordId,
    PackageId,
    Language,
    ModName,
    TranslationType,
    UploaderID,
    Author,
    LastUpdated,
    ROW_NUMBER() OVER (
      PARTITION BY PackageId, Language, TranslationType
      ORDER BY LastUpdated DESC, RecordId DESC
    ) AS KeepRank,
    CASE TranslationType
      WHEN 'Official_Group' THEN 3
      WHEN 'Manual' THEN 2
      WHEN 'AI_Auto' THEN 1
      ELSE 1
    END AS KeepLimit
  FROM TranslationRegistry
  WHERE IsDeleted IS NOT 1
)
SELECT *
FROM RankedRecords
WHERE KeepRank > KeepLimit
ORDER BY PackageId, Language, TranslationType, KeepRank;
