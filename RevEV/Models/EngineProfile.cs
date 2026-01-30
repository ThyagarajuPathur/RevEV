namespace RevEV.Models;

public class EngineProfile
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AudioFileName { get; set; } = string.Empty;
    public string IconName { get; set; } = string.Empty;

    /// <summary>
    /// The RPM at which the base audio sample was recorded.
    /// Used to calculate pitch multiplier.
    /// </summary>
    public float BaseSampleRpm { get; set; } = 3000f;

    /// <summary>
    /// Minimum RPM this engine profile supports.
    /// </summary>
    public float MinRpm { get; set; } = 800f;

    /// <summary>
    /// Maximum RPM this engine profile supports.
    /// </summary>
    public float MaxRpm { get; set; } = 8000f;

    /// <summary>
    /// Minimum pitch multiplier (prevents audio from going too low).
    /// </summary>
    public float MinPitchMultiplier { get; set; } = 0.3f;

    /// <summary>
    /// Maximum pitch multiplier (prevents audio from going too high).
    /// </summary>
    public float MaxPitchMultiplier { get; set; } = 3.0f;

    /// <summary>
    /// Base volume level (0.0 to 1.0).
    /// </summary>
    public float BaseVolume { get; set; } = 1.0f;

    /// <summary>
    /// Engine characteristics tag for UI display.
    /// </summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Calculates the pitch multiplier for a given RPM.
    /// </summary>
    public float GetPitchMultiplier(float currentRpm)
    {
        float pitch = currentRpm / BaseSampleRpm;
        return Math.Clamp(pitch, MinPitchMultiplier, MaxPitchMultiplier);
    }

    /// <summary>
    /// Gets volume based on throttle position and RPM.
    /// </summary>
    public float GetVolume(float throttlePosition, float rpm)
    {
        // Base volume modulated by throttle position
        float throttleFactor = 0.5f + (throttlePosition * 0.5f);

        // RPM-based volume boost at higher RPMs
        float rpmFactor = 0.8f + (Math.Clamp(rpm / MaxRpm, 0f, 1f) * 0.2f);

        return Math.Clamp(BaseVolume * throttleFactor * rpmFactor, 0f, 1f);
    }

    public static EngineProfile[] GetDefaultProfiles()
    {
        return new[]
        {
            new EngineProfile
            {
                Id = "v8_muscle",
                Name = "V8 Muscle",
                Description = "Classic American V8 rumble with deep bass",
                AudioFileName = "v8_muscle_loop.wav",
                IconName = "engine_v8.png",
                BaseSampleRpm = 3000f,
                MinRpm = 600f,
                MaxRpm = 6500f,
                Tags = new[] { "V8", "Muscle", "Deep" }
            },
            new EngineProfile
            {
                Id = "sport_inline6",
                Name = "Sport Inline-6",
                Description = "High-revving sport car with smooth delivery",
                AudioFileName = "sport_loop.wav",
                IconName = "engine_sport.png",
                BaseSampleRpm = 4000f,
                MinRpm = 800f,
                MaxRpm = 8000f,
                Tags = new[] { "I6", "Sport", "Smooth" }
            },
            new EngineProfile
            {
                Id = "futuristic_whine",
                Name = "Futuristic Whine",
                Description = "Electric turbine with sci-fi overtones",
                AudioFileName = "futuristic_whine.wav",
                IconName = "engine_future.png",
                BaseSampleRpm = 5000f,
                MinRpm = 0f,
                MaxRpm = 12000f,
                MinPitchMultiplier = 0.1f,
                MaxPitchMultiplier = 4.0f,
                Tags = new[] { "EV", "Sci-Fi", "Whine" }
            }
        };
    }
}
