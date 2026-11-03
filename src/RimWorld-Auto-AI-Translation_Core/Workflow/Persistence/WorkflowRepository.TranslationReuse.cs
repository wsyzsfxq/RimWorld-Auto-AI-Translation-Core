using System;
using System.Data.Common;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace AutoTranslator_Core.Workflow.Persistence
{
    internal sealed class ReusableTranslation
    {
        public string Text { get; set; }
        public TranslationOrigin Origin { get; set; }
        public string SourcePackage { get; set; }
        public string SourceFile { get; set; }
        public string SourceKey { get; set; }
        public string Provider { get; set; }
        public string Model { get; set; }
        public string PromptVersion { get; set; }
        public string RunId { get; set; }
        public int BatchIndex { get; set; }
    }

    internal sealed partial class WorkflowRepository
    {
        public Dictionary<string, ReusableTranslation> FindReusableTranslations(
            IList<CandidateRecord> candidates, string targetLanguage)
        {
            var results = new Dictionary<string, ReusableTranslation>(StringComparer.Ordinal);
            if (candidates.Count == 0) return results;
            using (DbConnection connection = _connections.OpenConnection())
            {
                // Bound SQL parameters without limiting the number of candidates processed.
                for (int offset = 0; offset < candidates.Count; offset += 100)
                using (DbCommand command = connection.CreateCommand())
                {
                    var batch = candidates.Skip(offset).Take(100).ToList();
                    var values = new List<string>();
                    for (int i = 0; i < batch.Count; i++)
                    {
                        values.Add("(@id" + i + ",@hash" + i + ",@text" + i + ")");
                        Add(command, "@id" + i, batch[i].CandidateId);
                        Add(command, "@hash" + i, batch[i].SourceTextHash);
                        Add(command, "@text" + i, batch[i].SourceText);
                    }
                    Add(command, "@target", targetLanguage);
                    command.CommandText = "WITH requested(id,hash,text) AS (VALUES " +
                        string.Join(",", values) + @")
                        SELECT requested.id AS recipient_id,r.* FROM requested
                        JOIN TranslationResults r ON r.target_language=@target
                        AND r.candidate_id=(SELECT c.candidate_id FROM Candidates c
                            JOIN TranslationResults donor ON donor.candidate_id=c.candidate_id
                            WHERE c.source_text_hash=requested.hash AND c.source_text=requested.text COLLATE BINARY
                              AND c.candidate_id<>requested.id AND donor.target_language=@target
                              AND donor.source_text_hash_at_translation=c.source_text_hash
                              AND donor.translation_origin>0 AND donor.translation_text<>''
                              AND donor.validation_status<>'Invalid'
                              AND (donor.translation_state=2 OR donor.translation_origin>1)
                              AND donor.last_sync_status NOT IN ('ReadError','SourceChanged','SourceMissing')
                              AND NOT EXISTS (SELECT 1 FROM PendingFileOperations p
                                  WHERE p.candidate_id=c.candidate_id AND p.target_language=@target AND p.state IN (0,2))
                            ORDER BY donor.translation_origin DESC,donor.updated_utc DESC,c.candidate_id LIMIT 1);";
                    using (DbDataReader reader = command.ExecuteReader())
                    while (reader.Read())
                        results[Convert.ToString(reader["recipient_id"])] = new ReusableTranslation
                        {
                            Text = Convert.ToString(reader["translation_text"]),
                            Origin = (TranslationOrigin)Convert.ToInt32(reader["translation_origin"]),
                            SourcePackage = Convert.ToString(reader["source_package_id"]),
                            SourceFile = Convert.ToString(reader["source_file_relative_path"]),
                            SourceKey = Convert.ToString(reader["source_entry_key"]),
                            Provider = Convert.ToString(reader["ai_provider"]),
                            Model = Convert.ToString(reader["ai_model"]),
                            PromptVersion = Convert.ToString(reader["ai_prompt_version"]),
                            RunId = Convert.ToString(reader["ai_run_id"]),
                            BatchIndex = Convert.ToInt32(reader["ai_batch_index"])
                        };
                }
            }
            return results;
        }

        private static void RestoreReuseProvenance(DbConnection connection, DbTransaction transaction,
            PendingTranslationCompletion completion)
        {
            string json;
            using (DbCommand read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = "SELECT reuse_provenance_json FROM PendingFileOperations WHERE operation_id=@operation;";
                Add(read, "@operation", completion.OperationId.ToString("N"));
                json = Convert.ToString(read.ExecuteScalar());
            }
            if (string.IsNullOrEmpty(json)) return;
            ReusableTranslation source = JsonConvert.DeserializeObject<ReusableTranslation>(json);
            if (source == null) throw new InvalidOperationException("Missing reused translation provenance.");
            using (DbCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"UPDATE TranslationResults SET translation_origin=@origin,
                    source_package_id=@package,source_file_relative_path=@file,source_entry_key=@key,
                    ai_provider=@provider,ai_model=@model,ai_prompt_version=@prompt,ai_run_id=@run,ai_batch_index=@batch
                    WHERE candidate_id=@id AND target_language=@target;";
                Add(update, "@origin", (int)source.Origin);
                Add(update, "@package", source.SourcePackage ?? "");
                Add(update, "@file", source.SourceFile ?? "");
                Add(update, "@key", source.SourceKey ?? "");
                Add(update, "@provider", source.Provider ?? "");
                Add(update, "@model", source.Model ?? "");
                Add(update, "@prompt", source.PromptVersion ?? "");
                Add(update, "@run", source.RunId ?? "");
                Add(update, "@batch", source.BatchIndex);
                Add(update, "@id", completion.CandidateId);
                Add(update, "@target", completion.TargetLanguage);
                update.ExecuteNonQuery();
            }
        }
    }
}
