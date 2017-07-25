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

        #endregion

        public MainViewModel()
        {
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
            double[,] samples = new double[2, nSamples];
            // Channel 1
            if (SealTest_Channel1 && VC_Channel1)
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
            //Notify all dependents that acquistion starts
            if (Start != null)
                Start.Invoke();
            //Start the DAQ board
            HardwareManager.DaqBoard.Start((s, i) => { return genSamples(s, i); });
        }

        void StopAcquisition()
        {
            //Notify all dependents that acquisition stops
            if (Stop != null)
                Stop.Invoke();
            //Stop the DAQ board
            HardwareManager.DaqBoard.Stop();
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
