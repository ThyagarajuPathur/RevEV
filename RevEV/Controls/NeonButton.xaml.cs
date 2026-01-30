using System.Windows.Input;

namespace RevEV.Controls;

public partial class NeonButton : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(NeonButton), string.Empty,
            propertyChanged: OnTextChanged);

    public static readonly BindableProperty NeonColorProperty =
        BindableProperty.Create(nameof(NeonColor), typeof(Color), typeof(NeonButton),
            Color.FromArgb("#00FFFF"), propertyChanged: OnNeonColorChanged);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(NeonButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(NeonButton));

    public static readonly BindableProperty IsFilledProperty =
        BindableProperty.Create(nameof(IsFilled), typeof(bool), typeof(NeonButton), false,
            propertyChanged: OnIsFilledChanged);

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Color NeonColor
    {
        get => (Color)GetValue(NeonColorProperty);
        set => SetValue(NeonColorProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public event EventHandler? Clicked;

    public NeonButton()
    {
        InitializeComponent();
        UpdateAppearance();
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var button = (NeonButton)bindable;
        button.ButtonLabel.Text = newValue?.ToString() ?? string.Empty;
    }

    private static void OnNeonColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((NeonButton)bindable).UpdateAppearance();
    }

    private static void OnIsFilledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((NeonButton)bindable).UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        ButtonBorder.Stroke = new SolidColorBrush(NeonColor);

        if (IsFilled)
        {
            ButtonBorder.BackgroundColor = NeonColor;
            ButtonLabel.TextColor = Color.FromArgb("#000000"); // Void Black
        }
        else
        {
            ButtonBorder.BackgroundColor = Colors.Transparent;
            ButtonLabel.TextColor = NeonColor;
        }

        ButtonLabel.Text = Text;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        // Visual feedback
        await ButtonBorder.ScaleTo(0.95, 50);
        await ButtonBorder.ScaleTo(1.0, 50);

        // Haptic feedback
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch { /* Ignore if not supported */ }

        // Invoke command
        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }

        Clicked?.Invoke(this, EventArgs.Empty);
    }
}
