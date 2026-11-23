using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using AstroHybrid.App.Models;

namespace AstroHybrid.App.Services
{
    public class SettingsManager
    {
        // Process-wide: serializes every read-modify-write across all SettingsManager
        // instances in THIS process (App, MainWindowViewModel, SetupWizardViewModel,
        // LauncherService, UpdateService, InstallationManager each construct their own).
        // This is NOT a cross-process guarantee: a second launcher process or an older
        // build can still write the file concurrently - the atomic replace below only
        // narrows what a concurrent reader can observe, it cannot merge across processes.
        private static readonly object _fileLock = new object();

        private readonly string _settingsPath;
        private readonly JsonSerializerOptions _jsonOptions;
        private AppSettings? _cachedSettings;
        // JSON of the settings as this instance last loaded/saved them: the baseline that
        // lets Save tell "values this caller changed" apart from a stale cached copy.
        private string? _loadedBaselineJson;

        public SettingsManager(string rootDirectory)
        {
            _settingsPath = Path.Combine(rootDirectory, "Build", "astrohybrid-settings.json");
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
        }

        public AppSettings Load()
        {
            lock (_fileLock)
            {
                if (_cachedSettings != null)
                    return _cachedSettings;

                _cachedSettings = LoadFromDisk();
                _loadedBaselineJson = Serialize(_cachedSettings);
                return _cachedSettings;
            }
        }

        private AppSettings LoadFromDisk()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
                    // Older files only have the preset index ("rumble"); make the percent explicit.
                    settings.NormalizeRumble();
                    return settings;
                }
                catch
                {
                    return new AppSettings();
                }
            }

            return new AppSettings();
        }

        // Contract: pass the FULL settings object this instance loaded (Load()) with the
        // caller's changes applied. A partially populated object marks every untouched
        // property "unchanged" only against its own baseline; a caller that never loaded
        // gets the current disk state as baseline, so any property at its default in the
        // partial object overwrites the disk value - i.e. a partial Save resets fields the
        // caller never meant to touch. Coupled fields must be set together: changing only
        // the rumble index while the explicit percent came from another writer leaves the
        // pair mismatched, and the read-only computed EffectiveRumblePercent (JsonIgnore)
        // is not part of the merge, so an index-only change can be ignored at runtime.
        public void Save(AppSettings settings)
        {
            lock (_fileLock)
            {
                // Reload the current file inside the lock and merge per setting: only values
                // this caller changed relative to its own load baseline overwrite the file;
                // every other setting keeps the newest on-disk value. A stale cached
                // instance (every component constructs its own SettingsManager) can no
                // longer clobber another instance's saved changes with a whole-file write.
                var baseline = _loadedBaselineJson != null
                    ? JsonSerializer.Deserialize<AppSettings>(_loadedBaselineJson, _jsonOptions) ?? new AppSettings()
                    : LoadFromDisk();
                var current = LoadFromDisk();
                var merged = MergeChangedSettings(baseline, current, settings);
                merged.NormalizeRumble();

                WriteAtomically(Serialize(merged));

                _cachedSettings = merged;
                _loadedBaselineJson = Serialize(merged);
            }
        }

        // Property-level three-way merge: take the incoming value only where it differs
        // from the caller's baseline (the caller changed it); keep the current on-disk
        // value otherwise. Values are compared as JSON so nested objects compare by value.
        private static AppSettings MergeChangedSettings(AppSettings baseline, AppSettings current, AppSettings incoming)
        {
            foreach (var prop in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead || !prop.CanWrite) continue;
                var incomingJson = JsonSerializer.Serialize(prop.GetValue(incoming));
                var baselineJson = JsonSerializer.Serialize(prop.GetValue(baseline));
                if (!string.Equals(incomingJson, baselineJson, StringComparison.Ordinal))
                {
                    prop.SetValue(current, prop.GetValue(incoming));
                }
            }
            return current;
        }

        private string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, _jsonOptions);

        // Unique temp file in the same directory (same volume, so the final replace/move
        // is a rename). A crash between the temp write and the replace leaves the old
        // settings file untouched plus an orphaned temp file - never a half-written
        // settings file that Load would silently reset to defaults.
        private void WriteAtomically(string json)
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempFile = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempFile, json);
                if (File.Exists(_settingsPath))
                {
                    try
                    {
                        File.Replace(tempFile, _settingsPath, null);
                    }
                    catch (Exception)
                    {
                        // Destination locked or unsupported filesystem: fall back to a
                        // direct overwrite. This fallback is NOT atomic - it only matches
                        // the previous behavior for that contended case.
                        File.Copy(tempFile, _settingsPath, true);
                    }
                }
                else
                {
                    File.Move(tempFile, _settingsPath);
                }
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            }
        }

        public string GetSettingsPath() => _settingsPath;
    }
}
