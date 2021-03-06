﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NationalInstruments.DAQmx;
using MHApi.Utilities;
using MHApi.Threading;
using MHApi.GUI;
using System.Threading;

namespace PatchCommander.Hardware
{
    /// <summary>
    /// Sample read event args
    /// </summary>
    public class ReadDoneEventArgs
    {
        /// <summary>
        /// The sample data
        /// </summary>
        readonly double[,] _data;

        /// <summary>
        /// The running index of the first sample in data
        /// </summary>
        readonly long _startIndex;

        /// <summary>
        /// The sample data
        /// </summary>
        public double[,] Data
        {
            get
            {
                return _data;
            }
        }

        /// <summary>
        /// The running index of the first sample in data
        /// </summary>
        public long StartIndex
        {
            get
            {
                return _startIndex;
            }
        }

        public ReadDoneEventArgs(double[,] samples, long startIndex)
        {
            _data = samples;
            _startIndex = startIndex;
        }
    }

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
        /// Writes samples to the digital channel controlling
        /// channel modes
        /// </summary>
        DigitalSingleChannelWriter[] _chModeWriters;

        /// <summary>
        /// Digital out tasks to control channel modes
        /// </summary>
        Task[] _chModeTasks;

        /// <summary>
        /// The read thread reading from analog in channels
        /// </summary>
        WorkerT<long> _readThread;

