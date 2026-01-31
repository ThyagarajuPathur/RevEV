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

        // Request permissions on startup
        RequestAllBluetoothPermissions();
    }

    private async void RequestAllBluetoothPermissions()
    {
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S) // Android 12+
            {
                // Request Bluetooth permissions for Android 12+
                var btStatus = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
                if (btStatus != PermissionStatus.Granted)
                {
                    await Permissions.RequestAsync<Permissions.Bluetooth>();
                }

                // Also request nearby devices permission if needed
                var nearbyStatus = await Permissions.CheckStatusAsync<Permissions.Nearby>();
                if (nearbyStatus != PermissionStatus.Granted)
                {
                    await Permissions.RequestAsync<Permissions.Nearby>();
                }
            }
            else
            {
                // Android < 12 requires Location for Bluetooth scanning
                var locationStatus = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (locationStatus != PermissionStatus.Granted)
                {
                    await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Permission request error: {ex.Message}");
        }
    }

    protected override void OnResume()
    {
        base.OnResume();

        // Re-check Bluetooth state when app resumes (user might have enabled it)
        CheckBluetoothState();
    }

    private void CheckBluetoothState()
    {
        try
        {
            var adapter = Android.Bluetooth.BluetoothAdapter.DefaultAdapter;
            if (adapter != null && !adapter.IsEnabled)
            {
                // Bluetooth is disabled - we could prompt user to enable it
                System.Diagnostics.Debug.WriteLine("Bluetooth is disabled");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bluetooth check error: {ex.Message}");
        }
    }
}
