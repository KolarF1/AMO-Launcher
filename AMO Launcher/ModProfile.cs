using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AMO_Launcher.Models
{
    public class ModProfile
    {
        // Unique identifier for the profile
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        // Profile display name
        [JsonPropertyName("name")]
        public string Name { get; set; } = "Default Profile";

        // Ensure LastModified is serialized/deserialized correctly
        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; } = DateTime.Now;

        // List of mods - ensure proper serialization
        [JsonPropertyName("appliedMods")]
        public List<AppliedModSetting> AppliedMods { get; set; } = new List<AppliedModSetting>();

        // Default constructor
        public ModProfile()
        {
            // Initialize with default values
            Id = Guid.NewGuid().ToString();
            Name = "Default Profile";
            LastModified = DateTime.Now;
            AppliedMods = new List<AppliedModSetting>();
        }

        // Static factory method
        public static ModProfile Create(string name)
        {
            return new ModProfile { Name = name ?? "New Profile" };
        }
    }
}