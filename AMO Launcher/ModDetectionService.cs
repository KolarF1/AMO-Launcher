using AMO_Launcher.Models;
using Newtonsoft.Json;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Tar;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Readers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AMO_Launcher.Services
{
    public class ModDetectionService
    {
        private readonly string _modsBasePath;
        private BitmapImage _defaultModIcon;
        private readonly string[] _supportedArchiveExtensions = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" };

        public ModDetectionService()
        {
            // Get the folder where the application is running
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            _modsBasePath = Path.Combine(appPath, "Mods");

            // Ensure the Mods folder exists
            if (!Directory.Exists(_modsBasePath))
            {
                Directory.CreateDirectory(_modsBasePath);
                System.Diagnostics.Debug.WriteLine($"Created Mods folder at {_modsBasePath}");
            }

            // Load default mod icon
            _defaultModIcon = LoadDefaultModIcon();
        }

        // Get or create the game-specific mods folder
        public string GetGameModsFolder(GameInfo game)
        {
            if (game == null) return null;

            // Use game name as folder name (replace invalid chars)
            string gameFolderName = string.Join("_", game.Name.Split(Path.GetInvalidFileNameChars()));
            string gameModsPath = Path.Combine(_modsBasePath, gameFolderName);

            // Create the folder if it doesn't exist
            if (!Directory.Exists(gameModsPath))
            {
                Directory.CreateDirectory(gameModsPath);
                System.Diagnostics.Debug.WriteLine($"Created game mods folder at {gameModsPath}");
            }

            return gameModsPath;
        }

        // Check if a file is a supported archive
        private bool IsArchiveFile(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return _supportedArchiveExtensions.Contains(extension);
        }

        // Process archives in the game's mod folder without extracting
        private async Task ProcessArchivesAsync(string gameModsFolder, string gameName, List<ModInfo> mods)
        {
            foreach (var archivePath in Directory.GetFiles(gameModsFolder).Where(f => IsArchiveFile(f)))
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"Processing archive file: {archivePath}");

                    // Read the mod data from the archive
                    var mod = await LoadModFromArchivePathAsync(archivePath, gameName);
                    if (mod != null)
                    {
                        mods.Add(mod);
                        System.Diagnostics.Debug.WriteLine($"Loaded mod from archive: {mod.Name} by {mod.Author}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing archive {archivePath}: {ex.Message}");
                }
            }
        }

        // Load mod from archive without extracting (accessible publicly)
        public async Task<ModInfo> LoadModFromArchivePathAsync(string archivePath, string currentGameName, string rootPath = null)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var archive = ArchiveFactory.Open(archivePath))
                    {
                        // Look for mod.json in the archive
                        var modJsonEntry = archive.Entries.FirstOrDefault(e =>
                            Path.GetFileName(e.Key).Equals("mod.json", StringComparison.OrdinalIgnoreCase));

                        if (modJsonEntry == null)
                        {
                            // Try to look in subdirectories
                            modJsonEntry = archive.Entries.FirstOrDefault(e =>
                                e.Key.EndsWith("/mod.json", StringComparison.OrdinalIgnoreCase) ||
                                e.Key.EndsWith("\\mod.json", StringComparison.OrdinalIgnoreCase));
                        }

                        if (modJsonEntry == null)
                        {
                            System.Diagnostics.Debug.WriteLine($"No mod.json found in archive {archivePath}");
                            return null;
                        }

                        string modJsonContent;
                        using (var reader = new StreamReader(modJsonEntry.OpenEntryStream()))
                        {
                            modJsonContent = reader.ReadToEnd();
                        }

                        // Parse the mod.json content
                        var modData = ParseModJson(modJsonContent); // Use the new method

                        // Skip this mod if it doesn't specify a game or doesn't match the current game
                        if (string.IsNullOrEmpty(modData.Game) ||
                            !modData.Game.Equals(currentGameName, StringComparison.OrdinalIgnoreCase))
                        {
                            System.Diagnostics.Debug.WriteLine($"Mod in {archivePath} has no game specified or doesn't match current game");
                            return null;
                        }

                        // Get the parent directory of mod.json to determine the root path inside the archive
                        string modRootInArchive = rootPath;
                        if (string.IsNullOrEmpty(modRootInArchive))
                        {
                            modRootInArchive = Path.GetDirectoryName(modJsonEntry.Key)?.Replace('\\', '/') ?? "";
                            if (!string.IsNullOrEmpty(modRootInArchive) && !modRootInArchive.EndsWith("/"))
                            {
                                modRootInArchive += "/";
                            }
                        }

                        // Create mod info
                        var modInfo = new ModInfo
                        {
                            Name = string.IsNullOrEmpty(modData.Name) ? "Unknown Mod" : modData.Name,
                            Description = modData.Description ?? "",
                            Version = string.IsNullOrEmpty(modData.Version) ? "N/A" : modData.Version,
                            Author = string.IsNullOrEmpty(modData.Author) ? "Unknown" : modData.Author,
                            Game = modData.Game,
                            Category = modData.Category ?? "Uncategorized", // Handle the category
                            IsFromArchive = true,
                            ArchiveSource = archivePath,
                            ArchiveRootPath = modRootInArchive
                        };

                        // Look for icon.png in the archive
                        var iconEntry = archive.Entries.FirstOrDefault(e =>
                            Path.GetFileName(e.Key).Equals("icon.png", StringComparison.OrdinalIgnoreCase) &&
                            Path.GetDirectoryName(e.Key)?.Replace('\\', '/') == modRootInArchive.TrimEnd('/'));

                        if (iconEntry != null)
                        {
                            try
                            {
                                using (var stream = iconEntry.OpenEntryStream())
                                {
                                    var memoryStream = new MemoryStream();
                                    stream.CopyTo(memoryStream);
                                    memoryStream.Position = 0;

                                    var bitmap = new BitmapImage();
                                    bitmap.BeginInit();
                                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                    bitmap.StreamSource = memoryStream;
                                    bitmap.EndInit();
                                    bitmap.Freeze(); // Make it thread safe

                                    modInfo.Icon = bitmap;
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error loading icon from archive {archivePath}: {ex.Message}");
                                modInfo.Icon = _defaultModIcon;
                            }
                        }
                        else
                        {
                            modInfo.Icon = _defaultModIcon;
                        }

                        return modInfo;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading mod from archive {archivePath}: {ex.Message}");
                    return null;
                }
            });
        }

        // Scan for mods for a specific game
        public async Task<List<ModInfo>> ScanForModsAsync(GameInfo game)
        {
            var mods = new List<ModInfo>();
            if (game == null) return mods;

            return await Task.Run(async () =>
            {
                try
                {
                    string gameModsFolder = GetGameModsFolder(game);
                    if (string.IsNullOrEmpty(gameModsFolder) || !Directory.Exists(gameModsFolder))
                    {
                        return mods;
                    }

                    System.Diagnostics.Debug.WriteLine($"Scanning for mods in {gameModsFolder}");

                    // Process all subdirectories (each could be a mod)
                    foreach (var modFolder in Directory.GetDirectories(gameModsFolder))
                    {
                        try
                        {
                            var mod = LoadModFromFolderPath(modFolder, game.Name);
                            if (mod != null)
                            {
                                mods.Add(mod);
                                System.Diagnostics.Debug.WriteLine($"Found mod: {mod.Name} by {mod.Author}");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log error and continue with next mod
                            System.Diagnostics.Debug.WriteLine($"Error loading mod from {modFolder}: {ex.Message}");
                        }
                    }

                    // Process archive files without extracting
                    await ProcessArchivesAsync(gameModsFolder, game.Name, mods);

                    System.Diagnostics.Debug.WriteLine($"Found {mods.Count} mods for game {game.Name}");
                    return mods;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error scanning for mods: {ex.Message}");
                    return mods;
                }
            });
        }

        // Load a mod from a folder (accessible publicly)
        public ModInfo LoadModFromFolderPath(string folderPath, string currentGameName)
        {
            // Check for mod.json file
            string modJsonPath = Path.Combine(folderPath, "mod.json");
            if (!File.Exists(modJsonPath))
            {
                System.Diagnostics.Debug.WriteLine($"No mod.json found in {folderPath}");
                return null;
            }

            try
            {
                // Parse mod.json
                string jsonContent = File.ReadAllText(modJsonPath);
                var modData = ParseModJson(jsonContent); // Use the new method

                // Skip this mod if it doesn't specify a game or doesn't match the current game
                if (string.IsNullOrEmpty(modData.Game) ||
                    !modData.Game.Equals(currentGameName, StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"Mod in {folderPath} has no game specified or doesn't match current game");
                    return null;
                }

                // Create mod info
                var modInfo = new ModInfo
                {
                    Name = string.IsNullOrEmpty(modData.Name) ? "Unknown Mod" : modData.Name,
                    Description = modData.Description ?? "",
                    Version = string.IsNullOrEmpty(modData.Version) ? "N/A" : modData.Version,
                    Author = string.IsNullOrEmpty(modData.Author) ? "Unknown" : modData.Author,
                    Game = modData.Game,
                    Category = modData.Category ?? "Uncategorized", // Handle the category
                    ModFolderPath = folderPath,
                    ModFilesPath = Path.Combine(folderPath, "Mod"),
                    IsFromArchive = false
                };

                // Load icon if exists
                string iconPath = Path.Combine(folderPath, "icon.png");
                if (File.Exists(iconPath))
                {
                    modInfo.Icon = LoadIconFromFile(iconPath);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No icon.png found for mod in {folderPath}, using default");
                    modInfo.Icon = _defaultModIcon;
                }

                return modInfo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing mod.json from {folderPath}: {ex.Message}");
                return null;
            }
        }

        // Load icon from file
        private BitmapImage LoadIconFromFile(string iconPath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(iconPath);
                bitmap.EndInit();
                bitmap.Freeze(); // Make it thread safe
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading icon from {iconPath}: {ex.Message}");
                return _defaultModIcon;
            }
        }

        // Load default mod icon (question mark icon)
        private BitmapImage LoadDefaultModIcon()
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultModIcon.png");
                bitmap.EndInit();
                bitmap.Freeze(); // Make it thread safe
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading default mod icon: {ex.Message}");
                return null;
            }
        }

        private ModData ParseModJson(string jsonContent)
        {
            try
            {
                return JsonConvert.DeserializeObject<ModData>(jsonContent);
            }
            catch (JsonReaderException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON parsing error: {ex.Message} - Attempting to clean the JSON");

                // 1. Handle BOM and encoding issues by normalizing line endings
                jsonContent = jsonContent.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);

                // 2. Handle trailing commas
                string cleanedJson = System.Text.RegularExpressions.Regex.Replace(
                    jsonContent,
                    @",\s*([}\]])",
                    "$1"
                );

                // 3. Remove any non-printable characters
                cleanedJson = System.Text.RegularExpressions.Regex.Replace(
                    cleanedJson,
                    @"[\u0000-\u001F\u007F-\u009F]",
                    string.Empty
                );

                try
                {
                    return JsonConvert.DeserializeObject<ModData>(cleanedJson);
                }
                catch (JsonReaderException innerEx)
                {
                    // 4. If all else fails, try a more aggressive approach - rewrite the entire JSON manually
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"First cleanup failed: {innerEx.Message} - Trying manual parsing");

                        // Extract values using simple regex patterns
                        var nameMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                        var descMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"description\"\\s*:\\s*\"([^\"]*)\"");
                        var versionMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"version\"\\s*:\\s*\"([^\"]*)\"");
                        var authorMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"author\"\\s*:\\s*\"([^\"]*)\"");
                        var gameMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"game\"\\s*:\\s*\"([^\"]*)\"");
                        var categoryMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"category\"\\s*:\\s*\"([^\"]*)\"");

                        // Construct a clean JSON object
                        var modData = new ModData
                        {
                            Name = nameMatch.Success ? nameMatch.Groups[1].Value : null,
                            Description = descMatch.Success ? descMatch.Groups[1].Value : null,
                            Version = versionMatch.Success ? versionMatch.Groups[1].Value : null,
                            Author = authorMatch.Success ? authorMatch.Groups[1].Value : null,
                            Game = gameMatch.Success ? gameMatch.Groups[1].Value : null,
                            Category = categoryMatch.Success ? categoryMatch.Groups[1].Value : null
                        };

                        return modData;
                    }
                    catch (Exception finalEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"All JSON parsing attempts failed: {finalEx.Message}");
                        // Rethrow the original exception for better diagnostics
                        throw ex;
                    }
                }
            }
        }

        // Helper class for deserializing mod.json
        private class ModData
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("version")]
            public string Version { get; set; }

            [JsonProperty("author")]
            public string Author { get; set; }

            [JsonProperty("game")]
            public string Game { get; set; }

            [JsonProperty("category")]
            public string Category { get; set; }
        }
    }
}