using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using RevEV.Services.Audio;
using RevEV.Services.Bluetooth;
using RevEV.Services.Interpolation;
using RevEV.Services.Settings;
using RevEV.ViewModels;
using RevEV.Views;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace RevEV;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("Orbitron-Regular.ttf", "Orbitron");
                fonts.AddFont("Orbitron-Bold.ttf", "OrbitronBold");
                fonts.AddFont("Orbitron-Black.ttf", "OrbitronBlack");
            });

        // Register services
        builder.Services.AddSingleton<IAppSettings, AppSettings>();
        builder.Services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // Audio services
        builder.Services.AddSingleton(AudioManager.Current);
        builder.Services.AddSingleton<IAudioEngine, AudioEngine>();
        builder.Services.AddSingleton<IEngineProfileManager, EngineProfileManager>();

        // Bluetooth services
        builder.Services.AddSingleton<IBluetoothService, BleService>();
        builder.Services.AddSingleton<BluetoothManager>();
        builder.Services.AddSingleton<OBDProtocolHandler>();

        // Interpolation
        builder.Services.AddSingleton<LerpInterpolator>();

        // ViewModels
        builder.Services.AddSingleton<DriveViewModel>();
        builder.Services.AddSingleton<EngineBayViewModel>();
        builder.Services.AddSingleton<TerminalViewModel>();

        // Views
        builder.Services.AddSingleton<DrivePage>();
        builder.Services.AddSingleton<EngineBayPage>();
        builder.Services.AddSingleton<TerminalPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
