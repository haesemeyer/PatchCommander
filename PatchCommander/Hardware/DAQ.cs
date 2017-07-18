using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NationalInstruments.DAQmx;
using MHApi.Utilities;
using MHApi.Threading;
using System.Threading;

namespace PatchCommander.Hardware
{
    /// <summary>
    /// Class to represent acquisition and control via NI DAQ
    /// </summary>
    class DAQ: PropertyChangeNotification, IDisposable
    {
        public enum ClampMode { CurrentClamp=0, VoltageClamp=1};

        #region Members

        /// <summary>
        /// The clamp modes (Current vs. Voltage) of each channel
        /// </summary>
        ClampMode[] _channelModes;

        /// <summary>
        /// The read thread reading from analog in channels
        /// </summary>
        WorkerT<object> _readThread;

        /// <summary>
        /// The write thread for writing analog out samples
        /// </summary>
        WorkerT<Func<double, int, double[,]>> _writeThread;

        /// <summary>
        /// Indicates whether we are currently acquiring/generating data
        /// </summary>
        bool _isRunning;

        /// <summary>
        /// Indicates to the read thread that the write thread is ready
        /// </summary>
        AutoResetEvent _writeThreadReady = new AutoResetEvent(false);

        #endregion

        /// <summary>
        /// Constructs a new DAQ object
        /// </summary>
        public DAQ()
        {
            //At startup, set both main channels to VoltageClamp
            _channelModes = new ClampMode[2];
            // Channel1Mode = ClampMode.VoltageClamp;
            // Channel2Mode = ClampMode.VoltageClamp;
        }

        #region Properties

        /// <summary>
        /// The recording mode of channel1
        /// </summary>
        ClampMode Channel1Mode
        {
            get
            {
                return _channelModes[0];
            }
            set
            {
                throw new NotImplementedException();
                RaisePropertyChanged(nameof(Channel1Mode));
            }
        }

        /// <summary>
        /// The recording mode of channel2
        /// </summary>
        ClampMode Channel2Mode
        {
            get
            {
                return _channelModes[1];
            }
            set
            {
                throw new NotImplementedException();
                RaisePropertyChanged(nameof(Channel2Mode));
            }
        }

        /// <summary>
        /// Indicates whether we are currently acquiring/generating data
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return _isRunning;
            }
            private set
            {
                _isRunning = value;
                RaisePropertyChanged(nameof(IsRunning));
            }
        }

        #endregion

        #region Events

        public delegate void ReadDoneEvent(double[,] data);

        public event ReadDoneEvent ReadDone;

        #endregion

        #region Methods

        void ReadThreadRun(AutoResetEvent stop, object data)
        {
            Task readTask = new Task("EphysRead");
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch1Read, "Electrode1", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch2Read, "Electrode2", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.Timing.ConfigureSampleClock("", HardwareSettings.DAQ.Rate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            _writeThreadReady.WaitOne();
            try
            {
                readTask.Start();
                AnalogMultiChannelReader dataReader = new AnalogMultiChannelReader(readTask.Stream);
                while (!stop.WaitOne(0))
                {
                    double[,] read = dataReader.ReadMultiSample(10);
                    ReadDone.Invoke(read);
                }
            }
            finally
            {
                readTask.Stop();
                readTask.Dispose();
            }
        }

        void WriteThreadRun(AutoResetEvent stop, Func<double, int, double[,]> sampleFunction)
        {
            Task writeTask = new Task("EphysWrite");
            double[,] firstSamples = sampleFunction(0, HardwareSettings.DAQ.Rate);
            if (firstSamples.GetLength(1) != HardwareSettings.DAQ.Rate)
                throw new ApplicationException("Did not receive the required number of samples");
            var nChannels = firstSamples.GetLength(0);
            for (int i = 0; i < nChannels; i++)
                writeTask.AOChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + string.Format("AO{0}", i), "", -10, 10, AOVoltageUnits.Volts);
            writeTask.Timing.ConfigureSampleClock("ai/SampleClock", HardwareSettings.DAQ.Rate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            writeTask.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger("ai/StartTrigger ", DigitalEdgeStartTriggerEdge.Rising);
            writeTask.Stream.WriteRegenerationMode = WriteRegenerationMode.DoNotAllowRegeneration;
            AnalogMultiChannelWriter dataWriter = new AnalogMultiChannelWriter(writeTask.Stream);
            dataWriter.WriteMultiSample(false, firstSamples);
            writeTask.Start();
            _writeThreadReady.Set();
            double second = 0;
            try
            {
                while (!stop.WaitOne(800))
                {
                    double[,] samples = sampleFunction(++second, HardwareSettings.DAQ.Rate);
                    System.Diagnostics.Debug.Assert(samples.GetLength(1) == HardwareSettings.DAQ.Rate);
                    dataWriter.WriteMultiSample(false, samples);
                }
                System.Diagnostics.Debug.WriteLine("Left write loop");
            }
            finally
            {
                writeTask.Stop();
                writeTask.Dispose();
            }
        }

        public void Dispose()
        {
            if(_readThread != null)
            {
                _readThread.Dispose();
                _readThread = null;
            }
        }

        /// <summary>
        /// Starts acquisition and generation
        /// </summary>
        public void Start(Func<double, int, double[,]> sampleFunction = null)
        {
            if (IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("Tried to start acquisition while running");
                return;
            }
            _readThread = new WorkerT<object>(ReadThreadRun, null, true, 3000);
            if (sampleFunction != null)
            {
                _writeThread = new WorkerT<Func<double, int, double[,]>>(WriteThreadRun, sampleFunction, true, 3000);
            }
            else
                _writeThreadReady.Set();
            IsRunning = true;
        }

        /// <summary>
        /// Stops all acquisition and generation
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
                return;
            _writeThreadReady.Reset();
            if(_readThread != null)
            {
                _readThread.Dispose();
                _readThread = null;
            }
            if (_writeThread != null)
            {
                _writeThread.Dispose();
                _writeThread = null;
            }
            IsRunning = false;
        }
        #endregion
    }
}
