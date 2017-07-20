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
    /// <summary>
    /// Represents an indexed sample
    /// </summary>
    struct IndexedSample
    {
        public double Sample;

        public long Index;

        public IndexedSample(double sample, long index)
        {
            Sample = sample;
            Index = index;
        }
    }

    class MainViewModel : ViewModelBase
    {
        #region Members

        /// <summary>
        /// Collection of values to plot on the live plot of channel 1
        /// </summary>
        ChartCollection<double> _plotData_live1;

        /// <summary>
        /// Producer consumer for live plotting on channel 1
        /// </summary>
        ProducerConsumer<double> _ch1LiveDump;

        /// <summary>
        /// Collection of values to plot on the seal test plot of channel 1
        /// </summary>
        ChartCollection<double, double> _plotData_seal1;

        /// <summary>
        /// Producer consumer for time-aligned seal test
        /// on channel 1
        /// </summary>
        ProducerConsumer<IndexedSample> _ch1SealTestDump;

        /// <summary>
        /// UI update timer for all live plots
        /// </summary>
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

        /// <summary>
        /// X-Axis range on the seal test plot
        /// </summary>
        private Range<double> _ch1_sealXRange = new Range<double>(0, 100);

        #endregion

        public MainViewModel()
        {
            if (IsInDesignMode)
                return;
            _plotData_live1 = new ChartCollection<double>(2000);
            _plotData_seal1 = new ChartCollection<double, double>();
            //Create buffer with 1s worth of storage
            _ch1LiveDump = new ProducerConsumer<double>(HardwareSettings.DAQ.Rate);
            _ch1SealTestDump = new ProducerConsumer<IndexedSample>(HardwareSettings.DAQ.Rate);
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
            UpdateCh1LivePlot();
            if (Ch1_SealTest)
                UpdateCh1SealTest();
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

        public ChartCollection<double, double> PlotData_seal1
        {
            get
            {
                return _plotData_seal1;
            }
            set
            {
                _plotData_seal1 = value;
                RaisePropertyChanged(nameof(PlotData_seal1));
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
                    return new Range<double>(-2000, 2000);
                else
                    return new Range<double>(-100, 100);
            }
        }

        public Range<double> Ch1_SealXRange
        {
            get
            {
                return _ch1_sealXRange;
            }
            set
            {
                _ch1_sealXRange = value;
                RaisePropertyChanged(nameof(Ch1_SealXRange));
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

        private double DAQV_to_pA(double daqVolts)
        {
            return daqVolts * 2000; //0.5V per nA - voltage clamp
        }

        private double DAQV_to_mV(double daqVolts)
        {
            return daqVolts * 100.0; //10mV per mv - current clamp
        }

        /// <summary>
        /// Gets called by the UI timer to update the display of
        /// the live plot of channel 1
        /// </summary>
        private void UpdateCh1LivePlot()
        {
            int bin_factor = HardwareSettings.DAQ.Rate / 2000;
            int c = _ch1LiveDump.Count;
            if (c < bin_factor)
                return;
            if (_chartRunningSamples == null || _chartRunningSamples.Length != bin_factor)
                _chartRunningSamples = new double[bin_factor];
            //we take only a number of samples that evenly fits
            //into our binning
            int to_take = c - (c % bin_factor);
            //create the appropriate chart values as the absolute maximum within the bin
            double[] values = new double[to_take / bin_factor];
            for (int i = 0; i < to_take; i++)
            {
                _chartRunningSamples[_chartRunningCount % bin_factor] = _ch1LiveDump.Consume();
                _chartRunningCount++;
                if (_chartRunningCount % bin_factor == 0)
                {
                    int maxIndex = -1;
                    double absMax = -1;
                    for (int j = 0; j < bin_factor; j++)
                    {
                        if (Math.Abs(_chartRunningSamples[j]) > absMax)
                        {
                            absMax = Math.Abs(_chartRunningSamples[j]);
                            maxIndex = j;
                        }
                    }
                    //add our binned value after conversion into mV (current clamp) or pA (voltage clamp)
                    var daqvolts = _chartRunningSamples[maxIndex];
                    if (!VC_Channel1)
                        daqvolts = DAQV_to_mV(daqvolts);
                    else
                        daqvolts = DAQV_to_pA(daqvolts);
                    values[i / bin_factor] = daqvolts;
                }
            }
            PlotData_live1.Append(values);
        }

        /// <summary>
        /// Array to accumulate seal test samples
        /// for alignment and averaging
        /// </summary>
        double[] _sealTestAccum;

        /// <summary>
        /// Array containing the times of all seal
        /// test samples
        /// </summary>
        double[] _sealTestTime;

        private void UpdateCh1SealTest()
        {
            //Some constants that should be settable later
            int sealTestFreq = 10;
            int sealTestSamples = HardwareSettings.DAQ.Rate / sealTestFreq;
            int sealTestOnSamples = sealTestSamples / 2;
            int nAccum = sealTestFreq / 2;
            // We want to align plotting of the seal test, such that the
            // TODO: current spikes at start and end of step are in the middle of the plot
            // i.e. such that the middle of the OnSamples is in the middle of the plot
            int zeroPoint = sealTestSamples - sealTestOnSamples / 2;
            if (_sealTestAccum == null || _sealTestAccum.Length != sealTestSamples)
            {
                _sealTestAccum = new double[sealTestSamples];
                _sealTestTime = new double[sealTestSamples];
                for (int i = 0; i < sealTestSamples; i++)
                    _sealTestTime[i] = (1000.0 / HardwareSettings.DAQ.Rate) * i;
                PlotData_seal1.Capacity = sealTestSamples;
                Ch1_SealXRange = new Range<double>(0, 1000.0 / sealTestFreq);
            }
            // consume samples
            int c = _ch1SealTestDump.Count;
            if (c < sealTestSamples)
                return;
            for (int i = 0; i < c; i++)
            {
                var sample = _ch1SealTestDump.Consume();
                //check if this sample belongs to a new accumulation window - if yes, plot and reset accumulator
                long window_index = (sample.Index + zeroPoint) % (sealTestSamples * nAccum);
                if(window_index == 0)
                {
                    //plot
                    PlotData_seal1.Clear();
                    PlotData_seal1.Append(_sealTestTime, _sealTestAccum);
                    //reset accumulator
                    for (int j = 0; j < sealTestSamples; j++)
                        _sealTestAccum[j] = 0;
                }
                //add new samples to array
                int array_index = (int)((sample.Index % sealTestSamples - zeroPoint + sealTestSamples) % sealTestSamples);
                _sealTestAccum[array_index] += DAQV_to_pA(sample.Sample / nAccum);
            }
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
        void SampleAcquired(ReadDoneEventArgs args)
        {
            for (int i = 0; i < args.Data.GetLength(1); i++)
            {
                _ch1LiveDump.Produce(args.Data[0, i]);
                if (Ch1_SealTest)
                    _ch1SealTestDump.Produce(new IndexedSample(args.Data[0, i], args.StartIndex + i));
            }
        }

        #endregion
    }
}
