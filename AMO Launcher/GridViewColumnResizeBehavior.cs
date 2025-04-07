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
            return (bool)obj.GetValue(StretchProperty);
        }

        public static void SetStretch(DependencyObject obj, bool value)
        {
            obj.SetValue(StretchProperty, value);
        }

        public static int GetStretchColumn(DependencyObject obj)
        {
            return (int)obj.GetValue(StretchColumnProperty);
        }

        public static void SetStretchColumn(DependencyObject obj, int value)
        {
            obj.SetValue(StretchColumnProperty, value);
        }

        public static double GetMinWidth(DependencyObject obj)
        {
            return (double)obj.GetValue(MinWidthProperty);
        }

        public static void SetMinWidth(DependencyObject obj, double value)
        {
            obj.SetValue(MinWidthProperty, value);
        }

        #endregion

        #region Event Handlers

        private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ListView listView)
            {
                bool oldValue = (bool)e.OldValue;
                bool newValue = (bool)e.NewValue;

                if (oldValue && !newValue)
                {
                    listView.SizeChanged -= ListView_SizeChanged;
                }
                else if (!oldValue && newValue)
                {
                    listView.SizeChanged += ListView_SizeChanged;
                    if (listView.IsLoaded)
                    {
                        // Manually trigger a resize without creating a SizeChangedEventArgs
                        UpdateColumnWidths(listView);
                    }
                    else
                    {
                        listView.Loaded += (s, args) =>
                        {
                            // Manually trigger a resize when loaded
                            UpdateColumnWidths(listView);
                        };
                    }
                }
            }
        }

        private static void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ListView listView)
            {
                UpdateColumnWidths(listView);
            }
        }

        private static void UpdateColumnWidths(ListView listView)
        {
            if (listView.View is GridView gridView)
            {
                int stretchColumnIndex = GetStretchColumn(listView);
                if (stretchColumnIndex < 0) stretchColumnIndex = 1; // Default to second column if not set

                if (stretchColumnIndex < gridView.Columns.Count)
                {
                    // Calculate the width available for the stretch column
                    double totalWidth = listView.ActualWidth - SystemParameters.VerticalScrollBarWidth;
                    double occupiedWidth = 0;

                    // First, ensure minimum widths are respected for all columns
                    for (int i = 0; i < gridView.Columns.Count; i++)
                    {
                        var column = gridView.Columns[i];

                        // Get the minimum width set for this column (if any)
                        double minWidth = GetMinWidth(column);

                        // If this column has a minimum width constraint and current width is less
                        if (minWidth > 0 && column.ActualWidth < minWidth)
                        {
                            column.Width = minWidth;
                        }

                        // If this is not the stretch column, add its width to occupied space
                        if (i != stretchColumnIndex)
                        {
                            occupiedWidth += column.ActualWidth;
                        }
                    }

                    // Set the stretch column width to take up remaining space
                    double stretchWidth = Math.Max(totalWidth - occupiedWidth, 100); // Ensure minimum width
                    gridView.Columns[stretchColumnIndex].Width = stretchWidth;
                }
            }
        }

        #endregion
    }
}
