-- Run this against the existing production D1 database before any upgrade.
-- It only reads metadata and sample counts; it does not change data.

PRAGMA table_info(TranslationRegistry);

SELECT
  COUNT(*) AS TotalRecords,
  SUM(CASE WHEN IsDeleted IS 1 THEN 1 ELSE 0 END) AS SoftDeletedRecords,
  SUM(CASE WHEN Language = 'ChineseSimplified' AND IsDeleted IS NOT 1 THEN 1 ELSE 0 END) AS ActiveSimplifiedChineseRecords
FROM TranslationRegistry;

SELECT Language, TranslationType, COUNT(*) AS Records
FROM TranslationRegistry
GROUP BY Language, TranslationType
ORDER BY Records DESC;
