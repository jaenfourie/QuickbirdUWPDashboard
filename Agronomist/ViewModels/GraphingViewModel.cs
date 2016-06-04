﻿using DatabasePOCOs;
using DatabasePOCOs.User;
using DatabasePOCOs.Global;
using System.Collections.Generic;
using System;
using Windows.UI.Xaml;
using Agronomist.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Linq;
using MoreLinq;
using Windows.UI.Xaml.Data;
using System.Collections.ObjectModel;
using Agronomist.Util;

namespace Agronomist.ViewModels
{
    public class GraphingViewModel : ViewModelBase
    {
        private string _title = "Graphs";
        private MainDbContext _db = null;

        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly Action<IEnumerable<Messenger.SensorReading>> _recieveDatapointAction;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly Action<string> _loadCacheAction;

        /// <summary>
        /// Cached Data, of all cropCycles
        /// </summary>
        private List<CroprunTuple> _cache = new List<CroprunTuple>();

        /* This data applies to the chosen crop cycle only*/
        private CropCycle _selectedCropCycle; 

        private IEnumerable<IGrouping<string, SensorTuple>> _sensors;
        private DateTimeOffset _selectedStartTime;
        private DateTimeOffset? _selectedEndTime;
        private bool _currentlyRunning = true;

        private bool _historicalMode; 

        //Probaablyt don't need this
        private DispatcherTimer _refresher = null; 

        //Other stuff
        //Hour Long Buffer
        //Histrotical Buffer 

        public GraphingViewModel(){
            _db = new MainDbContext();

            _recieveDatapointAction = ReceiveDatapoint;
            _loadCacheAction = LoadCache;
            Messenger.Instance.NewSensorDataPoint.Subscribe(_recieveDatapointAction);
            Messenger.Instance.TablesChanged.Subscribe(_loadCacheAction);

            //Settings settings = new Settings();
            //settings.UnsetCreds(); 

            //LoadData
            LoadCache(); 
        }

        public void LoadCache(string obj)
        {
            LoadCache(); 
        }

