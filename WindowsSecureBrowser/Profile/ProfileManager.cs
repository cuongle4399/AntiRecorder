using System;
using System.Collections.Generic;
using System.IO;

using WindowsSecureBrowser.AppSystem;

namespace WindowsSecureBrowser.Profile
{
    public class UserProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Personal";
        public bool IsGuest { get; set; }
        public string UserDataFolder { get; set; } = "";
        public int ProtectionMode { get; set; } = 1; // 0: FullStealth, 1: AllowOSCapture, 2: Disabled
        public List<string> Bookmarks { get; set; } = new List<string>();

        public List<string> History { get; set; } = new List<string>();
    }

    public class ProfileManager
    {
        private readonly string _baseProfilesPath;
        public List<UserProfile> Profiles { get; } = new List<UserProfile>();
        public UserProfile CurrentProfile { get; private set; }

        public ProfileManager()
        {
            _baseProfilesPath = AppDataPath.ProfilesDir;
            Directory.CreateDirectory(_baseProfilesPath);


            // Initialize Personal Profile
            var personal = new UserProfile
            {
                Id = "personal_default",
                Name = "Personal",
                IsGuest = false,
                UserDataFolder = Path.Combine(_baseProfilesPath, "Personal")
            };

            Profiles.Add(personal);
            CurrentProfile = personal;
        }

        public UserProfile CreateGuestProfile()
        {
            string guestPath = Path.Combine(Path.GetTempPath(), "SecureBrowserSystem_Guest_" + Guid.NewGuid().ToString("N"));
            var guest = new UserProfile
            {
                Id = "guest_" + Guid.NewGuid().ToString("N"),
                Name = "Guest Profile",
                IsGuest = true,
                UserDataFolder = guestPath
            };
            return guest;
        }

        public void SwitchProfile(UserProfile profile)
        {
            CurrentProfile = profile;
        }

        public void SaveProfile(UserProfile profile)
        {
            if (profile.IsGuest) return; // Do not persist guest profiles

            string configPath = Path.Combine(profile.UserDataFolder, "profile_config.dat");
            ProfileStorage.SaveEncrypted(configPath, profile);
        }

        public void AddBookmark(string url)
        {
            if (!CurrentProfile.Bookmarks.Contains(url))
            {
                CurrentProfile.Bookmarks.Add(url);
                SaveProfile(CurrentProfile);
            }
        }

        public void AddHistory(string url)
        {
            if (CurrentProfile.IsGuest) return;

            CurrentProfile.History.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {url}");
            SaveProfile(CurrentProfile);
        }
    }
}
