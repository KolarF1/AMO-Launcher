using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AMO_Launcher.Models
{
    public class ModProfile
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonPropertyName("name")]
        public string Name { get; set; } = "Default Profile";

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; } = DateTime.Now;

        [JsonPropertyName("appliedMods")]
        public List<AppliedModSetting> AppliedMods { get; set; } = new List<AppliedModSetting>();

        public ModProfile()
        {
            Id = Guid.NewGuid().ToString();
            Name = "Default Profile";
            LastModified = DateTime.Now;
            AppliedMods = new List<AppliedModSetting>();
        }

        public static ModProfile Create(string name)
        {
            return new ModProfile { Name = name ?? "New Profile" };
        }
    }
}