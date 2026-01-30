using RevEV.Models;
using RevEV.Services.Settings;

namespace RevEV.Services.Audio;

public class EngineProfileManager : IEngineProfileManager
{
    private readonly IAppSettings _settings;
    private readonly List<EngineProfile> _profiles = new();

    public IReadOnlyList<EngineProfile> Profiles => _profiles.AsReadOnly();
    public EngineProfile? CurrentProfile { get; private set; }

    public EngineProfileManager(IAppSettings settings)
    {
        _settings = settings;
    }

    public async Task LoadProfilesAsync()
    {
        _profiles.Clear();

        // Load default profiles
        var defaultProfiles = EngineProfile.GetDefaultProfiles();
        _profiles.AddRange(defaultProfiles);

        // Try to load custom profiles from app data
        await LoadCustomProfilesAsync();

        // Select previously selected profile or first available
        var selectedId = _settings.SelectedEngineProfileId;
        if (!string.IsNullOrEmpty(selectedId))
        {
            CurrentProfile = GetProfile(selectedId);
        }

        CurrentProfile ??= _profiles.FirstOrDefault();
    }

    private async Task LoadCustomProfilesAsync()
    {
        try
        {
            var customProfilesDir = Path.Combine(FileSystem.AppDataDirectory, "Profiles");

            if (!Directory.Exists(customProfilesDir))
            {
                return;
            }

            var profileFiles = Directory.GetFiles(customProfilesDir, "*.json");

            foreach (var file in profileFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var profile = System.Text.Json.JsonSerializer.Deserialize<EngineProfile>(json);

                    if (profile != null && !_profiles.Any(p => p.Id == profile.Id))
                    {
                        _profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load custom profile {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load custom profiles: {ex.Message}");
        }
    }

    public EngineProfile? GetProfile(string id)
    {
        return _profiles.FirstOrDefault(p => p.Id == id);
    }

    public void SelectProfile(string id)
    {
        var profile = GetProfile(id);
        if (profile != null)
        {
            CurrentProfile = profile;
            _settings.SelectedEngineProfileId = id;
            _settings.Save();
        }
    }

    public async Task SaveCustomProfileAsync(EngineProfile profile)
    {
        try
        {
            var customProfilesDir = Path.Combine(FileSystem.AppDataDirectory, "Profiles");
            Directory.CreateDirectory(customProfilesDir);

            var filePath = Path.Combine(customProfilesDir, $"{profile.Id}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(profile,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            await File.WriteAllTextAsync(filePath, json);

            // Add to list if not already present
            if (!_profiles.Any(p => p.Id == profile.Id))
            {
                _profiles.Add(profile);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save custom profile: {ex.Message}");
            throw;
        }
    }

    public async Task DeleteCustomProfileAsync(string id)
    {
        try
        {
            var customProfilesDir = Path.Combine(FileSystem.AppDataDirectory, "Profiles");
            var filePath = Path.Combine(customProfilesDir, $"{id}.json");

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            var profile = _profiles.FirstOrDefault(p => p.Id == id);
            if (profile != null)
            {
                _profiles.Remove(profile);
            }

            // If current profile was deleted, select first available
            if (CurrentProfile?.Id == id)
            {
                CurrentProfile = _profiles.FirstOrDefault();
                if (CurrentProfile != null)
                {
                    _settings.SelectedEngineProfileId = CurrentProfile.Id;
                    _settings.Save();
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to delete custom profile: {ex.Message}");
            throw;
        }
    }
}
