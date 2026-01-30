using Plugin.Maui.Audio;
using RevEV.Models;
using RevEV.Services.Interpolation;
using RevEV.Services.Settings;

namespace RevEV.Services.Audio;

public class AudioEngine : IAudioEngine, IDisposable
{
    private readonly IAudioManager _audioManager;
    private readonly IAppSettings _settings;
    private readonly LerpInterpolator _interpolator;
    private IAudioPlayer? _player;
    private bool _isInitialized;
    private float _targetRpm;
    private float _throttle = 0.5f;
    private System.Timers.Timer? _updateTimer;

    public bool IsPlaying => _player?.IsPlaying ?? false;
    public float CurrentPitch { get; private set; } = 1.0f;
    public float CurrentVolume { get; private set; } = 1.0f;
    public EngineProfile? CurrentProfile { get; private set; }

    public AudioEngine(IAudioManager audioManager, IAppSettings settings, LerpInterpolator interpolator)
    {
        _audioManager = audioManager;
        _settings = settings;
        _interpolator = interpolator;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        // Set up update timer for smooth audio updates (60Hz)
        _updateTimer = new System.Timers.Timer(16.67); // ~60 FPS
        _updateTimer.Elapsed += (s, e) => Update(0.01667f);
        _updateTimer.AutoReset = true;

        _isInitialized = true;
        await Task.CompletedTask;
    }

    public async Task LoadProfileAsync(EngineProfile profile)
    {
        try
        {
            // Stop current playback if any
            Stop();

            // Dispose previous player
            _player?.Dispose();
            _player = null;

            // Load new audio file
            var stream = await FileSystem.OpenAppPackageFileAsync(profile.AudioFileName);
            _player = _audioManager.CreatePlayer(stream);

            // Configure for looping
            _player.Loop = true;

            CurrentProfile = profile;

            // Reset pitch and volume
            CurrentPitch = 1.0f;
            CurrentVolume = profile.BaseVolume * _settings.MasterVolume;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load audio profile: {ex.Message}");
            throw;
        }
    }

    public void Play()
    {
        if (_player == null || CurrentProfile == null) return;

        _player.Volume = CurrentVolume;
        _player.Play();
        _updateTimer?.Start();
    }

    public void Stop()
    {
        _updateTimer?.Stop();
        _player?.Stop();
    }

    public void SetRpm(float rpm)
    {
        _targetRpm = rpm;
    }

    public void SetVolume(float volume)
    {
        CurrentVolume = Math.Clamp(volume * _settings.MasterVolume, 0f, 1f);
        if (_player != null)
        {
            _player.Volume = CurrentVolume;
        }
    }

    public void SetThrottle(float throttle)
    {
        _throttle = Math.Clamp(throttle, 0f, 1f);
    }

    public void Update(float deltaTime)
    {
        if (CurrentProfile == null || _player == null || !IsPlaying) return;

        // Interpolate RPM for smooth audio
        float smoothedRpm = _interpolator.Interpolate(_targetRpm);

        // Calculate pitch based on RPM
        CurrentPitch = CurrentProfile.GetPitchMultiplier(smoothedRpm);

        // Calculate volume based on throttle and RPM
        float targetVolume = CurrentProfile.GetVolume(_throttle, smoothedRpm) * _settings.MasterVolume;
        CurrentVolume = CurrentVolume + (targetVolume - CurrentVolume) * 0.1f; // Smooth volume changes

        // Apply to player
        ApplyPitchAndVolume();
    }

    private void ApplyPitchAndVolume()
    {
        if (_player == null) return;

        // Volume is supported directly
        _player.Volume = CurrentVolume;

        // Pitch shifting requires platform-specific implementation
        // The Plugin.Maui.Audio doesn't support pitch natively,
        // so we need to use platform-specific APIs

#if ANDROID
        ApplyPitchAndroid();
#elif IOS
        ApplyPitchIOS();
#endif
    }

#if ANDROID
    private void ApplyPitchAndroid()
    {
        // On Android, we need to use AudioTrack with PlaybackParams
        // Since Plugin.Maui.Audio uses MediaPlayer internally,
        // we need to access the native player

        try
        {
            // Access the internal player through reflection or native handler
            // Note: This is a simplified approach - in production, you'd want
            // a custom audio implementation using AudioTrack directly

            // For now, we'll rely on the fact that newer Android versions
            // support playback speed on MediaPlayer
            var playerType = _player?.GetType();
            var playerField = playerType?.GetField("_player",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (playerField?.GetValue(_player) is Android.Media.MediaPlayer mediaPlayer)
            {
                if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.M)
                {
                    var playbackParams = new Android.Media.PlaybackParams();
                    playbackParams.SetPitch(CurrentPitch);
                    playbackParams.SetSpeed(CurrentPitch); // Keep speed in sync
                    mediaPlayer.PlaybackParams = playbackParams;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set pitch on Android: {ex.Message}");
        }
    }
#endif

#if IOS
    private void ApplyPitchIOS()
    {
        // On iOS, we would use AVAudioEngine with AVAudioUnitTimePitch
        // The Plugin.Maui.Audio uses AVAudioPlayer which doesn't support pitch
        // For full pitch support, a custom implementation using AVAudioEngine is needed

        try
        {
            // Note: This is a placeholder - in production, you'd implement
            // a custom audio engine using AVAudioEngine + AVAudioUnitTimePitch

            // AVAudioEngine approach:
            // 1. Create AVAudioEngine
            // 2. Create AVAudioPlayerNode
            // 3. Create AVAudioUnitTimePitch
            // 4. Connect: Player -> TimePitch -> MainMixer -> Output
            // 5. Set timePitch.pitch = (CurrentPitch - 1.0) * 1200 (in cents)

            System.Diagnostics.Debug.WriteLine($"iOS pitch shifting requires custom AVAudioEngine implementation. Target pitch: {CurrentPitch}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set pitch on iOS: {ex.Message}");
        }
    }
#endif

    public void Dispose()
    {
        Stop();
        _updateTimer?.Dispose();
        _player?.Dispose();
    }
}
