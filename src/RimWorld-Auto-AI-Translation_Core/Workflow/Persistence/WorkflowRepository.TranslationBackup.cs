using System;
using System.Collections.Generic;
using System.Data.Common;

namespace AutoTranslator_Core.Workflow.Persistence
{
    internal sealed partial class WorkflowRepository
    {
        internal List<TranslationBackupOrigin> GetTranslationBackupOrigins(ICollection<string> files)
        {
            var selected = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var result = new List<TranslationBackupOrigin>();
            using (DbConnection connection = _connections.OpenConnection())
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT t.* FROM TranslationResults t
                    JOIN Candidates c ON c.candidate_id=t.candidate_id
                    WHERE t.translation_state=2 AND t.translation_origin>0
                      AND t.source_text_hash_at_translation=c.source_text_hash
                      AND t.validation_status<>'Invalid'
                      AND t.last_sync_status NOT IN ('ReadError','SourceChanged','SourceMissing')
                      AND NOT EXISTS (SELECT 1 FROM PendingFileOperations p
                        WHERE p.candidate_id=t.candidate_id AND p.target_language=t.target_language AND p.state IN (0,2));";
                using (DbDataReader reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        string path = Convert.ToString(reader["managed_output_file"]).Replace('\\', '/');
                        if (!selected.Contains(path)) continue;
                        result.Add(new TranslationBackupOrigin
                        {
                            CandidateId = Convert.ToString(reader["candidate_id"]),
                            Language = Convert.ToString(reader["target_language"]),
                            Path = path, Key = Convert.ToString(reader["managed_output_key"]),
                            TextHash = Convert.ToString(reader["translation_hash"]),
                            SourceHash = Convert.ToString(reader["source_text_hash_at_translation"]),
                            Origin = (TranslationOrigin)Convert.ToInt32(reader["translation_origin"]),
                            SourcePackage = Convert.ToString(reader["source_package_id"]),
                            SourceFile = Convert.ToString(reader["source_file_relative_path"]),
                            SourceKey = Convert.ToString(reader["source_entry_key"]),
                            AiProvider = Convert.ToString(reader["ai_provider"])
                        });
                    }
            }
            return result;
        }

        internal List<string> GetTranslationBackupOutputPaths()
        {
            var paths = new List<string>();
            using (DbConnection connection = _connections.OpenConnection())
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT output_relative_path FROM Candidates WHERE output_relative_path<>''
                    UNION SELECT managed_output_file FROM TranslationResults WHERE managed_output_file<>'';";
                using (DbDataReader reader = command.ExecuteReader())
                    while (reader.Read()) paths.Add(Convert.ToString(reader[0]));
            }
            return paths;
        }

        internal List<string> GetTranslationBackupLanguages()
        {
            var languages = new List<string>();
            using (DbConnection connection = _connections.OpenConnection())
            using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT target_language FROM TranslationResults
                    UNION SELECT target_language FROM PendingFileOperations;";
                using (DbDataReader reader = command.ExecuteReader())
                    while (reader.Read())
                    {
                        string language = Convert.ToString(reader[0]);
                        if (!string.IsNullOrWhiteSpace(language)) languages.Add(language);
                    }
            }
            return languages;
        }
    }
}
