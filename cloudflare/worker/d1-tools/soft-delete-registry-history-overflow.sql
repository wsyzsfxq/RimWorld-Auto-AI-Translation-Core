-- Soft-delete translation registry records that exceed the public history limits.
-- Existing rows are not hard-deleted. R2 objects are not deleted.
--
-- Recommended flow:
-- 1. Run preview-registry-history-overflow.sql first.
-- 2. If the preview looks right, run this file.
-- 3. Deploy worker-v2.js with ENABLE_UPLOAD_HISTORY_CLEANUP = true so future
--    uploads keep the same limits automatically.
--
-- Limits:
-- - Official_Group: keep latest 3 per PackageId + Language
-- - Manual: keep latest 2 per PackageId + Language
-- - AI_Auto: keep latest 1 per PackageId + Language

UPDATE TranslationRegistry
SET IsDeleted = 1
WHERE RecordId IN (
  WITH RankedRecords AS (
    SELECT
      RecordId,
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
  SELECT RecordId
  FROM RankedRecords
  WHERE KeepRank > KeepLimit
);
