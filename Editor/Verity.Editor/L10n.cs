using System.Globalization;
using System.Text.Json;

namespace Verity.Editor;

public static class L10n
{
    private static readonly StringComparer KeyComparer = StringComparer.Ordinal;
    private static readonly HashSet<string> MissingKeysLogged = new(KeyComparer);
    private static Dictionary<string, string> _strings = new(KeyComparer);
    private static IReadOnlyDictionary<string, string> _fallbackStrings = new Dictionary<string, string>(KeyComparer);

    private static readonly List<string> _availableLanguages = ["en", "ko"];
    public static IReadOnlyList<string> AvailableLanguages => _availableLanguages;

    public static string CurrentLanguage { get; private set; } = "ko";

    public static void Initialize(string initialLanguage = "ko")
    {
        DiscoverAvailableLanguages();
        LoadLanguage(initialLanguage);
    }

    public static void DiscoverAvailableLanguages()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en" };

        foreach (string searchDir in GetLocalesSearchDirectories())
        {
            try
            {
                if (!Directory.Exists(searchDir))
                    continue;

                foreach (string filePath in Directory.GetFiles(searchDir, "*.json"))
                {
                    string code = Path.GetFileNameWithoutExtension(filePath);
                    if (!string.IsNullOrEmpty(code) && code.Length is >= 2 and <= 8 && code.All(char.IsLetter))
                        found.Add(code.ToLowerInvariant());
                }
            }
            catch { }
        }

        _availableLanguages.Clear();
        _availableLanguages.AddRange(found.OrderBy(c => c, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> GetLocalesSearchDirectories()
    {
        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Locales");
        yield return Path.Combine(baseDir, "Editor", "Verity.Editor", "Locales");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Verity.Editor", "Locales");
    }

    public static string GetWritableLocalesDirectory()
    {
        string[] candidates =
        [
            Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Verity.Editor", "Locales"),
            Path.Combine(AppContext.BaseDirectory, "Locales"),
        ];

        foreach (string dir in candidates)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string testFile = Path.Combine(dir, ".write_test");
                File.WriteAllText(testFile, "");
                File.Delete(testFile);
                return dir;
            }
            catch
            {
                continue;
            }
        }

        string fallback = Path.Combine(AppContext.BaseDirectory, "Locales");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public static void LoadLanguage(string? langCode)
    {
        string normalized = NormalizeLanguageCode(langCode);
        var fallback = BuildFallbackDictionary(normalized);
        Dictionary<string, string> merged = new(fallback, KeyComparer);

        if (!string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase))
        {
            var localized = TryLoadLanguageDictionary(normalized);
            if (localized != null)
            {
                foreach (var pair in localized)
                    merged[pair.Key] = pair.Value;
            }
            else
            {
                normalized = "en";
            }
        }

        _fallbackStrings = fallback;

        _strings = merged;
        CurrentLanguage = normalized;
        MissingKeysLogged.Clear();
    }

    private static Dictionary<string, string> BuildFallbackDictionary(string selectedLanguage)
    {
        Dictionary<string, string> merged = new(KeyComparer);

        foreach (string fallbackLanguage in EnumerateFallbackLanguages(selectedLanguage))
        {
            var dict = TryLoadLanguageDictionary(fallbackLanguage);
            if (dict == null)
                continue;

            foreach (var pair in dict)
            {
                if (!merged.ContainsKey(pair.Key))
                    merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private static IEnumerable<string> EnumerateFallbackLanguages(string selectedLanguage)
    {
        yield return "en";

        if (!string.Equals(selectedLanguage, "ko", StringComparison.OrdinalIgnoreCase))
            yield return "ko";

        foreach (string lang in _availableLanguages)
        {
            if (string.Equals(lang, selectedLanguage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lang, "ko", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return lang;
        }
    }

    public static string NormalizeLanguageCode(string? langCode)
    {
        if (string.IsNullOrWhiteSpace(langCode))
        {
            try
            {
                langCode = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            }
            catch
            {
                langCode = "en";
            }
        }

        string normalized = langCode.Trim().ToLowerInvariant();
        int separator = normalized.IndexOfAny(['-', '_']);
        if (separator >= 0)
            normalized = normalized[..separator];

        return _availableLanguages.Contains(normalized) ? normalized : "en";
    }

    public static bool AddLanguage(string langCode, string displayName, string baseLangCode = "en")
    {
        if (string.IsNullOrWhiteSpace(langCode))
            return false;

        string normalizedCode = langCode.Trim().ToLowerInvariant();
        if (normalizedCode.Length is < 2 or > 8)
            return false;
        if (!normalizedCode.All(char.IsLetter))
            return false;
        if (_availableLanguages.Contains(normalizedCode))
            return false;

        var baseDict = TryLoadLanguageDictionary(baseLangCode) ?? TryLoadLanguageDictionary("en");
        if (baseDict == null)
            return false;

        var newDict = new Dictionary<string, string>(baseDict, KeyComparer);
        string langKey = $"lang_{normalizedCode}";
        newDict[langKey] = displayName;

        try
        {
            string localesDir = GetWritableLocalesDirectory();
            string filePath = Path.Combine(localesDir, $"{normalizedCode}.json");

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(newDict, options);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            return false;
        }

        _availableLanguages.Add(normalizedCode);
        _availableLanguages.Sort(StringComparer.OrdinalIgnoreCase);

        return true;
    }

    public static string GetLanguageDisplayName(string langCode)
    {
        string key = $"lang_{langCode}";
        if (_strings.TryGetValue(key, out string? value))
            return value;

        if (_fallbackStrings.TryGetValue(key, out value))
            return value;

        var dict = TryLoadLanguageDictionary(langCode);
        if (dict != null && dict.TryGetValue(key, out value))
            return value;

        return langCode.ToUpperInvariant();
    }

    private static Dictionary<string, string>? TryLoadLanguageDictionary(string langCode)
    {
        string normalized = NormalizeLanguageCode(langCode);
        foreach (string path in EnumerateCandidatePaths(normalized))
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded == null)
                    continue;

                System.Diagnostics.Debug.WriteLine($"[L10n] Successfully loaded: {normalized} from {path}");
                return new Dictionary<string, string>(loaded, KeyComparer);
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine($"[L10n] Error loading {normalized} from {path}: {e.Message}");
            }
        }

        System.Diagnostics.Debug.WriteLine($"[L10n] Failed to find language file for {normalized}");
        return null;
    }

    public static IEnumerable<string> EnumerateCandidatePaths(string langCode)
    {
        string baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "Locales", $"{langCode}.json");
        yield return Path.Combine(baseDir, "Editor", "Verity.Editor", "Locales", $"{langCode}.json");
        yield return Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Verity.Editor", "Locales", $"{langCode}.json");
    }

    public static string Tr(string key)
    {
        if (_strings.TryGetValue(key, out string? value))
            return value;

        if (!MissingKeysLogged.Contains(key))
        {
            MissingKeysLogged.Add(key);
            System.Diagnostics.Debug.WriteLine($"[L10n] Missing key: {key} (lang={CurrentLanguage})");
        }

        if (_fallbackStrings.TryGetValue(key, out value))
            return value;

        return key;
    }

    public static string Tr(string key, params object[] args)
    {
        string raw = Tr(key);
        try
        {
            return string.Format(raw, args);
        }
        catch
        {
            return raw;
        }
    }
}
