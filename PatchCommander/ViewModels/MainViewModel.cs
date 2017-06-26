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

namespace WpfTestBed.ViewModels
{
    class EPhysPoint
    {
        public double Time
        {
            get; set;
        }

        public double Voltage { get; set; }
    }

    class MainViewModel : ViewModelBase
    {
        #region Members

        string _helloText;

        string _userText;

        Random _rnd;

        ChartCollection<double> _plotData;

        NationalInstruments.DAQmx.Task _ephys_task;

        AutoResetEvent _stopRead = new AutoResetEvent(false);

        //ObservableCollection<EPhysPoint> _seriesValues;

        //ObservableCollection<EPhysPoint> _plotSeries;

        double _time = 0;

        ProducerConsumer<double> _dataDump;

        DispatcherTimer _displayUpdater;

        #endregion

        public MainViewModel()
        {
            _helloText = "";
            _userText = "";
            if (IsInDesignMode)
                return;
            _rnd = new Random();
            //SeriesValues = new ObservableCollection<EPhysPoint>();
            //Values = new GearedValues<double>().WithQuality(Quality.Low);
            _dataDump = new ProducerConsumer<double>(2000);
            _displayUpdater = new DispatcherTimer();
            _displayUpdater.Tick += TimerEvent;
            _displayUpdater.Interval = new TimeSpan(0, 0, 0, 0, 5);
            _displayUpdater.Start();
            _plotData = new ChartCollection<double>(2000);
        }

        double[] values;
        int current_index = 0;

        private void TimerEvent(object sender, EventArgs e)
        {
            if (_dataDump.Count < 10)
                return;
            try
            {
                int c = _dataDump.Count;
                values = new double[c];
                for (int i = 0; i < c; i++)
                {
                    values[i] = _dataDump.Consume();
                }
                //System.Diagnostics.Debug.WriteLine("Appended data");
                PlotData.Append(values);
            }
            catch (OperationCanceledException) { }
        }

        #region Properties

        public ChartCollection<double> PlotData
        {
            get
            {
                return _plotData;
            }
            set
            {
                _plotData = value;
                RaisePropertyChanged(nameof(PlotData));
            }
        }

        public string HelloText
        {
            get
            {
                return _helloText;
            }
            set
            {
                _helloText = value;
                RaisePropertyChanged(nameof(HelloText));
            }
        }

        public string UserText
        {
            get
            {
                return _userText;
            }
            set
            {
                _userText = value;
                RaisePropertyChanged(nameof(UserText));
                RaisePropertyChanged(nameof(HasNoUserText));
            }
        }

        public bool HasNoUserText
        {
            get
            {
                return false;
            }
        }

        //public GearedValues<double> Values { get; set; }

        #endregion

        #region Methods

        public void SetDaText()
        {
            HelloText = UserText;
            EPhysPoint epoint = new EPhysPoint();
            //for (int i = 0; i < 1000; i++)
            //{
            //    epoint.Time = _time;
            //    _time += 0.01;
            //    epoint.Voltage = _rnd.NextDouble() * 20;
            //    if (SeriesValues.Count > 10000)
            //        SeriesValues.RemoveAt(0);
            //    SeriesValues.Add(epoint);
            //    //SeriesValues.Add(epoint);
            //    RaisePropertyChanged(nameof(SeriesValues));
            //    //System.Diagnostics.Debug.WriteLine(SeriesValues.Count);
            //    //Thread.Sleep(1);
            //    var t = Task.Run(async delegate
            //    {
            //        await Task.Delay(10);
            //        return 42;
            //    });
            //    t.Wait();
            //}
            if(_ephys_task != null)
            {
                _stopRead.Set();
                _ephys_task.Stop();
                _ephys_task.Dispose();
                _ephys_task = null;
                return;
            }
            else
            {
                _stopRead.Reset();
                _ephys_task = new NationalInstruments.DAQmx.Task("Patch01");
                _ephys_task.AIChannels.CreateVoltageChannel("Dev2/ai0", "Electrode1", NationalInstruments.DAQmx.AITerminalConfiguration.Differential, -10, 10, NationalInstruments.DAQmx.AIVoltageUnits.Volts);
                _ephys_task.Timing.ConfigureSampleClock("", 2000, NationalInstruments.DAQmx.SampleClockActiveEdge.Rising, NationalInstruments.DAQmx.SampleQuantityMode.ContinuousSamples);
                _ephys_task.Start();
            }
            Task updateTask = new Task(() => 
            {
                NationalInstruments.DAQmx.AnalogSingleChannelReader dataReader = new NationalInstruments.DAQmx.AnalogSingleChannelReader(_ephys_task.Stream);
                while(!_stopRead.WaitOne(0))
                {
                    double[] data = dataReader.ReadMultiSample(10);
                    for(int i = 0; i < data.Length; i++)
                    {
                        _dataDump.Produce(data[i]);
                    }
                }
                //TimeSpan ts = new TimeSpan(5000);
                //for(int i = 0; i < 50000; i++)
                //{
                //    epoint.Time = _time;
                //    _time += 0.01;
                //    if (i % 1000 == 0)
                //        epoint.Voltage = 20;
                //    else
                //        epoint.Voltage = 0;
                //    _dataDump.Produce(epoint.Voltage);
                //    if (i % 2 == 0)
                //        Thread.Sleep(1);
                //}
            });
            updateTask.Start();
        }

        #endregion
    }
}
