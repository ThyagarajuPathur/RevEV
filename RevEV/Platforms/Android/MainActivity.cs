using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace RevEV;

[Activity(Theme = "@style/Maui.SplashTheme",
          MainLauncher = true,
          LaunchMode = LaunchMode.SingleTop,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                                 ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Keep screen on while app is active
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        // Set status bar color to match theme
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
        {
            Window?.SetStatusBarColor(Android.Graphics.Color.Black);
        }

        // Request permissions
        RequestPermissionsAsync();
    }

    private async void RequestPermissionsAsync()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.S) // Android 12+
        {
            await Permissions.RequestAsync<Permissions.Bluetooth>();
        }
        else
        {
            await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }
    }
}
