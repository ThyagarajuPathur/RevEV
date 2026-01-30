using Foundation;
using UIKit;

namespace RevEV;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // Configure audio session for playback
        ConfigureAudioSession();

        return base.FinishedLaunching(application, launchOptions);
    }

    private void ConfigureAudioSession()
    {
        try
        {
            var audioSession = AVFoundation.AVAudioSession.SharedInstance();
            audioSession.SetCategory(AVFoundation.AVAudioSessionCategory.Playback,
                                     AVFoundation.AVAudioSessionCategoryOptions.MixWithOthers);
            audioSession.SetActive(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to configure audio session: {ex.Message}");
        }
    }
}
