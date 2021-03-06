﻿namespace Quickbird.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using Windows.UI.Xaml;
    using DbStructure;
    using DbStructure.User;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using MoreLinq;
    using Syncfusion.UI.Xaml.Charts;
    using Util;

    public class GraphingViewModel : ViewModelBase, IDisposable
    {
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly Action<string> _loadCacheAction;

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly Action<IEnumerable<BroadcasterService.SensorReading>> _recieveDatapointAction;

        /// <summary>Cached Data, of all cropCycles</summary>
        private List<CroprunTuple> _cache = new List<CroprunTuple>();

        private TimeSpan _chosenGraphPeriod;
        private readonly MainDbContext _db;

        //Other stuff
        //Hour Long Buffer
        //Histrotical Buffer 

        private readonly Action _pauseChart;

        private bool _realtimeMode;

        //Probaablyt don't need this
        private readonly DispatcherTimer _refresher = null;
        private readonly Action _resumeChart;

        /* This data applies to the chosen crop cycle only*/
        private CropCycle _selectedCropCycle;

        private DateTimeOffset? _selectedEndTime;
        private IEnumerable<IGrouping<string, SensorTuple>> _sensors;

        public GraphingViewModel(Action PauseChart, Action ResumeChart)
        {
            _db = new MainDbContext();
            _pauseChart = PauseChart;
            _resumeChart = ResumeChart;

            _recieveDatapointAction = ReceiveDatapoint;
            _loadCacheAction = LoadCache;
            BroadcasterService.Instance.NewSensorDataPoint.Subscribe(_recieveDatapointAction);

            //*crashes the app and screwes up graphs. Not clear why we should update them. in this frame. 
            //Messenger.Instance.TablesChanged.Subscribe(_loadCacheAction);

            //LoadData
            LoadCache();
        }

        public List<CroprunTuple> Cache
        {
            get { return _cache; }
            set
            {
                if (value == _cache) return;
                _cache = value;
                if (_selectedCropCycle != null)
                    SelectedCropCycle = _cache.First(l => l.cropCycle.ID == _selectedCropCycle.ID).cropCycle;

                OnPropertyChanged();
                OnPropertyChanged("Locations");
                OnPropertyChanged("CropRunList");
            }
        }

        public TimeSpan ChosenGraphPeriod
        {
            get { return _chosenGraphPeriod; }
            set
            {
                _chosenGraphPeriod = value;
                OnPropertyChanged();
                OnPropertyChanged("TimeLabel");
            }
        }

        public List<KeyValuePair<CropCycle, string>> CropRunList
        {
            get
            {
                var result = new List<KeyValuePair<CropCycle, string>>();
                foreach (var tuple in Cache)
                {
                    var displayName = $"{tuple.location.Name} - {tuple.cropCycle.CropTypeName}: " +
                                      $"{tuple.cropCycle.StartDate.LocalDateTime.Date.ToString("dd MMM")}" +
                                      $"-{tuple.cropCycle.EndDate?.LocalDateTime.Date.ToString("dd MMM") ?? "Now"}";

                    result.Add(new KeyValuePair<CropCycle, string>(tuple.cropCycle, displayName));
                }
                return result;
            }
        }

        public DateTimeOffset CycleEndTime { get { return _selectedCropCycle?.EndDate ?? DateTimeOffset.Now; } }


        public DateTimeOffset CycleStartTime
        {
            get { return _selectedCropCycle?.StartDate ?? DateTimeOffset.Now.AddDays(-1); }
        }

        public TimeSpan GraphPeriod { get; private set; }

        public Visibility HistControls { get { return _realtimeMode ? Visibility.Collapsed : Visibility.Visible; } }

        public bool LiveCropRun { get; private set; } = true;

        public List<Location> Locations
        {
            get { return MoreEnumerable.DistinctBy(Cache, c => c.location).Select(tup => tup.location).ToList(); }
        }

        public bool RealtimeMode
        {
            get { return _realtimeMode; }
            set
            {
                _realtimeMode = value;
                OnPropertyChanged("HistControls");
                OnPropertyChanged("TimeLabel");
                foreach (var tuple in SensorsToGraph)
                {
                    tuple.RealtimeMode = _realtimeMode;
                }
            }
        }

        /// <summary>One of the main method, when user selects crop cycle, this sets up al lthe variables</summary>
        public CropCycle SelectedCropCycle
        {
            get { return _selectedCropCycle; }
            set
            {
                {
                    _selectedCropCycle = value;

                    SensorsToGraph.Clear();

                    if (_selectedCropCycle.EndDate == null)
                    {
                        LiveCropRun = true;
                    }
                    else
                    {
                        LiveCropRun = false;
                    }

                    var sensors = _cache.First(c => c.cropCycle.ID == value.ID).sensors;

                    foreach (var sensor in sensors)
                    {
                        var tuple = new SensorTuple {displayName = sensor.SensorType.Param.Name, sensor = sensor};
                        SensorsToGraph.Add(tuple);
                    }
                    SensorsGrouped = SensorsToGraph.GroupBy(tup => tup.sensor.SensorType.Place.Name);
                    LoadHistoricalData();
                    GraphPeriod = (_selectedCropCycle.EndDate ?? DateTimeOffset.Now) - _selectedCropCycle.StartDate;
                    _selectedEndTime = _selectedCropCycle.EndDate;

                    RealtimeMode = false;
                    OnPropertyChanged();
                    OnPropertyChanged("RealtimeMode");
                    OnPropertyChanged("LiveCropRun");
                    OnPropertyChanged("CycleEndTime");
                    OnPropertyChanged("CycleStartTime");
                }
            }
        }


        public IEnumerable<IGrouping<string, SensorTuple>> SensorsGrouped
            //Replace with Igrouping or CollectionViewSource
        {
            get { return _sensors; }
            set
            {
                if (value == _sensors) return;
                _sensors = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<SensorTuple> SensorsToGraph { get; set; } = new ObservableCollection<SensorTuple>();


        public string TimeLabel
        {
            get
            {
                if (_realtimeMode)
                    return "hh:mm:ss";
                if (_chosenGraphPeriod < TimeSpan.FromHours(48))
                {
                    return "t";
                }
                if (_chosenGraphPeriod < TimeSpan.FromDays(15))
                {
                    return "ddd H:mm";
                }
                return "M";
            }
        }

        public void Dispose()
        {
            BroadcasterService.Instance.NewSensorDataPoint.Unsubscribe(_recieveDatapointAction);
            _refresher?.Stop();
            _db?.Dispose();
        }

        public override void Kill() { Dispose(); }

        public void LoadCache(string obj) { LoadCache(); }


        public void ReceiveDatapoint(IEnumerable<BroadcasterService.SensorReading> readings)
        {
            if (SensorsToGraph != null)
            {
                _pauseChart.Invoke();

                var Added = false;
                foreach (var reading in readings)
                {
                    var tuple = SensorsToGraph.FirstOrDefault(stup => stup.sensor.ID == reading.SensorId);
                    if (tuple != null)
                    {
                        if (tuple.hourlyDatapoints.Count == 0 ||
                            tuple.hourlyDatapoints.LastOrDefault().timestamp < reading.Timestamp.LocalDateTime)
                        {
                            Added = true;
                            var datapoint = new BindableDatapoint(reading.Timestamp, reading.Value);
                            tuple.hourlyDatapoints.Add(datapoint);

                            //Remove datapoints if we are storing more than an hour of them
                            if (tuple.hourlyDatapoints.Count > 3000)
                                tuple.hourlyDatapoints.RemoveAt(0);
                        }
                    }
                }
                if (Added)
                {
                    var tupleForChartUpdate = SensorsToGraph.FirstOrDefault();
                    if (tupleForChartUpdate?.RealtimeMode ?? false)
                    {
                        tupleForChartUpdate.Axis.Minimum =
                            tupleForChartUpdate.hourlyDatapoints.FirstOrDefault()?.timestamp ??
                            DateTime.Now.AddHours(-1);
                        tupleForChartUpdate.Axis.Maximum =
                            tupleForChartUpdate.hourlyDatapoints.LastOrDefault()?.timestamp ?? DateTime.Now;
                    }
                }

                _resumeChart.Invoke();
            }
        }

        /// <summary>Refreshed Cache</summary>
        private async void LoadCache()
        {
            var dbFetchTask = Task.Run(() =>
            {
                var dbLocationsRet =
                    _db.Locations.Include(loc => loc.CropCycles).Include(loc => loc.Devices).AsNoTracking().ToList();

                var sensorListRet =
                    _db.Sensors.Include(sen => sen.SensorType)
                        .Include(sen => sen.SensorType.Place)
                        .Include(sen => sen.SensorType.Param)
                        .Include(sen => sen.SensorType.Subsystem)
                        .AsNoTracking()
                        .ToList(); //Need to edit 

                return new Tuple<List<Location>, List<Sensor>>(dbLocationsRet, sensorListRet);
            });

            var result = await dbFetchTask;
            var dbLocations = result.Item1;
            var sensorList = result.Item2;


            var cache = new List<CroprunTuple>();


            foreach (var crop in dbLocations.SelectMany(loc => loc.CropCycles))
            {
                var cacheItem = new CroprunTuple(crop, crop.Location);

                var deviceIDs = crop.Location.Devices.Select(dev => dev.ID).ToList();
                foreach (var sensor in sensorList)
                {
                    if (deviceIDs.Contains(sensor.DeviceID))
                    {
                        cacheItem.sensors.Add(sensor);
                    }
                }

                cache.Add(cacheItem);
            }
            if (_selectedCropCycle == null)
            {
                _selectedCropCycle = cache.FirstOrDefault().cropCycle;
            }
            Cache = cache;
        }

        private async void LoadHistoricalData()
        {
            var endDate = _selectedCropCycle.EndDate?.AddDays(1) ?? DateTimeOffset.Now.AddDays(1);
            var sensorsHistories =
                await
                    _db.SensorsHistory.Where(
                        sh =>
                            sh.LocationID == _selectedCropCycle.LocationID &&
                            sh.TimeStamp > _selectedCropCycle.StartDate && sh.TimeStamp < endDate).ToListAsync();


            Parallel.ForEach(SensorsToGraph, tuple =>
            {
                var shCollection = sensorsHistories.Where(sh => sh.SensorID == tuple.sensor.ID);
                var datapointCollection = new List<BindableDatapoint>();
                foreach (var sh in shCollection)
                {
                    sh.DeserialiseData();
                    foreach (var dp in sh.Data)
                    {
                        if (dp.TimeStamp > _selectedCropCycle.StartDate)
                            // becuase datapoints are packed into days, we must avoid datapoitns that astarted before the cropRun
                        {
                            var bindable = new BindableDatapoint(dp);
                            datapointCollection.Add(bindable);
                        }
                    }
                }
                var ordered = datapointCollection.OrderBy(dp => dp.timestamp);


                tuple.historicalDatapoints = new ObservableCollection<BindableDatapoint>(ordered);
            });
        }


        public class SensorTuple : ViewModelBase
        {
            private ObservableCollection<BindableDatapoint> _historicalData =
                new ObservableCollection<BindableDatapoint>();

            private bool _realtimeMode;

            private bool _visible;

            public DateTimeAxis Axis = null;

            public ChartSeries ChartSeries = null;
            public Sensor sensor;
            public string displayName { get; set; }

            /// <summary>Only read from the DB, not reloaded in realtime</summary>
            public ObservableCollection<BindableDatapoint> historicalDatapoints
            {
                get { return _historicalData; }
                set
                {
                    _historicalData = value;
                    OnPropertyChanged();
                }
            }

            /// <summary>Updated as soon as possible</summary>
            public ObservableCollection<BindableDatapoint> hourlyDatapoints { get; set; } =
                new ObservableCollection<BindableDatapoint>();

            public bool RealtimeMode
            {
                get { return _realtimeMode; }
                set
                {
                    _realtimeMode = value;
                    if (ChartSeries != null)
                    {
                        var source = _realtimeMode ? hourlyDatapoints : historicalDatapoints;
                        ChartSeries.ItemsSource = source;
                        Axis.Minimum = source.FirstOrDefault()?.timestamp ?? DateTime.Now.AddHours(-1);
                        Axis.Maximum = source.LastOrDefault()?.timestamp ?? DateTime.Now;
                    }
                }
            }

            public bool visible
            {
                get { return _visible; }
                set
                {
                    _visible = value;
                    if (ChartSeries != null)
                    {
                        ChartSeries.IsEnabled = _visible;
                        ChartSeries.IsSeriesVisible = _visible;
                    }
                }
            }

            public override void Kill()
            {
                // I like spaghetti you like spaghetti.
            }
        }

        public struct CroprunTuple
        {
            public CroprunTuple(CropCycle inCropCycle, Location inLocation)
            {
                cropCycle = inCropCycle;
                location = inLocation;
                sensors = new List<Sensor>();
            }

            public CropCycle cropCycle;
            public Location location;

            public List<Sensor> sensors;
        }

        public class BindableDatapoint
        {
            public BindableDatapoint(SensorDatapoint datapoint)
            {
                timestamp = datapoint.TimeStamp.LocalDateTime;
                value = datapoint.Value;
            }

            public BindableDatapoint(DateTimeOffset inTimestamp, double inValue)
            {
                timestamp = inTimestamp.LocalDateTime;
                value = inValue;
            }

            public DateTime timestamp { get; set; }
            public double value { get; set; }
        }
    }
}
