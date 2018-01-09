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

namespace PatchCommander.ViewModels
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
        /// Cache of seal test samples
        /// </summary>
        double[] _stSamples;

        /// <summary>
        /// True if channel 1 is in voltage clamp
        /// </summary>
        bool _vc_ch1;

        /// <summary>
        /// True if channel 2 is in voltage clamp
        /// </summary>
        bool _vc_ch2;

        /// <summary>
        /// Indicates whether seal test should
        /// be run on channel 1
        /// </summary>
        bool _sealTest_ch1;

        /// <summary>
        /// Indicates whether seal test
        /// should be run on channel 2
        /// </summary>
        bool _sealTest_ch2;

        /// <summary>
        /// Indicates whether data is currently acquired from the DAQ board
        /// </summary>
        bool _isAcquiring;

        /// <summary>
        /// Indicates wheter data is currently written to file for Ch1
        /// </summary>
        bool _isRecordingCh1;

        /// <summary>
        /// The base filename for channel 1
        /// </summary>
        string _baseFNameCh1;

        /// <summary>
        /// Indicates whether holding voltage is requested for channel 1
        /// </summary>
        bool _holdingCh1;

        /// <summary>
        /// Indicates the desired holding voltage for channel 1
        /// </summary>
        double _holdingVoltageCh1;

        #endregion

        public MainViewModel()
        {
            BaseFNameCh1 = "Fish_01";
            if (IsInDesignMode)
                return;
            //Subscribe to channel view events
            ChannelViewModel.ClampModeChanged += ClampModeChanged;
            ChannelViewModel.SealTestChanged += SealTestChanged;
        }

        #region Properties

        /// <summary>
        /// Indicates if channel1 is in voltage clamp
        /// </summary>
        public bool VC_Channel1
        {
            get
            {
                return _vc_ch1;
            }
            private set
            {
                _vc_ch1 = value;
                RaisePropertyChanged(nameof(VC_Channel1));
            }
        }

        /// <summary>
        /// Indicates if channel2 is in voltage clamp
        /// </summary>
        public bool VC_Channel2
        {
            get
            {
                return _vc_ch2;
            }
            private set
            {
                _vc_ch2 = value;
                RaisePropertyChanged(nameof(VC_Channel2));
            }
        }

        /// <summary>
        /// Indicates if channel1 should produce seal test
        /// </summary>
        public bool SealTest_Channel1
        {
            get
            {
                return _sealTest_ch1;
            }
            private set
            {
                _sealTest_ch1 = value;
                RaisePropertyChanged(nameof(SealTest_Channel1));
            }
        }

        /// <summary>
        /// Indicates if channel2 should produce seal test
        /// </summary>
        public bool SealTest_Channel2
        {
            get
            {
                return _sealTest_ch2;
            }
            private set
            {
                _sealTest_ch2 = value;
                RaisePropertyChanged(nameof(SealTest_Channel2));
            }
        }

        /// <summary>
        /// Indicates whether data is currently being acquired from the daq board
        /// </summary>
        public bool IsAcquiring
        {
            get
            {
                return _isAcquiring;
            }
            set
            {
                _isAcquiring = value;
                RaisePropertyChanged(nameof(IsAcquiring));
            }
        }

        /// <summary>
        /// Indicates whether data is currently written to file for Ch1
        /// </summary>
        public bool IsRecordingCh1
        {
            get
            {
                return _isRecordingCh1;
            }
            set
            {
                _isRecordingCh1 = value;
                RaisePropertyChanged(nameof(IsRecordingCh1));
            }
        }

        /// <summary>
        /// The base filename for channel 1
        /// </summary>
        public string BaseFNameCh1
        {
            get
            {
                return _baseFNameCh1;
            }
            set
            {
                _baseFNameCh1 = value;
                RaisePropertyChanged(nameof(BaseFNameCh1));
            }
        }

        /// <summary>
        /// Indicates whether holding voltage should be applied to channel 1
        /// </summary>
        public bool HoldingCh1
        {
            get
            {
                return _holdingCh1;
            }
            set
            {
                _holdingCh1 = value;
                RaisePropertyChanged(nameof(HoldingCh1));
            }
        }

        /// <summary>
        /// Indicates the holding voltage of channel 1
        /// </summary>
        public double HoldingVoltageCh1
        {
            get
            {
                return _holdingVoltageCh1;
            }
            set
            {
                _holdingVoltageCh1 = value;
                RaisePropertyChanged(nameof(HoldingVoltageCh1));
            }
        }

        #endregion

        #region Methods

        #region Button Handlers
        public void StartStop()
        {
            if (HardwareManager.DaqBoard.IsRunning)
                StopAcquisition();
            else
                StartAcquisition();
        }

        public void StartStopRecCh1()
        {
            if (IsRecordingCh1)
                StopRecording(1);
            else
                StartRecording(1);
        }
        #endregion Button Handlers

        /// <summary>
        /// Generates necessary analog out samples
        /// </summary>
        /// <param name="second">The current second since start</param>
        /// <param name="nSamples">The number of samples to generate</param>
        /// <returns>For each analog out the appropriate voltage samples</returns>
        private double[,] genSamples(double second, int nSamples)
        {
            double[,] samples = new double[2, nSamples];
            // Channel 1
            double offset = (VC_Channel1 && HoldingCh1) ? milliVoltsToAOVolts(HoldingVoltageCh1) : 0;
            if (SealTest_Channel1 && VC_Channel1)
            {
                var sts = sealTestSamples(second, nSamples);
                for (int i = 0; i < nSamples; i++)
                    samples[0, i] = sts[i] + offset;
            }
            else
            {
                for (int i = 0; i < nSamples; i++)
                {
                    samples[0, i] = offset;
                }
            }
            // Channel 2
            if (SealTest_Channel2 && VC_Channel2)
            {
                var sts = sealTestSamples(second, nSamples);
                for (int i = 0; i < nSamples; i++)
                    samples[1, i] = sts[i];
            }
            else
            {
                for (int i = 0; i < nSamples; i++)
                {
                    samples[1, i] = 0;
                }
            }
            return samples;
        }

        /// <summary>
        /// Function to convert desired millivoltages in voltage clamp
        /// to corresponding analog out values to the amplifier
        /// </summary>
        /// <param name="mv">The desired milli-volts</param>
        /// <returns>The analog out voltage to apply</returns>
        private double milliVoltsToAOVolts(double mv)
        {
            return mv / 20;
        }

        /// <summary>
        /// Function to convert desired pico-amps injection in current clamp
        /// to corresponding analog out values to the amplifier
        /// </summary>
        /// <param name="pa">The desired pico-amps</param>
        /// <returns>The analog out voltage to apply</returns>
        private double picoAmpsToAOVolts(double pa)
        {
            return pa / 400;
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
                        _stSamples[i] = milliVoltsToAOVolts(ampMV);
                    else
                        _stSamples[i] = 0;
                }
            }
            return _stSamples;
        }

        void StartAcquisition()
        {
            //Notify all dependents that acquistion starts
            if (Start != null)
                Start.Invoke();
            //Start the DAQ board
            HardwareManager.DaqBoard.Start((s, i) => { return genSamples(s, i); });
            IsAcquiring = true;
        }

        void StopAcquisition()
        {
            //Notify all dependents that acquisition stops
            if (Stop != null)
                Stop.Invoke();
            if (IsRecordingCh1)
                StopRecording(1);
            //Stop the DAQ board
            HardwareManager.DaqBoard.Stop();
            IsAcquiring = false;
        }

        void StartRecording(int channelIndex)
        {
            if(channelIndex == 1)
            {
                IsRecordingCh1 = true;
            }
        }

        void StopRecording(int channelIndex)
        {
            if (channelIndex == 1)
            {
                IsRecordingCh1 = false;
            }
        }

        /// <summary>
        /// Gets called whenver the clamp mode changes on a channel view
        /// </summary>
        /// <param name="args"></param>
        void ClampModeChanged(ClampModeChangedArgs args)
        {
            if (args.ChannelIndex == 0)
                VC_Channel1 = (args.Mode == DAQ.ClampMode.VoltageClamp);
            else if (args.ChannelIndex == 1)
                VC_Channel2 = (args.Mode == DAQ.ClampMode.VoltageClamp);
        }

        /// <summary>
        /// Gets called whenver the seal test mode changed on a channel view
        /// </summary>
        /// <param name="args"></param>
        void SealTestChanged(SealTestChangedArgs args)
        {
            if (args.ChannelIndex == 0)
                SealTest_Channel1 = args.SealTest;
            else if (args.ChannelIndex == 1)
                SealTest_Channel2 = args.SealTest;
        }

        /// <summary>
        /// Creates a unique recording filename
        /// </summary>
        /// <param name="channelIndex">The index of the channel for which the filename should be created</param>
        /// <returns>The filename string without extension</returns>
        string CreateFilename(int channelIndex)
        {
            if (channelIndex == 1)
            {
                DateTime now = DateTime.Now;
                return string.Format("{0}_{1}_{2}_{3}_{4}", BaseFNameCh1, now.Year, now.Month, now.Day, now.Ticks);
            }
            else
                throw new NotImplementedException("Channel 2 not currently implemented");
        }

        #endregion

        #region Events

        /// <summary>
        /// Event to subscribe to to get notified
        /// about acquisition starts
        /// </summary>
        public static event Action Start;

        /// <summary>
        /// Event to subscribe to get notified
        /// about acquisition stops
        /// </summary>
        public static event Action Stop;

        #endregion

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (HardwareManager.DaqBoard.IsRunning)
                StopAcquisition();
        }
    }
}
