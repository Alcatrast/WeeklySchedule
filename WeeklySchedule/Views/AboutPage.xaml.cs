namespace WeeklySchedule.Views;

public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
        VersionLabel.Text = $"Версия {AppInfo.Current.VersionString}";
    }
}