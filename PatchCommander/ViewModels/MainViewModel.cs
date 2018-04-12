using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

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

    /// <summary>
    /// Represents a data chunk for one channel
    /// </summary>
    struct ChannelReadDataChunk
    {
        public long StartIndex;

        public double[,] Data;
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
        /// Indicates the desired holding voltage in mV for channel 1
        /// </summary>
        double _holdingVoltageCh1;

        /// <summary>
        /// Indicates whether current should be injected on channel 1
        /// </summary>
        bool _injectCh1;

        /// <summary>
        /// Indicates the desired current injection in pA for channel 1
        /// </summary>
        double _injectionCurrentCh1;

        /// <summary>
        /// Task to write data to file
        /// </summary>
        Task _recordTask;

        /// <summary>
        /// When recording, receives copy of the read data for asynchronous disk writing
        /// </summary>
        ProducerConsumer<ChannelReadDataChunk> _record_dataQueue;

        #endregion

        public MainViewModel()
        {
            BaseFNameCh1 = "Fish_01";
            if (IsInDesignMode)
                return;
            _record_dataQueue = new ProducerConsumer<ChannelReadDataChunk>(HardwareSettings.DAQ.Rate);
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
        /// Indicates the holding voltage of channel 1 in mV
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

        /// <summary>
        /// Indicates whether current should be injected on channel 1
        /// </summary>
        public bool InjectCh1
        {
            get
            {
                return _injectCh1;
            }
            set
            {
                _injectCh1 = value;
                RaisePropertyChanged(nameof(InjectCh1));
            }
        }

        /// <summary>
        /// The desired injection current in pA for channel 1
        /// </summary>
        public double InjectionCurrentCh1
        {
            get
            {
                return _injectionCurrentCh1;
            }
            set
            {
                _injectionCurrentCh1 = value;
                RaisePropertyChanged(nameof(InjectionCurrentCh1));
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
        /// <param name="start_sample">The index of the first sample</param>
        /// <param name="nSamples">The number of samples to generate</param>
        /// <returns>For each analog out the appropriate voltage samples</returns>
        private double[,] genSamples(long start_sample, int nSamples)
        {
            double[,] samples = new double[2, nSamples];
            // Channel 1
            double offset = 0;//= (VC_Channel1 && HoldingCh1) ? milliVoltsToAOVolts(HoldingVoltageCh1) : 0;
            if (VC_Channel1)
            {
                //Set offset to holding voltage if requested
                if (HoldingCh1)
                    offset = milliVoltsToAOVolts(HoldingVoltageCh1);
            }
            else
            {
                //Set offset to injection current if requested
                if (InjectCh1)
                    offset = picoAmpsToAOVolts(InjectionCurrentCh1);
            }
            if (SealTest_Channel1 && VC_Channel1)
            {
                var sts = sealTestSamples(start_sample, nSamples);
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
                var sts = sealTestSamples(start_sample, nSamples);
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
        /// <param name="start_sampe">The index of the first sample</param>
        /// <param name="nSamples">The number of samples to generate</param>
        /// <param name="freqHz">The frequency in Hz at which to generate sealTestSamples</param>
        /// <param name="ampMV">The amplitude in mV</param>
        /// <returns></returns>
        private double[] sealTestSamples(long start_sample, int nSamples, int freqHz=10, double ampMV=10)
        {
            if (_stSamples == null || _stSamples.Length != HardwareSettings.DAQ.Rate)
            {
                //Generate our sample buffer
                _stSamples = new double[HardwareSettings.DAQ.Rate];
                if(HardwareSettings.DAQ.Rate % (freqHz*2) != 0)
                {
                    System.Diagnostics.Debug.WriteLine("Warning seal test frequency does not result in even samples.");
                }
                int sam_per_seal = HardwareSettings.DAQ.Rate / freqHz;
                int sam_on = sam_per_seal / 2;
                for(int i = 0; i<_stSamples.Length; i++)
                {
                    if (i % sam_per_seal < sam_on)
                        _stSamples[i] = milliVoltsToAOVolts(ampMV);
                    else
                        _stSamples[i] = 0;
                }
            }
            double[] samOut = new double[nSamples];
            for(int i = 0;i < nSamples; i++)
            {
                Array.Copy(_stSamples, start_sample % HardwareSettings.DAQ.Rate, samOut, 0, samOut.Length); 
            }
            return samOut;
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
            //Attach ourselves to the sample read event queue
            if(!IsRecordingCh1)
                HardwareManager.DaqBoard.ReadDone += RecordSamples;
            if (channelIndex == 1)
            {
                _recordTask = new Task(() =>
               {
                   BinaryWriter ch1File = new BinaryWriter(File.OpenWrite(CreateFilename(1) + ".data"));
                   while (true)
                   {
                       ChannelReadDataChunk chnk = _record_dataQueue.Consume();
                       if (chnk.StartIndex == 0 && chnk.Data == null)
                           break;
                       for (int i = 0; i < chnk.Data.GetLength(1); i++)
                           WriteChannel1Sample(ch1File, chnk.StartIndex + i, chnk.Data[2, i] > 2, (float)chnk.Data[4, i], (float)chnk.Data[0, i],
                               (float)chnk.Data[6, i]);
                   }
                   ch1File.Dispose();
               });
                _recordTask.Start();
                IsRecordingCh1 = true;
            }
            else
                throw new NotImplementedException();
        }

        void StopRecording(int channelIndex)
        {
            //Detach ourselves from the sample read event queue
            if (IsRecordingCh1)
            {
                HardwareManager.DaqBoard.ReadDone -= RecordSamples;
                //add end of recording to signal to our queue
                ChannelReadDataChunk end_chunk = new ChannelReadDataChunk();
                end_chunk.StartIndex = 0;
                end_chunk.Data = null;
                _record_dataQueue.Produce(end_chunk);
                //wait for our file writing to finish
                if (_recordTask != null && !_recordTask.IsCompleted)
                    if (!_recordTask.Wait(1000))
                        System.Diagnostics.Debug.WriteLine("Timed out waiting on record task to end");
                _recordTask = null;
            }
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
                string folder = string.Format("F:\\PatchCommander_Data\\{0}_{1}_{2}", now.Year, now.Month, now.Day);
                Directory.CreateDirectory(folder);
                return string.Format("{0}\\Ch1_{1}_{2}_{3}_{4}_{5}", folder, BaseFNameCh1, now.Year, now.Month, now.Day, now.Ticks);
            }
            else
                throw new NotImplementedException("Channel 2 not currently implemented");
        }

        void WriteChannel1Sample(BinaryWriter file, long index, bool mode, float command, float read, float laser)
        {
            //To read in python, use numpy.fromfile with the following data-type definition
            //dt = numpy.dtype([('index',np.int64),('mode',np.bool),('command',np.float32),('read',np.float32),('laser',np.float32)])
            if (file==null)
                return;
            try
            {
                file.Write(index);
                file.Write(mode);
                file.Write(command);
                file.Write(read);
                file.Write(laser);
            }
            catch (IOException)
            {
                System.Diagnostics.Debug.WriteLine("Error writing to data file. File may be corrupted.");
            }
        }

        #endregion

        #region EventHandlers

        void RecordSamples(ReadDoneEventArgs args)
        {
            ChannelReadDataChunk chunk = new ChannelReadDataChunk();
            chunk.StartIndex = args.StartIndex;
            chunk.Data = args.Data.Clone() as double[,];
            _record_dataQueue.Produce(chunk);
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
            if (IsRecordingCh1)
                StopRecording(1);
            if (IsAcquiring)
                StopAcquisition();
            if (HardwareManager.DaqBoard.IsRunning)
                StopAcquisition();
        }
    }
}
