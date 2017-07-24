using System.ComponentModel;

using MHApi.GUI;
using PatchCommander.ViewModels;

namespace PatchCommander.Views
{
    /// <summary>
    /// Interaction logic for ChannelView.xaml
    /// </summary>
    public partial class ChannelView : WindowAwareView
    {
        private ChannelViewModel _viewModel;

        public ChannelView()
        {
            InitializeComponent();
            _viewModel = ViewModel.Source as ChannelViewModel;
        }

        protected override void WindowClosing(object sender, CancelEventArgs e)
        {
            //Clean up when the window closes
            _viewModel.Dispose();
            base.WindowClosing(sender, e);
        }
    }

}
