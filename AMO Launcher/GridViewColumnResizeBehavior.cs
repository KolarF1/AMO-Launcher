using AMO_Launcher.Utilities;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AMO_Launcher.Views
{
    public class GridViewColumnResizeBehavior
    {
        #region DependencyProperties

        public static readonly DependencyProperty StretchProperty =
            DependencyProperty.RegisterAttached("Stretch",
                typeof(bool),
                typeof(GridViewColumnResizeBehavior),
                new PropertyMetadata(false, OnStretchChanged));

        public static readonly DependencyProperty StretchColumnProperty =
            DependencyProperty.RegisterAttached("StretchColumn",
                typeof(int),
                typeof(GridViewColumnResizeBehavior),
                new PropertyMetadata(-1));

        public static readonly DependencyProperty MinWidthProperty =
            DependencyProperty.RegisterAttached("MinWidth",
                typeof(double),
                typeof(GridViewColumnResizeBehavior),
                new PropertyMetadata(0.0));

        #endregion

        #region Getters and Setters

        public static bool GetStretch(DependencyObject obj)
        {
            return ErrorHandler.ExecuteSafe(() =>
                (bool)obj.GetValue(StretchProperty),
                "Getting Stretch property",
                showErrorToUser: false,
                defaultValue: false);
        }

        public static void SetStretch(DependencyObject obj, bool value)
        {
            ErrorHandler.ExecuteSafe(() =>
                obj.SetValue(StretchProperty, value),
                "Setting Stretch property");
        }

        public static int GetStretchColumn(DependencyObject obj)
        {
            return ErrorHandler.ExecuteSafe(() =>
                (int)obj.GetValue(StretchColumnProperty),
                "Getting StretchColumn property",
                showErrorToUser: false,
                defaultValue: -1);
        }

        public static void SetStretchColumn(DependencyObject obj, int value)
        {
            ErrorHandler.ExecuteSafe(() =>
                obj.SetValue(StretchColumnProperty, value),
                "Setting StretchColumn property");
        }

        public static double GetMinWidth(DependencyObject obj)
        {
            return ErrorHandler.ExecuteSafe(() =>
                (double)obj.GetValue(MinWidthProperty),
                "Getting MinWidth property",
                showErrorToUser: false,
                defaultValue: 0.0);
        }

        public static void SetMinWidth(DependencyObject obj, double value)
        {
            ErrorHandler.ExecuteSafe(() =>
                obj.SetValue(MinWidthProperty, value),
                "Setting MinWidth property");
        }

        #endregion

        #region Event Handlers

        private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (d is ListView listView)
                {
                    App.LogService?.LogDebug($"Stretch property changed for ListView: old={e.OldValue}, new={e.NewValue}");

                    bool oldValue = (bool)e.OldValue;
                    bool newValue = (bool)e.NewValue;

                    if (oldValue && !newValue)
                    {
                        App.LogService?.LogDebug("Removing SizeChanged event handler");
                        listView.SizeChanged -= ListView_SizeChanged;
                    }
                    else if (!oldValue && newValue)
                    {
                        App.LogService?.LogDebug("Adding SizeChanged event handler");
                        listView.SizeChanged += ListView_SizeChanged;

                        if (listView.IsLoaded)
                        {
                            App.LogService?.LogDebug("ListView already loaded, updating column widths");
                            UpdateColumnWidths(listView);
                        }
                        else
                        {
                            App.LogService?.LogDebug("ListView not loaded, adding Loaded event handler");
                            listView.Loaded += (s, args) =>
                            {
                                ErrorHandler.ExecuteSafe(() =>
                                {
                                    App.LogService?.LogDebug("ListView loaded, updating column widths");
                                    UpdateColumnWidths(listView);
                                }, "ListView Loaded event handler");
                            };
                        }
                    }
                }
                else
                {
                    App.LogService?.Warning($"Stretch property was set on a non-ListView object: {d?.GetType()?.Name ?? "null"}");
                }
            }, "Handling Stretch property change");
        }

        private static void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (sender is ListView listView)
                {
                    App.LogService?.LogDebug($"ListView size changed: old={e.PreviousSize}, new={e.NewSize}");
                    UpdateColumnWidths(listView);
                }
            }, "Handling ListView size change");
        }

        private static void UpdateColumnWidths(ListView listView)
        {
            ErrorHandler.ExecuteSafe(() =>
            {
                if (listView.View is GridView gridView)
                {
                    App.LogService?.LogDebug($"Updating column widths for GridView with {gridView.Columns.Count} columns");

                    int stretchColumnIndex = GetStretchColumn(listView);
                    if (stretchColumnIndex < 0)
                    {
                        stretchColumnIndex = 1;
                        App.LogService?.LogDebug($"Using default stretch column index: {stretchColumnIndex}");
                    }

                    if (stretchColumnIndex < gridView.Columns.Count)
                    {
                        double totalWidth = listView.ActualWidth - SystemParameters.VerticalScrollBarWidth;
                        double occupiedWidth = 0;

                        App.LogService?.LogDebug($"ListView width: {totalWidth:F1}px (minus scrollbar)");

                        for (int i = 0; i < gridView.Columns.Count; i++)
                        {
                            var column = gridView.Columns[i];

                            double minWidth = GetMinWidth(column);

                            if (minWidth > 0 && column.ActualWidth < minWidth)
                            {
                                App.LogService?.Trace($"Column {i}: Setting minimum width {minWidth:F1}px (was {column.ActualWidth:F1}px)");
                                column.Width = minWidth;
                            }

                            if (i != stretchColumnIndex)
                            {
                                occupiedWidth += column.ActualWidth;
                                App.LogService?.Trace($"Column {i}: Width {column.ActualWidth:F1}px (non-stretch)");
                            }
                            else
                            {
                                App.LogService?.Trace($"Column {i}: Current width {column.ActualWidth:F1}px (stretch column)");
                            }
                        }

                        double stretchWidth = Math.Max(totalWidth - occupiedWidth, 100);
                        App.LogService?.LogDebug($"Setting stretch column {stretchColumnIndex} width to {stretchWidth:F1}px");
                        gridView.Columns[stretchColumnIndex].Width = stretchWidth;
                    }
                    else
                    {
                        App.LogService?.Warning($"Stretch column index {stretchColumnIndex} is out of bounds (column count: {gridView.Columns.Count})");
                    }
                }
                else
                {
                    App.LogService?.Warning("ListView does not have a GridView");
                }
            }, "Updating GridView column widths");
        }

        #endregion
    }
}