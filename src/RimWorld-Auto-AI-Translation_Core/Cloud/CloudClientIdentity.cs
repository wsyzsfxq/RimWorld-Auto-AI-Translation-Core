using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslator_Core
{
    // Local associations only. They do not transfer ownership of server records.
    internal static class CloudClientIdentity
    {
        private static readonly object Gate = new object();
        private static string configDirectory;
        private static string steamId = string.Empty;
        private static string unityId = string.Empty;
        private static long nextSnapshotTicks;
        private static IdentityProfile activeProfile;
        private static bool failureReported;

        internal static void InitializeOnMainThread()
        {
            if (!UnityData.IsInMainThread) return;
            long now = DateTime.UtcNow.Ticks;
            if (now < nextSnapshotTicks) return;
            nextSnapshotTicks = now + TimeSpan.TicksPerSecond;
            try
            {
                string directory = GenFilePaths.ConfigFolderPath;
                string currentSteamId = ReadSteamId();
                string currentUnityId = ReadUnityId();
                lock (Gate)
                {
                    if (configDirectory != directory || steamId != currentSteamId || unityId != currentUnityId)
                    {
                        configDirectory = directory;
                        steamId = currentSteamId;
                        unityId = currentUnityId;
                        activeProfile = null;
                        AutoTranslatorMod.InvalidateCloudIdentityCache();
                    }
                }
            }
            catch
            {
                // A later main-thread call retries; never query Unity/Steam from upload workers.
                ReportFailure();
            }
        }

        internal static bool TryGetValue(out string value)
        {
            value = string.Empty;
            if (!UnityData.IsInMainThread) return false;
            InitializeOnMainThread();
            lock (Gate)
            {
                try
                {
                    if (string.IsNullOrEmpty(configDirectory)) return false;
                    if (activeProfile == null) activeProfile = ResolveAndSave();
                    value = activeProfile.CloudId;
                    failureReported = false;
                    return !string.IsNullOrWhiteSpace(value);
                }
                catch
                {
                    ReportFailure();
                    return false;
                }
            }
        }

        internal static Task<string> GetValueAsync()
        {
            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            ATC_Dispatcher.RunOnMainThread(() =>
            {
                string value;
                if (TryGetValue(out value)) completion.TrySetResult(value);
                else completion.TrySetException(new InvalidOperationException(
                    "Cloud identity could not be loaded or saved; existing identity files were preserved."));
            });
            return completion.Task;
        }

        internal static bool IsCurrentUserId(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            string value;
            if (!TryGetValue(out value)) return false;
            lock (Gate)
            {
                return Same(candidate, value) || Same(candidate, activeProfile.SteamId) ||
                       Same(candidate, activeProfile.LegacyUnityId) || Same(candidate, activeProfile.LegacyRandomId);
            }
        }

        private static IdentityProfile ResolveAndSave()
        {
            Directory.CreateDirectory(configDirectory);
            string path = Path.Combine(configDirectory, "ATC_CloudIdentities.json");
            // Separate game processes must not silently replace each other's first association.
            using (new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                IdentityDocument document = File.Exists(path)
                    ? JsonConvert.DeserializeObject<IdentityDocument>(File.ReadAllText(path))
                    : new IdentityDocument();
                if (document == null || document.Version != 1 || document.Profiles == null ||
                    document.Profiles.Any(p => p == null || string.IsNullOrWhiteSpace(p.CloudId)))
                    throw new InvalidDataException("Unsupported or incomplete cloud identity document.");

                IdentityProfile profile;
                if (string.IsNullOrEmpty(steamId))
                {
                    // Missing Steam information does not mean a different person started playing.
                    profile = document.Profiles.FirstOrDefault(p => Same(p.CloudId, document.LastSelectedCloudId)) ??
                              document.Profiles.FirstOrDefault(p => string.IsNullOrEmpty(p.SteamId));
                    if (profile == null && document.Profiles.Count == 1) profile = document.Profiles[0];
                    if (profile == null && document.Profiles.Count > 0)
                        throw new InvalidDataException("The last cloud identity is unavailable; existing profiles were preserved.");
                }
                else
                {
                    // A positively identified Steam account selects its own association.
                    profile = document.Profiles.FirstOrDefault(p => SameScope(p.SteamId, steamId));
                }
                bool changed = false;
                if (profile == null && !string.IsNullOrEmpty(steamId) &&
                    !document.Profiles.Any(p => !string.IsNullOrEmpty(p.SteamId)))
                {
                    // Steam may become ready after a first offline use. Keep that existing cloud ID.
                    profile = document.Profiles.FirstOrDefault(p => string.IsNullOrEmpty(p.SteamId));
                    if (profile != null) { profile.SteamId = steamId; changed = true; }
                }

                string oldRandomPath = Path.Combine(configDirectory, "ATC_CloudClientId.txt");
                string oldRandomId = File.Exists(oldRandomPath) ? File.ReadAllText(oldRandomPath).Trim() : string.Empty;
                if (profile == null)
                {
                    profile = new IdentityProfile { SteamId = steamId };
                    if (CanAssociate(document, profile, oldRandomId))
                    {
                        profile.CloudId = oldRandomId;
                        profile.Source = "legacy-random";
                    }
                    else if (CanAssociate(document, profile, unityId) && HasExistingUpload(unityId))
                    {
                        profile.CloudId = unityId;
                        profile.Source = "legacy-unity";
                    }
                    else if (!string.IsNullOrEmpty(steamId))
                    {
                        profile.CloudId = steamId;
                        profile.Source = "steam";
                    }
                    else
                    {
                        profile.CloudId = Guid.NewGuid().ToString("N");
                        profile.Source = "random";
                    }
                    document.Profiles.Add(profile);
                    changed = true;
                }

                if (string.IsNullOrEmpty(profile.LegacyUnityId) && CanAssociate(document, profile, unityId))
                {
                    profile.LegacyUnityId = unityId;
                    changed = true;
                }
                if (string.IsNullOrEmpty(profile.LegacyRandomId) && CanAssociate(document, profile, oldRandomId))
                {
                    profile.LegacyRandomId = oldRandomId;
                    changed = true;
                }
                if (!Same(document.LastSelectedCloudId, profile.CloudId))
                {
                    document.LastSelectedCloudId = profile.CloudId;
                    changed = true;
                }
                if (changed) SaveDocument(path, document);
                return profile;
            }
        }

        private static bool CanAssociate(IdentityDocument document, IdentityProfile profile, string value)
        {
            return !string.IsNullOrWhiteSpace(value) && !document.Profiles.Any(other =>
                !ReferenceEquals(other, profile) && (Same(value, other.CloudId) ||
                    Same(value, other.SteamId) || Same(value, other.LegacyUnityId) || Same(value, other.LegacyRandomId)));
        }

        private static bool HasExistingUpload(string value)
        {
            // Only use an already available registry. Identity selection never starts a network request.
            return AutoTranslatorSettings.CloudRegistry != null &&
                   AutoTranslatorSettings.CloudRegistry.Any(record => record != null && Same(record.UploaderID, value));
        }

        private static void SaveDocument(string path, IdentityDocument document)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(document, Formatting.Indented));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static string ReadSteamId()
        {
            try { return ReadSteamIdCore(); }
            catch { return string.Empty; }
        }

        private static string ReadSteamIdCore()
        {
            // Both game versions provide these interfaces. Avoid adding a second Steamworks assembly.
            Type manager = typeof(Mod).Assembly.GetType("Verse.Steam.SteamManager", false);
            PropertyInfo initialized = manager?.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Static);
            if (initialized == null || !Equals(initialized.GetValue(null, null), true)) return string.Empty;
            Type steamUser = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Steamworks.SteamUser", false)).FirstOrDefault(type => type != null);
            object id = steamUser?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
            if (id == null) return string.Empty;
            MethodInfo valid = id.GetType().GetMethod("IsValid", Type.EmptyTypes);
            if (valid == null || !Equals(valid.Invoke(id, null), true)) return string.Empty;
            FieldInfo numeric = id.GetType().GetField("m_SteamID", BindingFlags.Public | BindingFlags.Instance);
            object raw = numeric?.GetValue(id);
            return raw is ulong && (ulong)raw != 0
                ? ((ulong)raw).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string ReadUnityId()
        {
            try
            {
                Type systemInfo = typeof(UnityEngine.SystemInfo);
                PropertyInfo property = systemInfo.GetProperty(
                    "deviceUniqueIdentifier", BindingFlags.Public | BindingFlags.Static);
                string value = property?.GetValue(null, null) as string;
                string unsupported = systemInfo.GetField("unsupportedIdentifier", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) as string;
                if (string.IsNullOrWhiteSpace(value) || Same(value, unsupported) || Same(value, "unsupported"))
                    return string.Empty;
                return value.Trim();
            }
            catch { return string.Empty; }
        }

        private static bool Same(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameScope(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
        }

        private static void ReportFailure()
        {
            if (failureReported) return;
            failureReported = true;
            Log.Warning("[ATC Cloud] Cloud identity is temporarily unavailable. Existing identity files were preserved; the next cloud operation will retry.");
        }

        private sealed class IdentityDocument
        {
            public int Version = 1;
            public string LastSelectedCloudId = string.Empty;
            public List<IdentityProfile> Profiles = new List<IdentityProfile>();
        }

        private sealed class IdentityProfile
        {
            public string SteamId = string.Empty;
            public string CloudId = string.Empty;
            public string Source = string.Empty;
            public string LegacyUnityId = string.Empty;
            public string LegacyRandomId = string.Empty;
        }
    }
}
