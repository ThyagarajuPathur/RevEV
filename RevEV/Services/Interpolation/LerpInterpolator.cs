using RevEV.Models;
using RevEV.Services.Settings;

namespace RevEV.Services.Interpolation;

/// <summary>
/// Provides smooth linear interpolation for vehicle data values.
/// Used to create smooth audio transitions from discrete OBD-II updates.
/// </summary>
public class LerpInterpolator
{
    private readonly IAppSettings _settings;
    private float _currentRpm;
    private float _currentSpeed;
    private float _targetRpm;
    private float _targetSpeed;

    public float CurrentRpm => _currentRpm;
    public float CurrentSpeed => _currentSpeed;
    public float TargetRpm => _targetRpm;
    public float TargetSpeed => _targetSpeed;

    public LerpInterpolator(IAppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Updates target values from vehicle data.
    /// </summary>
    public void SetTargetValues(VehicleData data)
    {
        _targetRpm = data.Rpm;
        _targetSpeed = data.Speed;
    }

    /// <summary>
    /// Updates target RPM.
    /// </summary>
    public void SetTargetRpm(float rpm)
    {
        _targetRpm = rpm;
    }

    /// <summary>
    /// Updates target speed.
    /// </summary>
    public void SetTargetSpeed(float speed)
    {
        _targetSpeed = speed;
    }

    /// <summary>
    /// Performs one interpolation step and returns the smoothed RPM value.
    /// Call this at 60Hz (every frame) for smooth audio.
    /// </summary>
    public float Interpolate(float targetValue)
    {
        float smoothingFactor = _settings.SmoothingFactor;
        _currentRpm = _currentRpm + (targetValue - _currentRpm) * smoothingFactor;
        return _currentRpm;
    }

    /// <summary>
    /// Performs one interpolation step for all values.
    /// Call this at 60Hz for smooth transitions.
    /// </summary>
    public VehicleData InterpolateAll()
    {
        float smoothingFactor = _settings.SmoothingFactor;

        // Lerp formula: current = current + (target - current) * smoothingFactor
        _currentRpm = _currentRpm + (_targetRpm - _currentRpm) * smoothingFactor;
        _currentSpeed = _currentSpeed + (_targetSpeed - _currentSpeed) * smoothingFactor;

        return new VehicleData
        {
            Rpm = _targetRpm,
            Speed = _targetSpeed,
            InterpolatedRpm = _currentRpm,
            InterpolatedSpeed = _currentSpeed,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets interpolated RPM without advancing the interpolation.
    /// </summary>
    public float GetInterpolatedRpm() => _currentRpm;

    /// <summary>
    /// Gets interpolated speed without advancing the interpolation.
    /// </summary>
    public float GetInterpolatedSpeed() => _currentSpeed;

    /// <summary>
    /// Resets interpolation to match current target values immediately.
    /// Useful when connecting or after a large data gap.
    /// </summary>
    public void Reset()
    {
        _currentRpm = _targetRpm;
        _currentSpeed = _targetSpeed;
    }

    /// <summary>
    /// Resets interpolation to specific values.
    /// </summary>
    public void Reset(float rpm, float speed)
    {
        _currentRpm = rpm;
        _currentSpeed = speed;
        _targetRpm = rpm;
        _targetSpeed = speed;
    }

    /// <summary>
    /// Calculates the smoothing factor needed to reach 95% of target
    /// within a specified number of frames.
    /// </summary>
    public static float CalculateSmoothingFactor(int framesToSettle, float targetPercentage = 0.95f)
    {
        // Using the formula: (1 - smoothing)^frames = (1 - targetPercentage)
        // Solving for smoothing: smoothing = 1 - (1 - targetPercentage)^(1/frames)
        return 1.0f - MathF.Pow(1.0f - targetPercentage, 1.0f / framesToSettle);
    }
}
