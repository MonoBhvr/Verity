using System.Text.Json;

namespace Verity.Editor;

public static class L10n
{
    private static Dictionary<string, string> _strings = new();
    public static string CurrentLanguage { get; private set; } = "ko";

    public static void Initialize(string initialLanguage = "ko")
    {
        LoadLanguage(initialLanguage); 
    }

    public static void LoadLanguage(string langCode)
    {
        try
        {
            string baseDir = AppContext.BaseDirectory;
            
            // Priority 1: AppDir/Locales (Production)
            string path = Path.Combine(baseDir, "Locales", $"{langCode}.json");
            
            if (!File.Exists(path))
            {
                // Priority 2: AppDir/Editor/Verity.Editor/Locales (Some Dev structures)
                path = Path.Combine(baseDir, "Editor", "Verity.Editor", "Locales", $"{langCode}.json");
            }

            if (!File.Exists(path))
            {
                // Priority 3: CurrentDir/Editor/Verity.Editor/Locales (Root Dev)
                path = Path.Combine(Directory.GetCurrentDirectory(), "Editor", "Verity.Editor", "Locales", $"{langCode}.json");
            }

            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded != null)
                {
                    _strings = loaded;
                    CurrentLanguage = langCode;
                    System.Diagnostics.Debug.WriteLine($"[L10n] Successfully loaded: {langCode} from {path}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[L10n] Failed to find language file for {langCode}");
            }
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[L10n] Error loading {langCode}: {e.Message}");
        }
    }

    public static string Tr(string key)
    {
        if (_strings.TryGetValue(key, out string? value))
            return value;
        return key; // Return the key itself if not found
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