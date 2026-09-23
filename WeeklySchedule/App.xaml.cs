using System.Globalization;
using WeeklySchedule.Services;
using WeeklySchedule.Utilities;
using WeeklySchedule.Resources.Strings;

namespace WeeklySchedule;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly ISettingsService _settings;

    public App(IServiceProvider services, ISettingsService settings)
    {
        var culture = LanguageHelper.GetEffectiveCulture(settings.SelectedLanguage);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        AppResources.Culture = culture;

        InitializeComponent();
        _services = services;
        _settings = settings;

        UserAppTheme = settings.Theme;
    }

    public static void RestartApp()
    {
#if ANDROID
        try
        {
            var context = Android.App.Application.Context;
            if (context != null)
            {
#pragma warning disable CA1422 //bad idea but...
                var sharedPreferences = Android.Preferences.PreferenceManager.GetDefaultSharedPreferences(context);
#pragma warning restore CA1422

                if (sharedPreferences != null)
                {
                    using var editor = sharedPreferences.Edit();
                    editor?.Commit();
                }

                var packageName = context.PackageName;
                if (!string.IsNullOrEmpty(packageName))
                {
                    var intent = context.PackageManager?.GetLaunchIntentForPackage(packageName);
                    if (intent != null)
                    {
                        intent.AddFlags(Android.Content.ActivityFlags.ClearTop |
                                        Android.Content.ActivityFlags.ClearTask |
                                        Android.Content.ActivityFlags.NewTask);
                        context.StartActivity(intent);
                    }
                }
            }
        }
        catch {        }
        Java.Lang.JavaSystem.Exit(0);
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = _services.GetRequiredService<AppShell>();
        return new Window(shell);
    }
}