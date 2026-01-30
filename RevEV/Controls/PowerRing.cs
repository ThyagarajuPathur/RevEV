using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace RevEV.Controls;

/// <summary>
/// Circular RPM gauge with neon glow effect.
/// </summary>
public class PowerRing : SKCanvasView
{
    private const float RingThickness = 20f;
    private const float GlowRadius = 15f;
    private const float StartAngle = 135f;  // Start from bottom-left
    private const float SweepAngle = 270f;  // Sweep to bottom-right

    public static readonly BindableProperty PercentageProperty =
        BindableProperty.Create(nameof(Percentage), typeof(float), typeof(PowerRing), 0f,
            propertyChanged: OnPercentageChanged);

    public static readonly BindableProperty RingColorProperty =
        BindableProperty.Create(nameof(RingColor), typeof(Color), typeof(PowerRing),
            Color.FromArgb("#00FFFF"), propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty BackgroundRingColorProperty =
        BindableProperty.Create(nameof(BackgroundRingColor), typeof(Color), typeof(PowerRing),
            Color.FromArgb("#1A1A2E"), propertyChanged: OnVisualPropertyChanged);

    public static readonly BindableProperty GlowColorProperty =
        BindableProperty.Create(nameof(GlowColor), typeof(Color), typeof(PowerRing),
            Color.FromArgb("#00FFFF"), propertyChanged: OnVisualPropertyChanged);

    public float Percentage
    {
        get => (float)GetValue(PercentageProperty);
        set => SetValue(PercentageProperty, Math.Clamp(value, 0f, 1f));
    }

    public Color RingColor
    {
        get => (Color)GetValue(RingColorProperty);
        set => SetValue(RingColorProperty, value);
    }

    public Color BackgroundRingColor
    {
        get => (Color)GetValue(BackgroundRingColorProperty);
        set => SetValue(BackgroundRingColorProperty, value);
    }

    public Color GlowColor
    {
        get => (Color)GetValue(GlowColorProperty);
        set => SetValue(GlowColorProperty, value);
    }

    public PowerRing()
    {
        IgnorePixelScaling = false;
    }

    private static void OnPercentageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((PowerRing)bindable).InvalidateSurface();
    }

    private static void OnVisualPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((PowerRing)bindable).InvalidateSurface();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var info = e.Info;
        float size = Math.Min(info.Width, info.Height);
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        float radius = (size / 2f) - RingThickness - GlowRadius;

        var rect = new SKRect(
            centerX - radius,
            centerY - radius,
            centerX + radius,
            centerY + radius);

        // Draw background ring
        using var bgPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = RingThickness,
            StrokeCap = SKStrokeCap.Round,
            Color = ToSKColor(BackgroundRingColor),
            IsAntialias = true
        };

        using var arcPath = new SKPath();
        arcPath.AddArc(rect, StartAngle, SweepAngle);
        canvas.DrawPath(arcPath, bgPaint);

        if (Percentage > 0)
        {
            float fillSweep = SweepAngle * Percentage;

            // Draw glow effect
            using var glowPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = RingThickness + GlowRadius * 2,
                StrokeCap = SKStrokeCap.Round,
                Color = ToSKColor(GlowColor).WithAlpha(80),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GlowRadius)
            };

            using var fillPath = new SKPath();
            fillPath.AddArc(rect, StartAngle, fillSweep);
            canvas.DrawPath(fillPath, glowPaint);

            // Draw filled ring with gradient
            var gradientColors = new SKColor[]
            {
                ToSKColor(RingColor),
                ToSKColor(RingColor).WithAlpha(200),
                ToSKColor(GlowColor)
            };

            var gradientPositions = new float[] { 0f, 0.7f, 1f };

            using var fillPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = RingThickness,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true,
                Shader = SKShader.CreateSweepGradient(
                    new SKPoint(centerX, centerY),
                    gradientColors,
                    gradientPositions,
                    SKShaderTileMode.Clamp,
                    StartAngle,
                    StartAngle + fillSweep)
            };

            canvas.DrawPath(fillPath, fillPaint);

            // Draw end cap glow
            DrawEndCapGlow(canvas, rect, StartAngle + fillSweep, centerX, centerY, radius);
        }

        // Draw tick marks
        DrawTickMarks(canvas, centerX, centerY, radius);
    }

    private void DrawEndCapGlow(SKCanvas canvas, SKRect rect, float angle, float centerX, float centerY, float radius)
    {
        // Calculate end point position
        float radians = (float)(angle * Math.PI / 180);
        float endX = centerX + radius * (float)Math.Cos(radians);
        float endY = centerY + radius * (float)Math.Sin(radians);

        // Draw glow at end point
        using var endGlowPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToSKColor(GlowColor),
            IsAntialias = true,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GlowRadius * 2)
        };

        canvas.DrawCircle(endX, endY, RingThickness / 2, endGlowPaint);
    }

    private void DrawTickMarks(SKCanvas canvas, float centerX, float centerY, float radius)
    {
        using var tickPaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = ToSKColor(BackgroundRingColor).WithAlpha(150),
            IsAntialias = true
        };

        // Draw major ticks at 0%, 25%, 50%, 75%, 100%
        float[] tickPositions = { 0f, 0.25f, 0.5f, 0.75f, 1f };

        foreach (var pos in tickPositions)
        {
            float tickAngle = StartAngle + (SweepAngle * pos);
            float radians = (float)(tickAngle * Math.PI / 180);

            float innerRadius = radius - RingThickness / 2 - 5;
            float outerRadius = radius - RingThickness / 2 - 15;

            float innerX = centerX + innerRadius * (float)Math.Cos(radians);
            float innerY = centerY + innerRadius * (float)Math.Sin(radians);
            float outerX = centerX + outerRadius * (float)Math.Cos(radians);
            float outerY = centerY + outerRadius * (float)Math.Sin(radians);

            canvas.DrawLine(innerX, innerY, outerX, outerY, tickPaint);
        }
    }

    private static SKColor ToSKColor(Color color)
    {
        return new SKColor(
            (byte)(color.Red * 255),
            (byte)(color.Green * 255),
            (byte)(color.Blue * 255),
            (byte)(color.Alpha * 255));
    }
}
