using WeeklySchedule.Resources.Strings;

namespace WeeklySchedule.Views;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = string.Format(AppResources.VersionFormat, AppInfo.Current.VersionString);
    }
}