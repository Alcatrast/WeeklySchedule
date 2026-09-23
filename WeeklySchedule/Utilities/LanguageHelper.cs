using System.Globalization;
using WeeklySchedule.Models;

namespace WeeklySchedule.Utilities;

public static class LanguageHelper
{
    public static CultureInfo GetSystemCulture()
    {
        try
        {
            var sys = CultureInfo.InstalledUICulture;
            if (IsSupported(sys)) return sys;
        }
        catch { }
        return new CultureInfo("en-US"); // Фоллбэк на английский
    }

    public static CultureInfo GetEffectiveCulture(AppLanguage language)
    {
        if (language == AppLanguage.System) return GetSystemCulture();

        return language switch
        {
            AppLanguage.Russian => new CultureInfo("ru-RU"),
            AppLanguage.English => new CultureInfo("en-US"),
            AppLanguage.Chinese => new CultureInfo("zh-CN"),
            AppLanguage.Korean => new CultureInfo("ko-KR"),
            _ => new CultureInfo("en-US")
        };
    }

    private static bool IsSupported(CultureInfo culture)
    {
        var name = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        return name == "ru" || name == "en" || name == "zh" || name == "ko";
    }
    public static string GetNeutralName(AppLanguage lang)
    {
        var neutral = GetSystemCulture().TwoLetterISOLanguageName;
        return lang switch
        {
            AppLanguage.System => neutral switch { "ru" => "Системный", "zh" => "系统", "ko" => "시스템", _ => "System" },
            AppLanguage.Russian => neutral switch { "ru" => "Русский", "zh" => "俄语", "ko" => "러시아어", _ => "Russian" },
            AppLanguage.English => neutral switch { "ru" => "Английский", "zh" => "英语", "ko" => "영어", _ => "English" },
            AppLanguage.Chinese => neutral switch { "ru" => "Китайский", "zh" => "中文", "ko" => "중국어", _ => "Chinese" },
            AppLanguage.Korean => neutral switch { "ru" => "Корейский", "zh" => "韩语", "ko" => "한국어", _ => "Korean" },
            _ => "Unknown"
        };
    }

    public static (string NativeName, string Flag) GetNativeDetails(AppLanguage lang)
    {
        if (lang == AppLanguage.System)
        {
            var sys = GetSystemCulture();
            return (GetNativeNameFromCulture(sys), GetFlagFromCulture(sys));
        }

        return lang switch
        {
            AppLanguage.Russian => ("Русский", "🇷🇺"),
            AppLanguage.English => ("English", "🇬🇧"),
            AppLanguage.Chinese => ("中文", "🇨🇳"),
            AppLanguage.Korean => ("한국어", "🇰🇷"),
            _ => ("English", "🇬🇧")
        };
    }

    private static string GetNativeNameFromCulture(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "ru" => "Русский",
        "zh" => "中文",
        "ko" => "한국어",
        _ => "English"
    };

    private static string GetFlagFromCulture(CultureInfo culture) => culture.TwoLetterISOLanguageName switch
    {
        "ru" => "🇷🇺",
        "zh" => "🇨🇳",
        "ko" => "🇰🇷",
        _ => "🇬🇧"
    };
    public static string GetDisplayText(AppLanguage lang)
    {
        var neutralName = GetNeutralName(lang);
        var (nativeName, flag) = GetNativeDetails(lang);

        if (neutralName.Equals(nativeName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{flag} {nativeName}";
        }

        return $"{neutralName} ({flag} {nativeName})";
    }
}