using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ScreenShield
{
    public class ShieldProfile
    {
        public string Name { get; set; } = "";
        public string CustomWallpaperPath { get; set; } = "";
        public List<string> AutoShieldProcessNames { get; set; } = new List<string>();
        public List<string> HiddenIconNames { get; set; } = new List<string>();
        public List<string> ManuallyShieldedProcessNames { get; set; } = new List<string>();
        public bool IsIconsShieldEnabled { get; set; } = false;
    }

    public class AppConfig
    {
        public string CurrentProfileName { get; set; } = "По умолчанию";
        public bool IsDesktopShieldEnabled { get; set; } = false;
        public bool IsIconsShieldEnabled { get; set; } = false;
        public bool IsHideTaskbarEnabled { get; set; } = false;
        public List<ShieldProfile> Profiles { get; set; } = new List<ShieldProfile>();

        public bool IsHotkeyEnabled { get; set; } = true;
        public int HotkeyModifiers { get; set; } = 6; // Ctrl + Shift
        public int HotkeyKey { get; set; } = 0x53; // 'S' key
        public string HotkeyText { get; set; } = "Ctrl + Shift + S";

        // Legacy properties for smooth migration
        public string CustomWallpaperPath { get; set; } = "";
        public List<string> AutoShieldProcessNames { get; set; } = new List<string>();
        public List<string> HiddenIconNames { get; set; } = new List<string>();
        public List<string> ManuallyShieldedProcessNames { get; set; } = new List<string>();
    }

    public static class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "config.json"
        );

        public static AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null)
                    {
                        config.Profiles ??= new List<ShieldProfile>();
                        foreach (var profile in config.Profiles)
                        {
                            profile.AutoShieldProcessNames ??= new List<string>();
                            profile.HiddenIconNames ??= new List<string>();
                            profile.ManuallyShieldedProcessNames ??= new List<string>();
                        }

                        // --- LEGACY MIGRATION LOGIC ---
                        if (config.Profiles.Count == 0)
                        {
                            var defaultProfile = new ShieldProfile
                            {
                                Name = "По умолчанию",
                                CustomWallpaperPath = config.CustomWallpaperPath ?? "",
                                AutoShieldProcessNames = config.AutoShieldProcessNames ?? new List<string>(),
                                HiddenIconNames = config.HiddenIconNames ?? new List<string>()
                            };
                            config.Profiles.Add(defaultProfile);
                            config.CurrentProfileName = "По умолчанию";
                            
                            // Save migrated config
                            SaveConfig(config);
                        }

                        return config;
                    }
                }
            }
            catch { }

            // Create default config if not found or corrupted
            var newConfig = new AppConfig();
            newConfig.Profiles.Add(new ShieldProfile
            {
                Name = "По умолчанию",
                CustomWallpaperPath = "",
                AutoShieldProcessNames = new List<string>(),
                HiddenIconNames = new List<string>()
            });
            newConfig.CurrentProfileName = "По умолчанию";
            SaveConfig(newConfig);

            return newConfig;
        }

        public static void SaveConfig(AppConfig config)
        {
            try
            {
                config ??= new AppConfig();
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(ConfigPath, json);
            }
            catch { }
        }
    }
}
