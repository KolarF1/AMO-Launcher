// ModCategory.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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
            Name = name;
        }
    }

    public static class ModExtensions
    {
        // Group mods by their categories
        public static ObservableCollection<ModCategory> GroupByCategory(this IEnumerable<ModInfo> mods)
        {
            var categories = new ObservableCollection<ModCategory>();
            var categoryDict = new Dictionary<string, ModCategory>();

            // First pass: group mods by category
            foreach (var mod in mods)
            {
                string categoryName = mod.Category ?? "Uncategorized";

                if (!categoryDict.TryGetValue(categoryName, out ModCategory category))
                {
                    category = new ModCategory(categoryName);
                    categoryDict[categoryName] = category;
                    categories.Add(category);
                }

                category.Mods.Add(mod);
            }

            // Sort categories alphabetically, but keep "Uncategorized" at the end
            return new ObservableCollection<ModCategory>(
                categories.OrderBy(c => c.Name == "Uncategorized" ? "zzz" : c.Name)
            );
        }
    }
}