        /// <summary>
        /// The write thread for writing analog out samples
        /// </summary>
        WorkerT<Func<long, int, double[,]>> _writeThread;

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
            if (ViewModelBase.IsInDesignMode)
                return;
            //Create digital out tasks and writers to control channel mode
            _chModeTasks = new Task[2];
            _chModeTasks[0] = new Task("Ch1Mode");
            _chModeTasks[0].DOChannels.CreateChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch1Mode, "", ChannelLineGrouping.OneChannelForAllLines);
            System.Diagnostics.Debug.WriteLine("Created Ch1Mode task");
            _chModeTasks[1] = new Task("Ch2Mode");
            _chModeTasks[1].DOChannels.CreateChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch2Mode, "", ChannelLineGrouping.OneChannelForAllLines);
            System.Diagnostics.Debug.WriteLine("Created Ch2Mode task");
            _chModeWriters = new DigitalSingleChannelWriter[2];
            _chModeWriters[0] = new DigitalSingleChannelWriter(_chModeTasks[0].Stream);
            _chModeWriters[1] = new DigitalSingleChannelWriter(_chModeTasks[1].Stream);
            System.Diagnostics.Debug.WriteLine("Created mode writers");
            //At startup, set both main channels to VoltageClamp
            _channelModes = new ClampMode[2];
            Channel1Mode = ClampMode.CurrentClamp;
            Channel2Mode = ClampMode.CurrentClamp;
            Channel1Mode = ClampMode.VoltageClamp;
            Channel2Mode = ClampMode.VoltageClamp;
            System.Diagnostics.Debug.WriteLine("All modes set to voltage clamp");
        }

        #region Properties

        /// <summary>
        /// The recording mode of channel1
        /// </summary>
        public ClampMode Channel1Mode
        {
            get
            {
                return _channelModes[0];
            }
            set
            {
                if (_chModeWriters == null || _chModeWriters[0] == null)
                {
                    System.Diagnostics.Debug.WriteLine("Can't set channel 1 mode without digital IO");
                    return;
                }
                if (value != _channelModes[0])
                {
                    //write new value
                    if (value == ClampMode.CurrentClamp)
                    {
                        _chModeWriters[0].WriteSingleSampleSingleLine(true, false);
                        _chModeTasks[0].Stop();
                    }
                    else
                    {
                        _chModeWriters[0].WriteSingleSampleSingleLine(true, true);
                        _chModeTasks[0].Stop();
                    }
                    _channelModes[0] = value;
                    RaisePropertyChanged(nameof(Channel1Mode));
                }
            }
        }

        /// <summary>
        /// The recording mode of channel2
        /// </summary>
        public ClampMode Channel2Mode
        {
            get
            {
                return _channelModes[1];
            }
            set
            {
                if (_chModeWriters == null || _chModeWriters.Length < 2 || _chModeWriters[1] == null)
                {
                    System.Diagnostics.Debug.WriteLine("Can't set channel 2 mode without digital IO");
                    return;
                }
                if (value != _channelModes[1])
                {
                    //write new value
                    if (value == ClampMode.CurrentClamp)
                    {
                        _chModeWriters[1].WriteSingleSampleSingleLine(true, false);
                        _chModeTasks[1].Stop();
                    }
                    else
                    {
                        _chModeWriters[1].WriteSingleSampleSingleLine(true, true);
                        _chModeTasks[1].Stop();
                    }
                    _channelModes[1] = value;
                    RaisePropertyChanged(nameof(Channel2Mode));
                }
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

        public delegate void ReadDoneEvent(ReadDoneEventArgs args);

        /// <summary>
        /// Event that signals that a new data package was read off the board
        /// </summary>
        public event ReadDoneEvent ReadDone;

        /// <summary>
        /// Event that signals that the read thread has finished
        /// </summary>
        public event Action ReadThreadFinished;

        #endregion

        #region Methods

        void ReadThreadRun(AutoResetEvent stop, long maxRead)
        {
            //maxRead < 0 indicates "unlimited" reads
            if (maxRead < 0)
                maxRead = long.MaxValue;
            Task readTask = new Task("EphysRead");
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch1Read, "Electrode1", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch2Read, "Electrode2", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch1ModeRead, "Mode1", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch2ModeRead, "Mode2", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch1CommandRead, "Command1", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.Ch2CommandRead, "Command2", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.AIChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + HardwareSettings.DAQ.LaserRead, "LaserRead", AITerminalConfiguration.Differential, -10, 10, AIVoltageUnits.Volts);
            readTask.Timing.ConfigureSampleClock("", HardwareSettings.DAQ.Rate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            long sampleIndex = 0;
            _writeThreadReady.WaitOne();
            try
            {
                readTask.Start();
                AnalogMultiChannelReader dataReader = new AnalogMultiChannelReader(readTask.Stream);
                while (!stop.WaitOne(0) && sampleIndex < maxRead)
                {
                    var nsamples = readTask.Stream.AvailableSamplesPerChannel;
                    if (nsamples >= 10)
                    {
                        double[,] read = dataReader.ReadMultiSample((int)nsamples);
                        if (ReadDone != null)
                            ReadDone.Invoke(new ReadDoneEventArgs(read, sampleIndex));
                        //Update our running index
                        sampleIndex += nsamples;
                    }
                }
            }
            finally
            {
                readTask.Stop();
                readTask.Dispose();
                //Signal to subscribers that we exited
                //Note: Has to be asynchronous so that we leave here before stop gets called
                if (ReadThreadFinished != null)
                    ReadThreadFinished.BeginInvoke(null, null);
            }
        }

        void WriteThreadRun(AutoResetEvent stop, Func<long, int, double[,]> sampleFunction)
        {
            Task writeTask = new Task("EphysWrite");
            double[,] firstSamples = sampleFunction(0, HardwareSettings.DAQ.Rate);
            if (firstSamples.GetLength(1) != HardwareSettings.DAQ.Rate)
                throw new ApplicationException("Did not receive the required number of samples");
            var nChannels = firstSamples.GetLength(0);
            for (int i = 0; i < nChannels; i++)
                writeTask.AOChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + string.Format("AO{0}", i), "", -10, 10, AOVoltageUnits.Volts);
            //Note: Can't use ai clock, since we cannot guarantee that the read thread ai task finishes *after* the write task
            //otherwise Task.Stop will block indefinitely...
            writeTask.Timing.ConfigureSampleClock("", HardwareSettings.DAQ.Rate, SampleClockActiveEdge.Rising, SampleQuantityMode.ContinuousSamples);
            writeTask.Triggers.StartTrigger.ConfigureDigitalEdgeTrigger("ai/StartTrigger ", DigitalEdgeStartTriggerEdge.Rising);
            writeTask.Stream.WriteRegenerationMode = WriteRegenerationMode.DoNotAllowRegeneration;
            AnalogMultiChannelWriter dataWriter = new AnalogMultiChannelWriter(writeTask.Stream);
            dataWriter.WriteMultiSample(false, firstSamples);
            writeTask.Start();
            _writeThreadReady.Set();
            long start_sample = HardwareSettings.DAQ.Rate;
            try
            {
                while (!stop.WaitOne(50))
                {                    
                    double[,] samples = sampleFunction(start_sample, HardwareSettings.DAQ.Rate / 5);
                    if (samples == null)
                        break;
                    dataWriter.WriteMultiSample(false, samples);
                    start_sample += HardwareSettings.DAQ.Rate / 5;
                }
                System.Diagnostics.Debug.WriteLine("Left write loop");
            }
            finally
            {
                writeTask.Stop();
                writeTask.Dispose();
            }
        }

        /// <summary>
        /// Starts acquisition and generation
        /// </summary>
        public void Start(Func<long, int, double[,]> sampleFunction = null, long maxSamplesRead = -1)
        {
            if (IsRunning)
            {
                System.Diagnostics.Debug.WriteLine("Tried to start acquisition while running");
                return;
            }
            _readThread = new WorkerT<long>(ReadThreadRun, maxSamplesRead, true, 3000);
            if (sampleFunction != null)
            {
                _writeThread = new WorkerT<Func<long, int, double[,]>>(WriteThreadRun, sampleFunction, true, 3000);
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
            if (_writeThread != null)
            {
                _writeThread.Stop();
                _writeThread.Dispose();
                _writeThread = null;
            }
            if (_readThread != null)
            {
                _readThread.Stop();
                _readThread.Dispose();
                _readThread = null;
            }
            //Reset all writes to 0
            Task resetTask = new Task("ChReset");
            for (int i = 0; i < 3; i++)
                resetTask.AOChannels.CreateVoltageChannel(HardwareSettings.DAQ.DeviceName + "/" + string.Format("AO{0}", i), "", -10, 10, AOVoltageUnits.Volts);
            AnalogMultiChannelWriter resetWriter = new AnalogMultiChannelWriter(resetTask.Stream);
            resetWriter.WriteSingleSample(true, new double[3]);
            resetTask.Dispose();
            IsRunning = false;
        }
        #endregion

        public void Dispose()
        {
            if (_writeThread != null)
            {
                _writeThread.Dispose();
                _writeThread = null;
            }
            if (_readThread != null)
            {
                _readThread.Dispose();
                _readThread = null;
            }
            if (_chModeTasks != null)
            {
                if (_chModeTasks[0] != null)
                {
                    _chModeTasks[0].Dispose();
                    _chModeTasks[0] = null;
                }
                if (_chModeTasks.Length > 1 && _chModeTasks[1] != null)
                {
                    _chModeTasks[1].Dispose();
                    _chModeTasks[1] = null;
                }
                _chModeTasks = null;
                _chModeWriters = null;
            }
        }

        ~DAQ()
        {
            Dispose();
        }
    }
}
