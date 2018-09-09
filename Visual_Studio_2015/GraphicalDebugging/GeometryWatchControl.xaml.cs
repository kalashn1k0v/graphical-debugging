﻿//------------------------------------------------------------------------------
// <copyright file="GeometryWatchControl.xaml.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

namespace GraphicalDebugging
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Windows;
    using System.Windows.Controls;

    using System.Drawing;
    using System.Drawing.Imaging;
    using System.IO;
    using System.Windows.Media.Imaging;

    using EnvDTE;
    using EnvDTE80;
    using Microsoft.VisualStudio.PlatformUI;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Utilities;

    using System.Collections.Generic;
    using System.Collections.ObjectModel;

    /// <summary>
    /// Interaction logic for GeometryWatchControl.
    /// </summary>
    public partial class GeometryWatchControl : UserControl
    {
        private DTE2 m_dte;
        private Debugger m_debugger;
        private DebuggerEvents m_debuggerEvents;

        Util.IntsPool m_intsPool;

        private bool m_isDataGridEdited;

        Colors m_colors;
        Bitmap m_emptyBitmap;

        System.Windows.Shapes.Rectangle m_selectionRect = new System.Windows.Shapes.Rectangle();
        Geometry.Point m_pointDown = new Geometry.Point(0, 0);
        bool m_mouseDown = false;
        ZoomBox m_zoomBox = new ZoomBox();

        ExpressionDrawer m_expressionDrawer = new ExpressionDrawer();

        ObservableCollection<GeometryItem> Geometries { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="GeometryWatchControl"/> class.
        /// </summary>
        public GeometryWatchControl()
        {
            m_dte = (DTE2)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
            m_debugger = m_dte.Debugger;
            m_debuggerEvents = m_dte.Events.DebuggerEvents;
            m_debuggerEvents.OnEnterBreakMode += DebuggerEvents_OnEnterBreakMode;

            VSColorTheme.ThemeChanged += VSColorTheme_ThemeChanged;

            m_isDataGridEdited = false;

            m_colors = new Colors(this);
            m_intsPool = new Util.IntsPool(m_colors.Count);

            this.InitializeComponent();

            m_emptyBitmap = new Bitmap(100, 100);
            Graphics graphics = Graphics.FromImage(m_emptyBitmap);
            graphics.Clear(m_colors.ClearColor);
            image.Source = Util.BitmapToBitmapImage(m_emptyBitmap);

            m_selectionRect.Width = 0;
            m_selectionRect.Height = 0;
            m_selectionRect.Visibility = Visibility.Hidden;
            System.Windows.Media.Color col = System.Windows.SystemColors.HighlightColor;
            col.A = 92;
            m_selectionRect.Fill = new System.Windows.Media.SolidColorBrush(col);
            imageCanvas.Children.Add(m_selectionRect);

            Geometries = new ObservableCollection<GeometryItem>();
            dataGrid.ItemsSource = Geometries;

            ResetAt(new GeometryItem(-1, m_colors), Geometries.Count);
        }

        private void VSColorTheme_ThemeChanged(ThemeChangedEventArgs e)
        {
            m_colors.Update();
            Graphics graphics = Graphics.FromImage(m_emptyBitmap);
            graphics.Clear(m_colors.ClearColor);
            UpdateItems();
        }

        private void GeometryItem_NameChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            //e.PropertyName == "Name"

            GeometryItem geometry = (GeometryItem)sender;
            int index = Geometries.IndexOf(geometry);

            if (index < 0 || index >= dataGrid.Items.Count)
                return;

            if (geometry.Name == null || geometry.Name == "")
            {
                if (index < dataGrid.Items.Count - 1)
                {
                    m_intsPool.Push(geometry.ColorId);
                    Geometries.RemoveAt(index);
                    UpdateItems();
                }
            }
            else
            {
                UpdateItems(index);

                // insert new empty row
                int next_index = index + 1;
                if (next_index == Geometries.Count)
                {
                    ResetAt(new GeometryItem(-1, m_colors), index + 1);
                    SelectAt(index + 1, true);
                }
                else
                {
                    SelectAt(index + 1);
                }
            }
        }

        private void ResetAt(GeometryItem item, int index)
        {
            ((System.ComponentModel.INotifyPropertyChanged)item).PropertyChanged += GeometryItem_NameChanged;
            if (index < Geometries.Count)
                Geometries.RemoveAt(index);
            Geometries.Insert(index, item);
        }

        private void SelectAt(int index, bool isNew = false)
        {
            object item = dataGrid.Items[index];

            if (isNew)
            {
                dataGrid.SelectedItem = item;
                dataGrid.ScrollIntoView(item);
                DataGridRow dgrow = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromItem(item);
                dgrow.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            }
            else
            {
                dataGrid.SelectedItem = item;
                dataGrid.ScrollIntoView(item);
            }
        }

        private void DebuggerEvents_OnEnterBreakMode(dbgEventReason Reason, ref dbgExecutionAction ExecutionAction)
        {
            UpdateItems();
        }

        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            UpdateItems();
        }

        private void GeometryWatchWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateItems();
        }

        private void dataGrid_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Delete)
            {
                if (m_isDataGridEdited)
                    return;

                if (dataGrid.SelectedItems.Count > 0)
                {
                    int[] indexes = new int[dataGrid.SelectedItems.Count];
                    int i = 0;
                    foreach (var item in dataGrid.SelectedItems)
                    {
                        indexes[i] = dataGrid.Items.IndexOf(item);
                        ++i;
                    }
                    System.Array.Sort(indexes, delegate (int l, int r)
                    {
                        return -l.CompareTo(r);
                    });

                    bool removed = false;
                    foreach (int index in indexes)
                    {
                        if (index + 1 < Geometries.Count)
                        {
                            GeometryItem geometry = Geometries[index];
                            m_intsPool.Push(geometry.ColorId);
                            Geometries.RemoveAt(index);

                            removed = true;
                        }
                    }

                    if (removed)
                    {
                        UpdateItems();
                    }
                }
            }
        }

        private void dataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            m_isDataGridEdited = true;
        }

        private void dataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            m_isDataGridEdited = false;
        }

        private void UpdateItems(int modified_index = -1)
        {
            bool imageEmpty = true;
            if (m_debugger.CurrentMode == dbgDebugMode.dbgBreakMode)
            {
                string[] names = new string[Geometries.Count];
                ExpressionDrawer.Settings[] settings = new ExpressionDrawer.Settings[Geometries.Count];
                bool tryDrawing = false;

                // update the list, gather names and settings
                for (int index = 0; index < Geometries.Count; ++index)
                {
                    GeometryItem geometry = Geometries[index];

                    System.Windows.Media.Color color = geometry.Color;
                    int colorId = geometry.ColorId;
                    string type = null;

                    bool updateRequred = modified_index < 0 || modified_index == index;

                    if (geometry.Name != null && geometry.Name != "")
                    {
                        var expression = updateRequred ? m_debugger.GetExpression(geometry.Name) : null;
                        if (expression == null || expression.IsValidValue)
                        {
                            if (expression != null)
                                type = expression.Type;

                            names[index] = geometry.Name;

                            if (updateRequred && geometry.ColorId < 0)
                            {
                                colorId = m_intsPool.Pull();
                                color = Util.ConvertColor(m_colors[colorId]);
                            }

                            settings[index] = new ExpressionDrawer.Settings(Util.ConvertColor(color), true, true);

                            tryDrawing = true;
                        }
                    }

                    // set new row
                    if (updateRequred)
                        ResetAt(new GeometryItem(geometry.Name, type, colorId, m_colors), index);
                }

                // draw variables
                if (tryDrawing)
                {
                    int width = (int)System.Math.Round(image.ActualWidth);
                    int height = (int)System.Math.Round(image.ActualHeight);
                    if (width > 0 && height > 0)
                    {
                        Bitmap bmp = new Bitmap(width, height);

                        Graphics graphics = Graphics.FromImage(bmp);
                        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        graphics.Clear(m_colors.ClearColor);

                        m_expressionDrawer.DrawGeometries(graphics, m_debugger, names, settings, m_colors, m_zoomBox);

                        image.Source = Util.BitmapToBitmapImage(bmp);
                        imageEmpty = false;
                    }
                }
            }
            
            if (imageEmpty)
            {
                image.Source = Util.BitmapToBitmapImage(m_emptyBitmap);
            }

            imageGrid.ContextMenu = new ContextMenu();
            MenuItem mi = new MenuItem();
            mi.Header = "Copy";
            mi.Click += MenuItem_Copy;
            if (imageEmpty)
                mi.IsEnabled = false;
            imageGrid.ContextMenu.Items.Add(mi);
            MenuItem mi2 = new MenuItem();
            mi2.Header = "Original View";
            mi2.Click += MenuItem_ResetZoom;
            imageGrid.ContextMenu.Items.Add(mi2);
        }

        private void MenuItem_Copy(object sender, RoutedEventArgs e)
        {
            if (image != null && image.Source != null)
            {
                Clipboard.SetImage((BitmapImage)image.Source);
            }
        }

        private void MenuItem_ResetZoom(object sender, RoutedEventArgs e)
        {
            bool update = m_zoomBox.IsZoomed();
            m_zoomBox.Reset();
            if (update)
                UpdateItems();
        }

        private void imageGrid_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                m_mouseDown = true;
                System.Windows.Point point = e.GetPosition(image);
                m_pointDown[0] = point.X;
                m_pointDown[1] = point.Y;
                imageGrid.CaptureMouse();
            }
        }

        private void imageGrid_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (m_mouseDown)
            {
                System.Windows.Point point = e.GetPosition(image);
                if (m_pointDown[0] != point.X || m_pointDown[1] != point.Y)
                {
                    double ox = m_pointDown[0];
                    double oy = m_pointDown[1];
                    double x = Math.Min(Math.Max(point.X, 0), image.ActualWidth);
                    double y = Math.Min(Math.Max(point.Y, 0), image.ActualHeight);
                    double w = Math.Abs(x - ox);
                    double h = Math.Abs(y - oy);
                    
                    double prop = h / w;
                    double iProp = image.ActualHeight / image.ActualWidth;
                    if (prop < iProp)
                        h = iProp * w;
                    else if (prop > iProp)
                        w = h / iProp;

                    double l = ox;
                    double t = oy;

                    if (ox <= x)
                    {
                        if (ox + w > image.ActualWidth)
                        {
                            w = image.ActualWidth - ox;
                            h = iProp * w;
                        }
                    }
                    else
                    {
                        if (ox - w < 0)
                        {
                            w = ox;
                            h = iProp * w;
                        }
                        l = ox - w;
                    }

                    if (oy <= y)
                    {
                        if (oy + h > image.ActualHeight)
                        {
                            h = image.ActualHeight - oy;
                            w = h / iProp;
                        }
                    }
                    else
                    {
                        if (oy - h < 0)
                        {
                            h = oy;
                            w = h / iProp;
                        }
                        t = oy - h;
                    }

                    if (w > 0 && h > 0)
                    {
                        Canvas.SetLeft(m_selectionRect, l);
                        Canvas.SetTop(m_selectionRect, t);
                        m_selectionRect.Width = w;
                        m_selectionRect.Height = h;

                        m_selectionRect.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private void imageGrid_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (m_mouseDown)
            {
                imageGrid.ReleaseMouseCapture();
                m_mouseDown = false;

                // Calculate zoom box only if the region changed
                if (m_selectionRect.Visibility == Visibility.Visible)
                {
                    m_selectionRect.Visibility = Visibility.Hidden;

                    double leftR = Canvas.GetLeft(m_selectionRect);
                    double topR = Canvas.GetTop(m_selectionRect);
                    double wR = m_selectionRect.Width;
                    double hR = m_selectionRect.Height;

                    if (wR > 0 && hR > 0)
                    {
                        m_zoomBox.Zoom(leftR, topR, wR, hR, image.ActualWidth, image.ActualHeight);
                        UpdateItems();
                    }
                }
            }
        }
    }
}