using System.Windows;
using System.Windows.Controls;
using PatchCommander.ViewModels;

namespace PatchCommander.Views
{
    /// <summary>
    /// Interaction logic for MainView.xaml
    /// </summary>
    public partial class MainView : UserControl
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


    }
}
