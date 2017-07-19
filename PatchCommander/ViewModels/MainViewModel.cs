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

        /// <summary>
        /// Running samples for binning down
        /// from sampling rate to 2kHz display rate
        /// </summary>
        double[] _chartRunningSamples;

        /// <summary>
        /// Running chart sample index to allow binning
        /// </summary>
        long _chartRunningCount;

        /// <summary>
        /// True when performing seal test on channel 1
        /// </summary>
        bool _ch1SealTest;

        /// <summary>
        /// Array to buffer our standard seal-test samples
        /// </summary>
        private double[] _stSamples;

        #endregion

        public MainViewModel()
        {
            if (IsInDesignMode)
                return;
            _plotData_live1 = new ChartCollection<double>(2000);
            //Create buffer with 1s worth of storage
            _dataDump = new ProducerConsumer<double>(HardwareSettings.DAQ.Rate);
            _displayUpdater = new DispatcherTimer();
            _displayUpdater.Tick += TimerEvent;
            _displayUpdater.Interval = new TimeSpan(0, 0, 0, 0, 5);
            _displayUpdater.Start();
            RaisePropertyChanged(nameof(VC_Channel1));
        }

        /// <summary>
        /// Our display update timer event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TimerEvent(object sender, EventArgs e)
        {
            int bin_factor = HardwareSettings.DAQ.Rate / 2000;
            if (_dataDump.Count < bin_factor)
                return;
            if (_chartRunningSamples == null || _chartRunningSamples.Length != bin_factor)
                _chartRunningSamples = new double[bin_factor];
            int c = _dataDump.Count;
            //we take only a number of samples that evenly fits
            //into our binning
            int to_take = c - (c % bin_factor);
            //create the appropriate binned-down chart values
            double[] values = new double[to_take / bin_factor];
            for (int i = 0; i < to_take; i++)
            {
                _chartRunningSamples[_chartRunningCount % bin_factor] = _dataDump.Consume();
                _chartRunningCount++;
                if (_chartRunningCount % bin_factor == 0)
                {
                    int maxIndex = -1;
                    double absMax = -1;
                    for(int j = 0; j < bin_factor; j++)
                    {
                        if(Math.Abs(_chartRunningSamples[j]) > absMax)
                        {
                            absMax = Math.Abs(_chartRunningSamples[j]);
                            maxIndex = j;
                        }
                    }
                    //add our binned value after conversion into mV (current clamp) or pA (voltage clamp)
                    var daqvolts = _chartRunningSamples[maxIndex];
                    if (!VC_Channel1)
                        daqvolts *= 100.0; //10mV per mv - current clamp
                    else
                        daqvolts *= 2000; //0.5V per nA - voltage clamp
                    values[i / bin_factor] = daqvolts;
                }
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

        /// <summary>
        /// Indicates and sets whether channel1 is in voltage or current clamp mode
        /// </summary>
        public bool VC_Channel1
        {
            get
            {
                return HardwareManager.DaqBoard.Channel1Mode == DAQ.ClampMode.VoltageClamp;
            }
            set
            {
                if (value)
                    HardwareManager.DaqBoard.Channel1Mode = DAQ.ClampMode.VoltageClamp;
                else
                    HardwareManager.DaqBoard.Channel1Mode = DAQ.ClampMode.CurrentClamp;
                RaisePropertyChanged(nameof(VC_Channel1));
                RaisePropertyChanged(nameof(Ch1_UnitLabel));
                RaisePropertyChanged(nameof(Ch1_UnitRange));
            }
        }

        /// <summary>
        /// Depending on current mode sets the y-axis unit label of channel1 charts
        /// </summary>
        public string Ch1_UnitLabel
        {
            get
            {
                if (VC_Channel1)
                    return "Current [pA]";
                else
                    return "Voltage [mV]";
            }
        }

        /// <summary>
        /// Depending on current mode sets the y-axis range of channel1 charts
        /// </summary>
        public Range<double> Ch1_UnitRange
        {
            get
            {
                if (VC_Channel1)
                    return new Range<double>(-200, 200);
                else
                    return new Range<double>(-1000, 1000);
            }
        }

        /// <summary>
        /// If true, seal-test values will be generated on channel 1
        /// when in voltage clamp - ignored in current clamp
        /// </summary>
        public bool Ch1_SealTest
        {
            get
            {
                return _ch1SealTest;
            }
            set
            {
                _ch1SealTest = value;
                RaisePropertyChanged(nameof(Ch1_SealTest));
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

        /// <summary>
        /// Generates necessary analog out samples
        /// </summary>
        /// <param name="second">The current second since start</param>
        /// <param name="nSamples">The number of samples to generate</param>
        /// <returns>For each analog out the appropriate voltage samples</returns>
        private double[,] genSamples(double second, int nSamples)
        {
            double[,] samples = new double[1, nSamples];
            if (_ch1SealTest && VC_Channel1)
            {
                var sts = sealTestSamples(second, nSamples);
                for (int i = 0; i < nSamples; i++)
                    samples[0, i] = sts[i];
            }
            else
            {
                for (int i = 0; i < nSamples; i++)
                {
                    samples[0, i] = 0;
                }
            }
            return samples;
        }

        /// <summary>
        /// For one electrode generates our seal test samples for one whole second
        /// </summary>
        /// <param name="second">The starting second</param>
        /// <param name="nSamples">The number of samples to generate equaling one second</param>
        /// <param name="freqHz">The frequency in Hz at which to generate sealTestSamples</param>
        /// <param name="ampMV">The amplitude in mV</param>
        /// <returns></returns>
        private double[] sealTestSamples(double second, int nSamples, int freqHz=10, double ampMV=10)
        {
            if (_stSamples == null || _stSamples.Length != nSamples)
            {
                //Generate our sample buffer
                _stSamples = new double[nSamples];
                if(nSamples % (freqHz*2) != 0)
                {
                    System.Diagnostics.Debug.WriteLine("Warning seal test frequency does not result in even samples.");
                }
                int sam_per_seal = nSamples / freqHz;
                int sam_on = sam_per_seal / 2;
                for(int i = 0; i<nSamples; i++)
                {
                    if (i % sam_per_seal < sam_on)
                        _stSamples[i] = ampMV / 20;
                    else
                        _stSamples[i] = 0;
                }
            }
            return _stSamples;
        }

        void StartAcquisition()
        {
            //Subscribe to sample acquisition event
            HardwareManager.DaqBoard.ReadDone += SampleAcquired;
            HardwareManager.DaqBoard.Start((s,i) => { return genSamples(s, i); }) ;
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
