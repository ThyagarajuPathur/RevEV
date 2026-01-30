using RevEV.Models;

namespace RevEV.Services.Audio;

public interface IAudioEngine
{
    bool IsPlaying { get; }
    float CurrentPitch { get; }
    float CurrentVolume { get; }
    EngineProfile? CurrentProfile { get; }

    Task InitializeAsync();
    Task LoadProfileAsync(EngineProfile profile);
    void Play();
    void Stop();
    void SetRpm(float rpm);
    void SetVolume(float volume);
    void SetThrottle(float throttle);
    void Update(float deltaTime);
}
