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
        /// Indicates whether we are currently acquiring/generating data
        /// </summary>
        bool _isRunning;

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
            readTask.Timing.ConfigureSampleClock("", 2000, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
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
        public void Start()
        {
            if (IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("Tried to start acquisition while running");
                return;
            }
            _readThread = new WorkerT<object>(ReadThreadRun, null, true, 3000);
            IsRunning = true;
        }

        /// <summary>
        /// Stops all acquisition and generation
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
                return;
            if(_readThread != null)
            {
                _readThread.Dispose();
                _readThread = null;
            }
            IsRunning = false;
        }
        #endregion
    }
}
