using System;
using System.Collections.Generic;

namespace AMO_Launcher.Models
{
    public class AppliedModSetting
    {
        public string ModFolderPath { get; set; }
        public bool IsActive { get; set; }
        public bool IsFromArchive { get; set; }
        public string ArchiveSource { get; set; }
        public string ArchiveRootPath { get; set; }
    }
}