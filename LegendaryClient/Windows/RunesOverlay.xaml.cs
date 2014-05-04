﻿using LegendaryClient.Logic;
using LegendaryClient.Windows.Profile;
using System.Windows;
using System.Windows.Controls;

namespace LegendaryClient.Windows
{
    /// <summary>
    /// Interaction logic for RunesOverlay.xaml
    /// </summary>
    public partial class RunesOverlay : Page
    {
        public RunesOverlay()
        {
            InitializeComponent();
            Container.Content = new Runes().Content;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Client.OverlayContainer.Visibility = Visibility.Hidden;
        }
    }
}