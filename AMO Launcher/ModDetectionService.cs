using AMO_Launcher.Models;
using AMO_Launcher.Utilities;
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
        private readonly string _modsBasePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Mods");

        private BitmapImage _defaultModIcon;
        private readonly string[] _supportedArchiveExtensions = { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2" };

        public ModDetectionService()
        {
            InitializeDefaultIcon();
            InitializeFolders();
        }

        private void InitializeDefaultIcon()
        {
            _defaultModIcon = ErrorHandler.ExecuteSafe<BitmapImage>(() =>
            {
                App.LogService.LogDebug("Loading default mod icon");

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri("pack://application:,,,/AMO_Launcher;component/Resources/DefaultModIcon.png");
                    bitmap.EndInit();
                    bitmap.Freeze();
                    App.LogService.LogDebug("Default icon loaded successfully");
                    return bitmap;
                }
                catch (Exception ex)
                {
                    App.LogService.Error($"Error loading default mod icon: {ex.Message}");
                    App.LogService.LogDebug($"Exception details: {ex}");
                    return null;
                }
            }, "Loading default mod icon", false);
        }

        private void InitializeFolders()
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                App.LogService.Info("Initializing ModDetectionService");
                App.LogService.LogDebug($"Mods base path set to: {_modsBasePath}");

                if (!Directory.Exists(_modsBasePath))
                {
                    App.LogService.LogDebug($"Creating Mods folder at {_modsBasePath}");
                    Directory.CreateDirectory(_modsBasePath);
                    App.LogService.Info($"Created Mods folder at {_modsBasePath}");
                }

                App.LogService.Info("ModDetectionService initialized successfully");
            }, "ModDetectionService initialization");
        }

        public string GetGameModsFolder(GameInfo game)
        {
            return ErrorHandler.ExecuteSafe<string>(() =>
            {
                if (game == null)
                {
                    App.LogService.Warning("GetGameModsFolder called with null game");
                    return null;
                }

                string gameFolderName = string.Join("_", game.Name.Split(Path.GetInvalidFileNameChars()));
                string gameModsPath = Path.Combine(_modsBasePath, gameFolderName);
                App.LogService.LogDebug($"Game mods folder path: {gameModsPath}");

                if (!Directory.Exists(gameModsPath))
                {
                    App.LogService.LogDebug($"Creating game mods folder at {gameModsPath}");
                    Directory.CreateDirectory(gameModsPath);
                    App.LogService.Info($"Created game mods folder for {game.Name}");
                }

                return gameModsPath;
            }, "Getting game mods folder", true, null);
        }

        private bool IsArchiveFile(string filePath)
        {
            return ErrorHandler.ExecuteSafe<bool>(() =>
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    App.LogService.Warning("IsArchiveFile called with null or empty path");
                    return false;
                }

                string extension = Path.GetExtension(filePath).ToLowerInvariant();
                bool result = _supportedArchiveExtensions.Contains(extension);

                App.LogService.Trace($"Checking if file is archive: {filePath} - Result: {result}");
                return result;
            }, "Checking archive file", false, false);
        }

        private async Task ProcessArchivesAsync(string gameModsFolder, string gameName, List<ModInfo> mods)
        {
            await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(gameModsFolder) || !Directory.Exists(gameModsFolder))
                {
                    App.LogService.Warning($"ProcessArchivesAsync called with invalid folder: {gameModsFolder}");
                    return;
                }

                App.LogService.LogDebug($"Processing archive files in {gameModsFolder}");
                var archiveFiles = Directory.GetFiles(gameModsFolder).Where(f => IsArchiveFile(f)).ToList();
                App.LogService.LogDebug($"Found {archiveFiles.Count} archive files");

                int processedCount = 0;
                int successCount = 0;

                foreach (var archivePath in archiveFiles)
                {
                    processedCount++;
                    App.LogService.LogDebug($"Processing archive file ({processedCount}/{archiveFiles.Count}): {Path.GetFileName(archivePath)}");

                    try
                    {
                        string archiveFileName = Path.GetFileName(archivePath);

                        var mod = await LoadModFromArchivePathAsync(archivePath, gameName);
                        if (mod != null)
                        {
                            mods.Add(mod);
                            successCount++;
                            App.LogService.LogDebug($"Successfully loaded mod from archive: {mod.Name} by {mod.Author}");
                        }
                        else
                        {
                            App.LogService.LogDebug($"No valid mod found in archive: {archiveFileName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogService.Error($"Error processing archive {Path.GetFileName(archivePath)}: {ex.Message}");
                        App.LogService.LogDebug($"Archive error details: {ex}");
                    }
                }

                App.LogService.Info($"Archive processing complete: {successCount} of {archiveFiles.Count} archives contained valid mods");
            }, "Processing archive files");
        }

        public async Task<ModInfo> LoadModFromArchivePathAsync(string archivePath, string currentGameName, string rootPath = null)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath))
                {
                    App.LogService.Warning($"LoadModFromArchivePathAsync called with invalid path: {archivePath}");
                    return null;
                }

                if (string.IsNullOrEmpty(currentGameName))
                {
                    App.LogService.Warning("LoadModFromArchivePathAsync called with empty game name");
                    return null;
                }

                string archiveFileName = Path.GetFileName(archivePath);
                App.LogService.LogDebug($"Loading mod from archive: {archiveFileName}");

                return await Task.Run(() =>
                {
                    try
                    {
                        using (var archive = ArchiveFactory.Open(archivePath))
                        {
                            App.LogService.Trace($"Archive opened successfully: {archiveFileName} - {archive.Entries.Count()} entries");

                            var modJsonEntry = archive.Entries.FirstOrDefault(e =>
                                Path.GetFileName(e.Key).Equals("mod.json", StringComparison.OrdinalIgnoreCase));

                            if (modJsonEntry == null)
                            {
                                modJsonEntry = archive.Entries.FirstOrDefault(e =>
                                    e.Key.EndsWith("/mod.json", StringComparison.OrdinalIgnoreCase) ||
                                    e.Key.EndsWith("\\mod.json", StringComparison.OrdinalIgnoreCase));
                            }

                            if (modJsonEntry == null)
                            {
                                App.LogService.LogDebug($"No mod.json found in archive {archiveFileName}");
                                return null;
                            }

                            App.LogService.Trace($"Found mod.json at {modJsonEntry.Key}");

                            string modJsonContent;
                            using (var reader = new StreamReader(modJsonEntry.OpenEntryStream()))
                            {
                                modJsonContent = reader.ReadToEnd();
                            }

                            var modData = ParseModJson(modJsonContent);
                            if (modData == null)
                            {
                                App.LogService.Warning($"Failed to parse mod.json from archive {archiveFileName}");
                                return null;
                            }

                            if (string.IsNullOrEmpty(modData.Game) ||
                                !modData.Game.Equals(currentGameName, StringComparison.OrdinalIgnoreCase))
                            {
                                App.LogService.LogDebug($"Mod in {archiveFileName} has game '{modData.Game}', expected '{currentGameName}'");
                                return null;
                            }

                            string modRootInArchive = rootPath;
                            if (string.IsNullOrEmpty(modRootInArchive))
                            {
                                modRootInArchive = Path.GetDirectoryName(modJsonEntry.Key)?.Replace('\\', '/') ?? "";
                                if (!string.IsNullOrEmpty(modRootInArchive) && !modRootInArchive.EndsWith("/"))
                                {
                                    modRootInArchive += "/";
                                }
                            }

                            App.LogService.Trace($"Mod root in archive: {modRootInArchive}");

                            var modInfo = new ModInfo
                            {
                                Name = string.IsNullOrEmpty(modData.Name) ? "Unknown Mod" : modData.Name,
                                Description = modData.Description ?? "",
                                Version = string.IsNullOrEmpty(modData.Version) ? "N/A" : modData.Version,
                                Author = string.IsNullOrEmpty(modData.Author) ? "Unknown" : modData.Author,
                                Game = modData.Game,
                                Category = modData.Category ?? "Uncategorized",
                                IsFromArchive = true,
                                ArchiveSource = archivePath,
                                ArchiveRootPath = modRootInArchive
                            };

                            App.LogService.LogDebug($"Created ModInfo from archive: {modInfo.Name} v{modInfo.Version} by {modInfo.Author}");

                            var iconEntry = archive.Entries.FirstOrDefault(e =>
                                Path.GetFileName(e.Key).Equals("icon.png", StringComparison.OrdinalIgnoreCase) &&
                                Path.GetDirectoryName(e.Key)?.Replace('\\', '/') == modRootInArchive.TrimEnd('/'));

                            if (iconEntry != null)
                            {
                                App.LogService.Trace($"Found icon.png at {iconEntry.Key}");
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
                                        bitmap.Freeze();

                                        modInfo.Icon = bitmap;
                                        App.LogService.Trace("Icon loaded successfully");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    App.LogService.Warning($"Error loading icon from archive {archiveFileName}: {ex.Message}");
                                    App.LogService.LogDebug($"Icon loading error details: {ex}");
                                    modInfo.Icon = _defaultModIcon;
                                }
                            }
                            else
                            {
                                App.LogService.Trace($"No icon.png found, using default icon");
                                modInfo.Icon = _defaultModIcon;
                            }

                            return modInfo;
                        }
                    }
                    catch (SharpCompress.Common.ArchiveException aex)
                    {
                        App.LogService.Error($"Archive format error: {aex.Message}");
                        App.LogService.LogDebug($"Archive exception details: {aex}");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        App.LogService.Error($"Error reading mod from archive {archiveFileName}: {ex.Message}");
                        App.LogService.LogDebug($"Exception details: {ex}");
                        return null;
                    }
                });
            }, $"Loading mod from archive: {Path.GetFileName(archivePath)}", false);
        }

        public async Task<List<ModInfo>> ScanForModsAsync(GameInfo game)
        {
            return await ErrorHandler.ExecuteSafeAsync(async () =>
            {
                var mods = new List<ModInfo>();
                if (game == null)
                {
                    App.LogService.Warning("ScanForModsAsync called with null game");
                    return mods;
                }

                var startTime = DateTime.Now;
                App.LogService.Info($"Starting mod scan for game: {game.Name}");

                string gameModsFolder = GetGameModsFolder(game);
                if (string.IsNullOrEmpty(gameModsFolder) || !Directory.Exists(gameModsFolder))
                {
                    App.LogService.Warning($"Mods folder not found for game: {game.Name}");
                    return mods;
                }

                App.LogService.LogDebug($"Scanning for mods in {gameModsFolder}");

                var folderScanStopwatch = new System.Diagnostics.Stopwatch();
                folderScanStopwatch.Start();

                var modFolders = Directory.GetDirectories(gameModsFolder);
                App.LogService.LogDebug($"Found {modFolders.Length} potential mod folders");

                int foldersProcessed = 0;
                int foldersSuccessful = 0;

                foreach (var modFolder in modFolders)
                {
                    foldersProcessed++;
                    string folderName = Path.GetFileName(modFolder);
                    App.LogService.Trace($"Processing folder ({foldersProcessed}/{modFolders.Length}): {folderName}");

                    try
                    {
                        var mod = LoadModFromFolderPath(modFolder, game.Name);
                        if (mod != null)
                        {
                            mods.Add(mod);
                            foldersSuccessful++;
                            App.LogService.LogDebug($"Successfully loaded mod from folder: {mod.Name} by {mod.Author}");
                        }
                        else
                        {
                            App.LogService.LogDebug($"No valid mod found in folder: {folderName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        App.LogService.Error($"Error loading mod from {folderName}: {ex.Message}");
                        App.LogService.LogDebug($"Exception details: {ex}");
                    }
                }

                folderScanStopwatch.Stop();
                App.LogService.LogDebug($"Folder scan completed in {folderScanStopwatch.ElapsedMilliseconds}ms: {foldersSuccessful} of {modFolders.Length} folders contained valid mods");

                var archiveScanStopwatch = new System.Diagnostics.Stopwatch();
                archiveScanStopwatch.Start();

                await ProcessArchivesAsync(gameModsFolder, game.Name, mods);

                archiveScanStopwatch.Stop();
                App.LogService.LogDebug($"Archive scan completed in {archiveScanStopwatch.ElapsedMilliseconds}ms");

                var elapsed = DateTime.Now - startTime;
                App.LogService.Info($"Mod scan complete for {game.Name}: Found {mods.Count} mods in {elapsed.TotalMilliseconds:F1}ms");

                if (elapsed.TotalSeconds > 5)
                {
                    App.LogService.Warning($"Mod scanning took longer than expected ({elapsed.TotalSeconds:F1} seconds). Consider optimizing the mod directory.");
                }

                return mods;
            }, $"Scanning for mods: {game?.Name ?? "unknown game"}", true, new List<ModInfo>());
        }

        public ModInfo LoadModFromFolderPath(string folderPath, string currentGameName)
        {
            return ErrorHandler.ExecuteSafe<ModInfo>(() =>
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                {
                    App.LogService.Warning($"LoadModFromFolderPath called with invalid folder: {folderPath}");
                    return null;
                }

                if (string.IsNullOrEmpty(currentGameName))
                {
                    App.LogService.Warning("LoadModFromFolderPath called with empty game name");
                    return null;
                }

                string folderName = Path.GetFileName(folderPath);
                App.LogService.LogDebug($"Loading mod from folder: {folderName}");

                string modJsonPath = Path.Combine(folderPath, "mod.json");
                if (!File.Exists(modJsonPath))
                {
                    App.LogService.LogDebug($"No mod.json found in {folderName}");
                    return null;
                }

                try
                {
                    string jsonContent = File.ReadAllText(modJsonPath);
                    App.LogService.Trace("Read mod.json content");

                    var modData = ParseModJson(jsonContent);
                    if (modData == null)
                    {
                        App.LogService.Warning($"Failed to parse mod.json from folder {folderName}");
                        return null;
                    }

                    if (string.IsNullOrEmpty(modData.Game) ||
                        !modData.Game.Equals(currentGameName, StringComparison.OrdinalIgnoreCase))
                    {
                        App.LogService.LogDebug($"Mod in {folderName} has game '{modData.Game}', expected '{currentGameName}'");
                        return null;
                    }

                    var modInfo = new ModInfo
                    {
                        Name = string.IsNullOrEmpty(modData.Name) ? "Unknown Mod" : modData.Name,
                        Description = modData.Description ?? "",
                        Version = string.IsNullOrEmpty(modData.Version) ? "N/A" : modData.Version,
                        Author = string.IsNullOrEmpty(modData.Author) ? "Unknown" : modData.Author,
                        Game = modData.Game,
                        Category = modData.Category ?? "Uncategorized",
                        ModFolderPath = folderPath,
                        ModFilesPath = Path.Combine(folderPath, "Mod"),
                        IsFromArchive = false
                    };

                    App.LogService.LogDebug($"Created ModInfo from folder: {modInfo.Name} v{modInfo.Version} by {modInfo.Author}");

                    string iconPath = Path.Combine(folderPath, "icon.png");
                    if (File.Exists(iconPath))
                    {
                        App.LogService.Trace($"Found icon.png at {iconPath}");
                        modInfo.Icon = LoadIconFromFile(iconPath);
                    }
                    else
                    {
                        App.LogService.Trace("No icon.png found, using default icon");
                        modInfo.Icon = _defaultModIcon;
                    }

                    return modInfo;
                }
                catch (Exception ex)
                {
                    App.LogService.Error($"Error parsing mod.json from {folderName}: {ex.Message}");
                    App.LogService.LogDebug($"Exception details: {ex}");
                    return null;
                }
            }, $"Loading mod from folder: {Path.GetFileName(folderPath)}", false);
        }

        private BitmapImage LoadIconFromFile(string iconPath)
        {
            return ErrorHandler.ExecuteSafe<BitmapImage>(() =>
            {
                if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                {
                    App.LogService.Warning($"LoadIconFromFile called with invalid path: {iconPath}");
                    return _defaultModIcon;
                }

                App.LogService.Trace($"Loading icon from {iconPath}");

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(iconPath);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    App.LogService.Trace("Icon loaded successfully");
                    return bitmap;
                }
                catch (Exception ex)
                {
                    App.LogService.Warning($"Error loading icon from {iconPath}: {ex.Message}");
                    App.LogService.LogDebug($"Icon loading error details: {ex}");
                    return _defaultModIcon;
                }
            }, $"Loading icon: {Path.GetFileName(iconPath)}", false, _defaultModIcon);
        }

        private ModData ParseModJson(string jsonContent)
        {
            return ErrorHandler.ExecuteSafe<ModData>(() =>
            {
                if (string.IsNullOrEmpty(jsonContent))
                {
                    App.LogService.Warning("ParseModJson called with empty content");
                    return null;
                }

                App.LogService.Trace("Parsing mod.json content");

                try
                {
                    return JsonConvert.DeserializeObject<ModData>(jsonContent);
                }
                catch (JsonReaderException ex)
                {
                    App.LogService.Warning($"JSON parsing error: {ex.Message}");
                    App.LogService.LogDebug("Attempting to clean the JSON");

                    jsonContent = jsonContent.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
                    App.LogService.Trace("Normalized line endings");

                    string cleanedJson = System.Text.RegularExpressions.Regex.Replace(
                        jsonContent,
                        @",\s*([}\]])",
                        "$1"
                    );
                    App.LogService.Trace("Removed trailing commas");

                    cleanedJson = System.Text.RegularExpressions.Regex.Replace(
                        cleanedJson,
                        @"[\u0000-\u001F\u007F-\u009F]",
                        string.Empty
                    );
                    App.LogService.Trace("Removed non-printable characters");

                    try
                    {
                        App.LogService.LogDebug("Attempting to parse cleaned JSON");
                        return JsonConvert.DeserializeObject<ModData>(cleanedJson);
                    }
                    catch (JsonReaderException innerEx)
                    {
                        App.LogService.Warning($"First cleanup failed: {innerEx.Message}");
                        App.LogService.LogDebug("Trying manual parsing with regex");

                        try
                        {
                            var nameMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"name\"\\s*:\\s*\"([^\"]*)\"");
                            var descMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"description\"\\s*:\\s*\"([^\"]*)\"");
                            var versionMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"version\"\\s*:\\s*\"([^\"]*)\"");
                            var authorMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"author\"\\s*:\\s*\"([^\"]*)\"");
                            var gameMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"game\"\\s*:\\s*\"([^\"]*)\"");
                            var categoryMatch = System.Text.RegularExpressions.Regex.Match(jsonContent, "\"category\"\\s*:\\s*\"([^\"]*)\"");

                            App.LogService.Trace($"Regex matches - Name: {nameMatch.Success}, Description: {descMatch.Success}, " +
                                $"Version: {versionMatch.Success}, Author: {authorMatch.Success}, " +
                                $"Game: {gameMatch.Success}, Category: {categoryMatch.Success}");

                            var modData = new ModData
                            {
                                Name = nameMatch.Success ? nameMatch.Groups[1].Value : null,
                                Description = descMatch.Success ? descMatch.Groups[1].Value : null,
                                Version = versionMatch.Success ? versionMatch.Groups[1].Value : null,
                                Author = authorMatch.Success ? authorMatch.Groups[1].Value : null,
                                Game = gameMatch.Success ? gameMatch.Groups[1].Value : null,
                                Category = categoryMatch.Success ? categoryMatch.Groups[1].Value : null
                            };

                            App.LogService.LogDebug("Manual parsing successful");
                            return modData;
                        }
                        catch (Exception finalEx)
                        {
                            App.LogService.Error($"All JSON parsing attempts failed: {finalEx.Message}");
                            App.LogService.LogDebug($"Final exception details: {finalEx}");
                            throw ex;
                        }
                    }
                }
            }, "Parsing mod.json", false);
        }

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