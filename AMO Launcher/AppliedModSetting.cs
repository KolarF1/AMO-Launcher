using System;
using System.Text.Json.Serialization;

namespace AMO_Launcher.Models
{
    public class AppliedModSetting
    {
        [JsonPropertyName("modFolderPath")]
        public string ModFolderPath { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("isFromArchive")]
        public bool IsFromArchive { get; set; }

        [JsonPropertyName("archiveSource")]
        public string ArchiveSource { get; set; }

        [JsonPropertyName("archiveRootPath")]
        public string ArchiveRootPath { get; set; }
    }
}