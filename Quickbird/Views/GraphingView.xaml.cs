﻿namespace Quickbird.Views
{
    using Windows.UI.Xaml.Controls;
    using ViewModels;
    using System.Collections.Generic;
    using DbStructure.User;
    using Windows.UI.Xaml.Navigation;
    using Windows.UI.Xaml.Controls.Primitives;
    using DbStructure;
    using Syncfusion.UI.Xaml.Charts;
    using System;
    using System.Collections.Specialized;
    using Windows.UI.Xaml.Data;
    using Windows.UI.Xaml;
    using System.ComponentModel;
    using Windows.UI.Xaml.Media;
    using Windows.UI;

    /// <summary>
    ///     An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class GraphingView : Page
    {
        private GraphingViewModel ViewModel = new GraphingViewModel();

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ChartView.ResumeSeriesNotification();
        }

        public GraphingView()
        {
            InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required; 
            ViewModel.SensorsToGraph.CollectionChanged += EditChart;           
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            ChartView.SuspendSeriesNotification();
        }



        private void CropCycleSelected(object sender, SelectionChangedEventArgs e)
        {
            ChartView.SuspendSeriesNotification();
            // TODO: Kill this and move to Viewmodel.
            // THis is broken
            // You should two way bind box.selecteditem to ViewModel.SelectedCropCycle.
            ComboBox box = (ComboBox) sender;
            if (box != null)
            {
                KeyValuePair<CropCycle, string> selection = (KeyValuePair<CropCycle, string>)box.SelectedItem;
                ViewModel.SelectedCropCycle = selection.Key;
                StartDatePicker.Date = ViewModel.CycleStartTime;
                EndDatePicker.Date = ViewModel.CycleEndTime;
                ChartView.ResumeSeriesNotification();
            }
        }


        private void OnSensorToggleChecked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var button = sender as ToggleButton;
            var tuple = button.DataContext as GraphingViewModel.SensorTuple;
            tuple.visible = true;
        }

        private void OnSensorToggleUnchecked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            var button = sender as ToggleButton;
            var tuple = button.DataContext as GraphingViewModel.SensorTuple;
            tuple.visible = false;
        }

        private void EditChart(object sender, NotifyCollectionChangedEventArgs e)
        {
            ChartView.SuspendSeriesNotification();
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                foreach (var item in e.NewItems)
                {
                    GraphingViewModel.SensorTuple tuple = item as GraphingViewModel.SensorTuple;
                    AddToChart(tuple);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (var item in e.OldItems)
                {
                    GraphingViewModel.SensorTuple tuple = item as GraphingViewModel.SensorTuple;
                    ChartView.Series.Remove(tuple.ChartSeries);
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Replace
                || e.Action == NotifyCollectionChangedAction.Reset)
            {
                ChartView.Series.Clear();
                foreach (var sensorTuple in ViewModel.SensorsToGraph)
                {
                    AddToChart(sensorTuple);
                }
            }
            ChartView.ResumeSeriesNotification();
        }

        private void AddToChart(GraphingViewModel.SensorTuple tuple)
        {
            ChartSeries chartSeries;
            if (tuple.sensor.SensorTypeID == 19)
            {
                var series = new AreaSeries();
                series.Interior = new SolidColorBrush { Color = Colors.PaleGreen, Opacity = 0.5 }; 
                series.YBindingPath = "value";
                chartSeries = series;
            }
            else
            {
                var series = new FastLineSeries();
                series.YBindingPath = "value";
                chartSeries = series; 
            }
            chartSeries.ItemsSource = tuple.historicalDatapoints;
            chartSeries.EnableAnimation = true;
            chartSeries.AnimationDuration = TimeSpan.FromMilliseconds(150); 
            chartSeries.XBindingPath = "timestamp";

            tuple.ChartSeries = chartSeries;
            tuple.Axis = DateAxis; 
            chartSeries.IsSeriesVisible = false;

            //This is a string shortener! nothing else
            var placementNameLength = tuple.sensor.SensorType.Place.Name.Length > 6 ? 6 : tuple.sensor.SensorType.Place.Name.Length;
            var locationString = tuple.sensor.SensorType.Place.Name.Substring(0, placementNameLength);
            int spaceLocation = tuple.sensor.SensorType.Place.Name.IndexOf(' ');
            if (spaceLocation > 0 && tuple.sensor.SensorType.Place.Name.Length > spaceLocation + 1)
                locationString += tuple.sensor.SensorType.Place.Name.Substring(spaceLocation, 2) + ".";

      
            chartSeries.Label = tuple.sensor.SensorType.Param.Name + ": " + locationString;

            ChartView.Series.Add(chartSeries);
            
        }

        private void EndDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            ChartView.SuspendSeriesNotification();
            if (args.NewDate.HasValue)
            {
                if (args.NewDate.Value.LocalDateTime.Date == ViewModel.CycleEndTime.Date)
                {
                    DateAxis.Maximum = ViewModel.CycleEndTime.LocalDateTime;
                }
                else
                {
                    DateAxis.Maximum = args.NewDate.Value.LocalDateTime.Date;
                }
                ViewModel.ChosenGraphPeriod = (DateTime)DateAxis.Maximum - (DateTime)DateAxis.Minimum;
            }
            ChartView.ResumeSeriesNotification();
        }

        private void StartDatePicker_DateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
        {
            ChartView.SuspendSeriesNotification();
            if (args.NewDate.HasValue)
            {
                if (args.NewDate.Value.LocalDateTime.Date > ViewModel.CycleStartTime.LocalDateTime)
                {
                    DateAxis.Minimum = args.NewDate.Value.LocalDateTime.Date;
                }
                else
                {
                    DateAxis.Minimum = ViewModel.CycleStartTime.LocalDateTime; 
                }
                ViewModel.ChosenGraphPeriod = (DateTime)DateAxis.Maximum - (DateTime)DateAxis.Minimum; 
            }
            ChartView.ResumeSeriesNotification();
        }
    }
}