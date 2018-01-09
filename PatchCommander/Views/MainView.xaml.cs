﻿using System.Windows;
using System.Windows.Controls;
using PatchCommander.ViewModels;
using MHApi.GUI;
using System.ComponentModel;

namespace PatchCommander.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : WindowAwareView
    {
        private MainViewModel _viewModel;

        public MainView()
        {
            InitializeComponent();
            _viewModel = ViewModel.Source as MainViewModel;
        }

        private void btnStartStop_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.StartStop();
        }

        private void btnCh1Record_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.StartStopRecCh1();
        }

        protected override void WindowClosing(object sender, CancelEventArgs e)
        {
            //Clean up when the window closes
            _viewModel.Dispose();
            base.WindowClosing(sender, e);
        }


    }
}
