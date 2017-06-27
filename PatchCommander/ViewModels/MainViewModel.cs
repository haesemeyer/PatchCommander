using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MHApi.GUI;
using MHApi.Threading;
using System.Threading;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using MHApi.Utilities;
using NationalInstruments.Controls;
using PatchCommander.Hardware;

namespace WpfTestBed.ViewModels
{

    class MainViewModel : ViewModelBase
    {
        #region Members

        ChartCollection<double> _plotData_live1;

        ProducerConsumer<double> _dataDump;

        DispatcherTimer _displayUpdater;

        #endregion

        public MainViewModel()
        {
            if (IsInDesignMode)
                return;
            _plotData_live1 = new ChartCollection<double>(2000);
            _dataDump = new ProducerConsumer<double>(1000);
            _displayUpdater = new DispatcherTimer();
            _displayUpdater.Tick += TimerEvent;
            _displayUpdater.Interval = new TimeSpan(0, 0, 0, 0, 5);
            _displayUpdater.Start();
        }

        /// <summary>
        /// Our display update timer event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TimerEvent(object sender, EventArgs e)
        {
            if (_dataDump.Count < 10)
                return;
            int c = _dataDump.Count;
            double[] values = new double[c];
            for (int i = 0; i < c; i++)
            {
                values[i] = _dataDump.Consume();
            }
            PlotData_live1.Append(values);
        }

        #region Properties

        public ChartCollection<double> PlotData_live1
        {
            get
            {
                return _plotData_live1;
            }
            set
            {
                _plotData_live1 = value;
                RaisePropertyChanged(nameof(PlotData_live1));
            }
        }

        #endregion

        #region Methods

        public void StartStop()
        {
            if (HardwareManager.DaqBoard.IsRunning)
                StopAcquisition();
            else
                StartAcquisition();
        }

        void StartAcquisition()
        {
            //Subscribe to sample acquisition event
            HardwareManager.DaqBoard.ReadDone += SampleAcquired;
            HardwareManager.DaqBoard.Start();
        }

        void StopAcquisition()
        {
            HardwareManager.DaqBoard.Stop();
            //Unsubscribe from sample acquisition event
            HardwareManager.DaqBoard.ReadDone -= SampleAcquired;
        }

        /// <summary>
        /// Event handler whenever a bunch of new samples is acquired
        /// </summary>
        /// <param name="samples">The samples received</param>
        void SampleAcquired(double[,] samples)
        {
            for (int i = 0; i < samples.GetLength(1); i++)
                _dataDump.Produce(samples[0, i]);
        }

        #endregion
    }
}
