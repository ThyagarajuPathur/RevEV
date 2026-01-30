using RevEV.Models;

namespace RevEV.Services.Audio;

public interface IEngineProfileManager
{
    IReadOnlyList<EngineProfile> Profiles { get; }
    EngineProfile? CurrentProfile { get; }
    EngineProfile? GetProfile(string id);
    void SelectProfile(string id);
    Task LoadProfilesAsync();
}
