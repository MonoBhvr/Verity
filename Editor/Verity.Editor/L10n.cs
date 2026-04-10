using System.Globalization;
using System.Text.Json;

namespace Verity.Editor;

public static class L10n
{
    private static readonly StringComparer KeyComparer = StringComparer.Ordinal;
    private static readonly HashSet<string> MissingKeysLogged = new(KeyComparer);
    private static Dictionary<string, string> _strings = new(KeyComparer);
    private static IReadOnlyDictionary<string, string> _fallbackStrings = new Dictionary<string, string>(KeyComparer);

    public static string CurrentLanguage { get; private set; } = "ko";
    public static IReadOnlyList<string> AvailableLanguages { get; } = ["en", "ko"];

    public static void Initialize(string initialLanguage = "ko")
    {
        LoadLanguage(initialLanguage);
    }

    public static void LoadLanguage(string? langCode)
    {
        string normalized = NormalizeLanguageCode(langCode);
        var fallback = TryLoadLanguageDictionary("en") ?? new Dictionary<string, string>(KeyComparer);
        _fallbackStrings = fallback;

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

        _strings = merged;
        CurrentLanguage = normalized;
        MissingKeysLogged.Clear();
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

        return AvailableLanguages.Contains(normalized) ? normalized : "en";
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

    private static IEnumerable<string> EnumerateCandidatePaths(string langCode)
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
