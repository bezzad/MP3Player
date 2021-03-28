﻿using System.Windows;

namespace MP3Player.Sample
{
    public static class VisibilityHelper
    {
        public static readonly DependencyProperty IsVisibleProperty
            = DependencyProperty.RegisterAttached(
                "IsVisible",
                typeof(bool?),
                typeof(VisibilityHelper),
                new FrameworkPropertyMetadata(default(bool?),
                    FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    IsVisibleChangedCallback));

        private static void IsVisibleChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null)
                return;

            fe.Visibility = ((bool?)e.NewValue) == true
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        public static void SetIsVisible(DependencyObject element, bool? value)
        {
            element.SetValue(IsVisibleProperty, value);
        }

        public static bool? GetIsVisible(DependencyObject element)
        {
            return (bool?)element.GetValue(IsVisibleProperty);
        }

        public static readonly DependencyProperty IsCollapsedProperty
            = DependencyProperty.RegisterAttached(
                "IsCollapsed",
                typeof(bool?),
                typeof(VisibilityHelper),
                new FrameworkPropertyMetadata(default(bool?),
                    FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    IsCollapsedChangedCallback));

        private static void IsCollapsedChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null)
                return;

            fe.Visibility = ((bool?)e.NewValue) == true
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        public static void SetIsCollapsed(DependencyObject element, bool? value)
        {
            element.SetValue(IsCollapsedProperty, value);
        }

        public static bool? GetIsCollapsed(DependencyObject element)
        {
            return (bool?)element.GetValue(IsCollapsedProperty);
        }

        public static readonly DependencyProperty IsHiddenProperty
            = DependencyProperty.RegisterAttached(
                "IsHidden",
                typeof(bool?),
                typeof(VisibilityHelper),
                new FrameworkPropertyMetadata(default(bool?),
                    FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    IsHiddenChangedCallback));

        private static void IsHiddenChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var fe = d as FrameworkElement;
            if (fe == null)
                return;

            fe.Visibility = ((bool?)e.NewValue) == true
                ? Visibility.Hidden
                : Visibility.Visible;
        }

        public static void SetIsHidden(DependencyObject element, bool? value)
        {
            element.SetValue(IsHiddenProperty, value);
        }

        public static bool? GetIsHidden(DependencyObject element)
        {
            return (bool?)element.GetValue(IsHiddenProperty);
        }
    }
}
