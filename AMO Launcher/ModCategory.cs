// ModCategory.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using AMO_Launcher.Utilities;

namespace AMO_Launcher.Models
{
    public class ModCategory
    {
        public string Name { get; set; }
        public ObservableCollection<ModInfo> Mods { get; set; } = new ObservableCollection<ModInfo>();
        public bool IsExpanded { get; set; } = true;

        // Constructor
        public ModCategory(string name)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (string.IsNullOrEmpty(name))
                {
                    App.LogService?.Warning("Attempted to create category with null or empty name");
                    name = "Uncategorized";
                }

                Name = name;
                App.LogService?.LogDebug($"Created mod category: {name}");
            }, "ModCategory constructor", true);
        }
    }

    public static class ModExtensions
    {
        // Group mods by their categories with enhanced error handling
        public static ObservableCollection<ModCategory> GroupByCategory(this IEnumerable<ModInfo> mods)
        {
            return ErrorHandler.ExecuteSafe(() =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                App.LogService?.Info("Starting to group mods by category");

                // Check for null input
                if (mods == null)
                {
                    App.LogService?.Warning("Null mods collection passed to GroupByCategory");
                    return new ObservableCollection<ModCategory>();
                }

                var categories = new ObservableCollection<ModCategory>();
                var categoryDict = new Dictionary<string, ModCategory>();
                int modCount = 0;
                int errorCount = 0;

                // First pass: group mods by category
                foreach (var mod in mods)
                {
                    modCount++;

                    try
                    {
                        // Handle null mod
                        if (mod == null)
                        {
                            App.LogService?.Warning($"Encountered null mod at position {modCount}");
                            errorCount++;
                            continue;
                        }

                        string categoryName = mod.Category ?? "Uncategorized";
                        App.LogService?.Trace($"Processing mod: {mod.Name} (Category: {categoryName})");

                        if (!categoryDict.TryGetValue(categoryName, out ModCategory category))
                        {
                            category = new ModCategory(categoryName);
                            categoryDict[categoryName] = category;
                            categories.Add(category);
                            App.LogService?.LogDebug($"Created new category: {categoryName}");
                        }

                        category.Mods.Add(mod);
                    }
                    catch (Exception ex)
                    {
                        // Log but continue processing other mods
                        errorCount++;
                        App.LogService?.Error($"Error processing mod {modCount}: {ex.Message}");
                        App.LogService?.LogDebug($"Exception details: {ex}");
                    }
                }

                // Sort categories alphabetically, but keep "Uncategorized" at the end
                var sortedCategories = new ObservableCollection<ModCategory>(
                    categories.OrderBy(c => c.Name == "Uncategorized" ? "zzz" : c.Name)
                );

                // Log performance and results
                stopwatch.Stop();
                App.LogService?.Info($"Grouped {modCount} mods into {categories.Count} categories with {errorCount} errors");

                if (stopwatch.ElapsedMilliseconds > 500)
                {
                    App.LogService?.Warning($"Grouping operation took longer than expected: {stopwatch.ElapsedMilliseconds}ms");
                }
                else
                {
                    App.LogService?.LogDebug($"Grouping completed in {stopwatch.ElapsedMilliseconds}ms");
                }

                return sortedCategories;
            }, "GroupByCategory", true, new ObservableCollection<ModCategory>());
        }
    }
}