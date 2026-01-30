using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RevEV.Models;
using RevEV.Services.Audio;

namespace RevEV.ViewModels;

public partial class EngineBayViewModel : BaseViewModel
{
    private readonly IEngineProfileManager _profileManager;
    private readonly IAudioEngine _audioEngine;

    [ObservableProperty]
    private EngineProfile? _selectedProfile;

    [ObservableProperty]
    private bool _isPreviewPlaying;

    public ObservableCollection<EngineProfile> Profiles { get; } = new();

    public EngineBayViewModel(IEngineProfileManager profileManager, IAudioEngine audioEngine)
    {
        _profileManager = profileManager;
        _audioEngine = audioEngine;

        Title = "Engine Bay";
    }

    public async Task InitializeAsync()
    {
        await _profileManager.LoadProfilesAsync();
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();

        foreach (var profile in _profileManager.Profiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = _profileManager.CurrentProfile;
    }

    [RelayCommand]
    private async Task SelectProfileAsync(EngineProfile? profile)
    {
        if (profile == null) return;

        SelectedProfile = profile;
        _profileManager.SelectProfile(profile.Id);

        // Load into audio engine
        await _audioEngine.LoadProfileAsync(profile);
    }

    [RelayCommand]
    private async Task PreviewProfileAsync(EngineProfile? profile)
    {
        if (profile == null) return;

        if (IsPreviewPlaying)
        {
            _audioEngine.Stop();
            IsPreviewPlaying = false;
            return;
        }

        try
        {
            await _audioEngine.LoadProfileAsync(profile);

            // Set a mid-range RPM for preview
            _audioEngine.SetRpm(profile.BaseSampleRpm);
            _audioEngine.Play();
            IsPreviewPlaying = true;

            // Auto-stop after 3 seconds
            await Task.Delay(3000);

            if (IsPreviewPlaying)
            {
                _audioEngine.Stop();
                IsPreviewPlaying = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Preview failed: {ex.Message}";
            IsPreviewPlaying = false;
        }
    }

    [RelayCommand]
    private void StopPreview()
    {
        _audioEngine.Stop();
        IsPreviewPlaying = false;
    }
}
