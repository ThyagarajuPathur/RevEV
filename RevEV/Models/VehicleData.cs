using CommunityToolkit.Mvvm.ComponentModel;

namespace RevEV.Models;

public partial class VehicleData : ObservableObject
{
    [ObservableProperty]
    private float _rpm;

    [ObservableProperty]
    private float _speed;

    [ObservableProperty]
    private float _interpolatedRpm;

    [ObservableProperty]
    private float _interpolatedSpeed;

    [ObservableProperty]
    private float _throttlePosition;

    [ObservableProperty]
    private DateTime _timestamp;

    public float RpmPercentage => Math.Clamp(Rpm / MaxRpm, 0f, 1f);
    public float InterpolatedRpmPercentage => Math.Clamp(InterpolatedRpm / MaxRpm, 0f, 1f);

    public const float MaxRpm = 8000f;
    public const float MaxSpeed = 200f; // km/h or mph based on settings

    public VehicleData()
    {
        Timestamp = DateTime.UtcNow;
    }

    public VehicleData Clone()
    {
        return new VehicleData
        {
            Rpm = Rpm,
            Speed = Speed,
            InterpolatedRpm = InterpolatedRpm,
            InterpolatedSpeed = InterpolatedSpeed,
            ThrottlePosition = ThrottlePosition,
            Timestamp = Timestamp
        };
    }
}