        public void ReceiveDatapoint(IEnumerable<Messenger.SensorReading> readings)
        {
            if (SensorsGrouped != null)
            {
                List<SensorTuple> sensorsUngrouped = SensorsGrouped.SelectMany(group => group).ToList();
                foreach (Messenger.SensorReading reading in readings)
                {
                    SensorTuple tuple = sensorsUngrouped.FirstOrDefault(stup => stup.sensor.ID == reading.SensorId);
                    if (tuple != null)
                    {
                        if (tuple.hourlyDatapoints.Any(dp => dp.timestamp == reading.Timestamp.LocalDateTime) == false)
                        {
                            BindableDatapoint datapoint = new BindableDatapoint(reading.Timestamp, reading.Value);
                            tuple.hourlyDatapoints.Add(datapoint);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Refreshed Cache
        /// </summary>
        private async void LoadCache()
        {
            var dbLocations = await _db.Locations
                .Include(loc => loc.CropCycles)
                .Include(loc => loc.Devices)
                .AsNoTracking().ToListAsync();

            var sensorList = await _db.Sensors
                .Include(sen => sen.SensorType)
                .Include(sen => sen.SensorType.Place)
                .Include(sen => sen.SensorType.Param)
                .Include(sen => sen.SensorType.Subsystem)
                .AsNoTracking().ToListAsync(); //Need to edit 
            
            List<CroprunTuple> cache = 
                new List<CroprunTuple>();


            foreach (CropCycle crop in dbLocations.SelectMany(loc => loc.CropCycles))
            {
                CroprunTuple cacheItem = new CroprunTuple(crop, crop.Location);

                List<Guid> deviceIDs = crop.Location.Devices.Select(dev => dev.ID).ToList(); 
                foreach(Sensor sensor in sensorList)
                {
                    if (deviceIDs.Contains(sensor.DeviceID))
                    {
                        cacheItem.sensors.Add(sensor);
                    }
                }

                cache.Add(cacheItem); 
            }
            if(_selectedCropCycle == null)
            {
                _selectedCropCycle = cache.FirstOrDefault().cropCycle; 
            }
            Cache = cache; 
            
        }

        public string Title
        {
            get { return _title; }
            set
            {
                if(value == _title) return;
                _title = value;
                OnPropertyChanged();
            }
        }

        public List<KeyValuePair<CropCycle, string>> CropRunList
        {
            get
            {
                List<KeyValuePair<CropCycle, string>> result = new List<KeyValuePair<CropCycle, string>>(); 
                foreach(var tuple in Cache)
                {
                    string displayName = $"{tuple.location.Name} - {tuple.cropCycle.CropTypeName}: " 
                       + $"{tuple.cropCycle.StartDate.LocalDateTime.Date.ToString("dd MMM")}"
                       + $"-{tuple.cropCycle.EndDate?.LocalDateTime.Date.ToString("dd MMM") ?? "Now"}"; 
                        
                    result.Add(new KeyValuePair<CropCycle, string>(tuple.cropCycle, displayName)); 
                }
                return result; 
            }
        }

        public List<CroprunTuple> Cache
        {
            get { return _cache; }
            set
            {
                if (value == _cache) return;
                else
                {
                    _cache = value;
                    if(_selectedCropCycle != null)
                    SelectedCropCycle = _cache.First(l => l.cropCycle.ID == _selectedCropCycle.ID).cropCycle; 

                    OnPropertyChanged();
                    OnPropertyChanged("Locations");
                    OnPropertyChanged("CropRunList");
                }

            }
        }

        public List<Location> Locations
        {
            get { return Cache.DistinctBy(c => c.location).Select(tup => tup.location).ToList();}
        }


        public IEnumerable<IGrouping<string, SensorTuple>> SensorsGrouped //Replace with Igrouping or CollectionViewSource
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

        public CropCycle SelectedCropCycle
        {
            get { return _selectedCropCycle; }
            set
            {
                if (value == _selectedCropCycle) return;
                else
                {
                    _selectedCropCycle = value;
                    SensorsToGraph.Clear();

                    if (_selectedCropCycle.EndDate == null)
                    { _currentlyRunning = true;  }
                    else
                    { _currentlyRunning = false; }
                    List<Sensor> sensors = _cache.First(c => c.cropCycle.ID == value.ID).sensors;
                    
                    foreach (var sensor in sensors)
                    {
                        var tuple = new SensorTuple
                        {
                            displayName = sensor.SensorType.Param.Name,
                            sensor = sensor
                        };
                        SensorsToGraph.Add(tuple);
                    }
                    SensorsGrouped = SensorsToGraph.GroupBy(tup => tup.sensor.SensorType.Place.Name);
                                       
                    SelectedEndTime = _selectedCropCycle.EndDate ?? DateTimeOffset.Now;
                    _selectedEndTime = _selectedCropCycle.EndDate;
                    SelectedStartTime = _selectedCropCycle.StartDate; 

                    OnPropertyChanged();
                }
            }
        }

        public Visibility HistControls
        {
            get { return _historicalMode ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility RealtimeControls
        {
            get { return _historicalMode ? Visibility.Collapsed : Visibility.Visible; }
        }

        public DateTimeOffset SelectedEndTime
        {
            get { return _selectedEndTime ?? DateTimeOffset.Now; }
            set{
                _selectedEndTime = value;
                OnPropertyChanged();
            }
        }

        public DateTimeOffset SelectedStartTime
        {
            get { return _selectedStartTime; }
            set { _selectedStartTime = value;
                OnPropertyChanged(); 
            }
        }


        //Destructor
        ~GraphingViewModel(){
            try
            {
                _refresher?.Stop();
                _db?.Dispose();
            }
            catch(Exception e)
            {

            }
            
        }

        public class SensorTuple : ViewModelBase
        {
            public string displayName { get; set; }
            public Sensor sensor;

            private bool _visible = false;
            private bool _historyMode = false; 

            public bool HistoryMode
            {
                get { return _historyMode; }
                set
                {
                    _historyMode = value;
                    OnPropertyChanged("DataToGraph"); 
                }
            }

            public bool visible {
                get
                {
                    return _visible; 
                }
                set
                {
                    _visible = value;
                    if(DataToGraph != null)
                    {
                        ChartSeries.IsEnabled = _visible;
                        ChartSeries.IsSeriesVisible = _visible;
                    }
                }
            } 
            
            /// <summary>
            /// Updated as soon as possible
            /// </summary>
            public ObservableCollection<BindableDatapoint> hourlyDatapoints { get; set; } = new ObservableCollection<BindableDatapoint>();

            /// <summary>
            /// Only read from the DB, not reloaded in realtime
            /// </summary>
            public ObservableCollection<BindableDatapoint> historicalDatapoints { get; set; } = new ObservableCollection<BindableDatapoint>();

            public ObservableCollection<BindableDatapoint> DataToGraph
            {
                get { if (HistoryMode)
                        return historicalDatapoints;
                    else
                        return hourlyDatapoints; 
                }
            }

            public Syncfusion.UI.Xaml.Charts.ChartSeries ChartSeries = null;
        }

        public struct CroprunTuple
        {
            public CroprunTuple(CropCycle inCropCycle, Location inLocation)
            {
                cropCycle = inCropCycle;
                location = inLocation;
                sensors = new System.Collections.Generic.List<Sensor>(); 
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