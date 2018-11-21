using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using MahApps.Metro.Controls.Dialogs;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Linq;
using System.Diagnostics;
using WorkloadViewer.Model;
using System.Collections.ObjectModel;
using System.Data;
using System.Windows;

namespace WorkloadViewer.ViewModel
{

    public class MainViewModel : ViewModelBase
    {
        public bool CompareMode
        {
            get
            {
                return _benchmarkWorkloadAnalysis != null;
            }
        }

        public Visibility CompareModeVisibility
        {
            get
            {
                if(CompareMode) return Visibility.Visible;
                else return Visibility.Collapsed;
            }
        }

        private Options _options;
        private WorkloadAnalysis _baselineWorkloadAnalysis;
        private WorkloadAnalysis _benchmarkWorkloadAnalysis;

        public string StatusMessage { get; set; }

        private PlotModel[] PlotModels = new PlotModel[3];

        public PlotModel CpuPlotModel { get; private set; }
        public PlotModel DurationPlotModel { get; private set; }
        public PlotModel BatchesPlotModel { get; private set; }


        public List<FilterDefinition> HostList { get; set; }
        public List<FilterDefinition> ApplicationList { get; set; }
        public List<FilterDefinition> DatabaseList { get; set; }

        public ICommand LoadedCommand { get; set; }
        public ICommand RenderedCommand { get; set; }
        public ICommand KeyDownCommand { get; set; }

        public DataTable Queries { get; private set; }



        private IDialogCoordinator _dialogCoordinator;
        private DateTime _lastAxisAdjust = DateTime.Now;


        public MainViewModel()
        {
            LoadedCommand = new RelayCommand<EventArgs>(Loaded);
            RenderedCommand = new RelayCommand<EventArgs>(Rendered);
            KeyDownCommand = new RelayCommand<KeyEventArgs>(KeyDown);
            _dialogCoordinator = DialogCoordinator.Instance;
            PlotModels = new PlotModel[3];
        }


        private void Rendered(EventArgs ev)
        {
            RefreshAllCharts();
        }


        private void Loaded(EventArgs ev)
        {
            ParseOptions();
            InitializeWorkloadAnalysis();
            InitializeCharts();
            InitializeFilters();
        }

        private void InitializeWorkloadAnalysis()
        {
            _baselineWorkloadAnalysis = new WorkloadAnalysis() { Name = "Baseline" };
            _baselineWorkloadAnalysis.ConnectionInfo = new SqlConnectionInfo()
            {
                ServerName = _options.BaselineServer,
                DatabaseName = _options.BaselineDatabase,
                SchemaName = _options.BaselineSchema,
                UserName = _options.BaselineUsername,
                Password = _options.BaselinePassword
            };
            _baselineWorkloadAnalysis.Load();

            if(_options.BenchmarkServer != null)
            {
                _benchmarkWorkloadAnalysis = new WorkloadAnalysis() { Name = "Benchmark" };
                _benchmarkWorkloadAnalysis.ConnectionInfo = new SqlConnectionInfo()
                {
                    ServerName = _options.BenchmarkServer,
                    DatabaseName = _options.BenchmarkDatabase,
                    SchemaName = _options.BenchmarkSchema,
                    UserName = _options.BenchmarkUsername,
                    Password = _options.BenchmarkPassword
                };
                _benchmarkWorkloadAnalysis.Load();
            }


            // Initialize the queries
            Queries = new DataTable();
            Queries.Columns.Add(new DataColumn("query_hash", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("query_text", typeof(String)));
            Queries.Columns.Add(new DataColumn("query_normalized", typeof(String)));
            Queries.Columns.Add(new DataColumn("sum_duration_ms", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("sum_duration_ms2", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("diff_sum_duration_ms", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("avg_duration_ms", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("avg_duration_ms2", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("sum_cpu_ms", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("sum_cpu_ms2", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("diff_sum_cpu_ms", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("avg_cpu_ms", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("avg_cpu_ms2", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("sum_reads", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("sum_reads2", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("avg_reads", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("avg_reads2", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("execution_count", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("execution_count2", typeof(Int64)));
            Queries.Columns.Add(new DataColumn("querydetails", typeof(Object)));

            var baseline = from t in _baselineWorkloadAnalysis.Points
                           group t by new
                           {
                               query = t.NormalizedQuery
                           }
                           into grp
                           select new
                           {
                               query = grp.Key.query,
                               sum_duration_ms = grp.Sum(t => t.SumDurationMs),
                               avg_duration_ms = grp.Average(t => t.AvgDurationMs),
                               sum_cpu_ms = grp.Sum(t => t.SumCpuMs),
                               avg_cpu_ms = grp.Average(t => t.AvgCpuMs),
                               sum_reads = grp.Sum(t => t.SumReads),
                               avg_reads = grp.Average(t => t.AvgReads),
                               execution_count = grp.Sum(t => t.ExecutionCount)
                           };

            var benchmark = from t in baseline where false select new { t.query, t.sum_duration_ms, t.avg_duration_ms, t.sum_cpu_ms, t.avg_cpu_ms, t.sum_reads, t.avg_reads, t.execution_count };

            if (_benchmarkWorkloadAnalysis != null)
            {
                benchmark = from t in _benchmarkWorkloadAnalysis.Points
                            group t by new
                            {
                                query = t.NormalizedQuery
                            }
                            into grp
                            select new
                            {
                                query = grp.Key.query,
                                sum_duration_ms = grp.Sum(t => t.SumDurationMs),
                                avg_duration_ms = grp.Average(t => t.AvgDurationMs),
                                sum_cpu_ms = grp.Sum(t => t.SumCpuMs),
                                avg_cpu_ms = grp.Average(t => t.AvgCpuMs),
                                sum_reads = grp.Sum(t => t.SumReads),
                                avg_reads = grp.Average(t => t.AvgReads),
                                execution_count = grp.Sum(t => t.ExecutionCount)
                            };
            }

            foreach(var itm in baseline)
            {
                var newRow = Queries.Rows.Add();
                newRow["query_hash"] = itm.query.Hash;
                newRow["query_text"] = itm.query.ExampleText;
                newRow["query_normalized"] = itm.query.NormalizedText;
                newRow["sum_duration_ms"] = itm.sum_duration_ms;
                newRow["avg_duration_ms"] = itm.avg_duration_ms;
                newRow["sum_cpu_ms"] = itm.sum_cpu_ms;
                newRow["avg_cpu_ms"] = itm.avg_cpu_ms;
                newRow["sum_reads"] = itm.sum_reads;
                newRow["avg_reads"] = itm.avg_reads;
                newRow["execution_count"] = itm.execution_count;

                if (_benchmarkWorkloadAnalysis != null)
                {
                    var itm2 = benchmark.Where(p => p.query.Hash == itm.query.Hash).ToList();

                    newRow["sum_duration_ms2"] = itm2[0].sum_duration_ms;
                    newRow["diff_sum_duration_ms"] = itm2[0].sum_duration_ms - itm.sum_duration_ms;
                    newRow["avg_duration_ms2"] = itm2[0].avg_duration_ms;
                    newRow["sum_cpu_ms2"] = itm2[0].sum_cpu_ms;
                    newRow["diff_sum_cpu_ms"] = itm2[0].sum_cpu_ms - itm.sum_cpu_ms;
                    newRow["avg_cpu_ms2"] = itm2[0].avg_cpu_ms;
                    newRow["sum_reads2"] = itm2[0].sum_reads;
                    newRow["avg_reads2"] = itm2[0].avg_reads;
                    newRow["execution_count2"] = itm2[0].execution_count;
                }

                // attach query details to the row
                newRow["querydetails"] = new QueryDetails(itm.query, _baselineWorkloadAnalysis, _benchmarkWorkloadAnalysis);
            }
            RaisePropertyChanged("Queries");
            RaisePropertyChanged("CompareModeVisibility");
            RaisePropertyChanged("CompareMode");
        }

        private void ParseOptions()
        {
            _options = ((WorkloadViewer.App)App.Current).Options;

            if(_options.ConfigurationFile != null)
            {
                // TODO: read configuration from file
            }
            else
            {
                if(_options.BaselineServer == null || _options.BaselineDatabase == null)
                {
                    // TODO: display dialog
                }
            }

        }

        private void KeyDown(KeyEventArgs e)
        {
            if(e.Key == Key.F5)
            {
                // invoke refresh
            }
        }


        private void RefreshAllCharts()
        {
            RaisePropertyChanged("CpuPlotModel");
            RaisePropertyChanged("DurationPlotModel");
            RaisePropertyChanged("BatchesPlotModel");
        }


        private void InitializeCharts()
        {
            CpuPlotModel = InitializePlotModel();
            CpuPlotModel.Axes[1].Title = "Cpu (ms)";
            CpuPlotModel.Title = "Cpu";
            CpuPlotModel.Series.Add(LoadCpuSeries(_baselineWorkloadAnalysis, OxyColor.Parse("#01B8AA")));
            if(_options.BenchmarkServer != null)
            {
                CpuPlotModel.Series.Add(LoadCpuSeries(_benchmarkWorkloadAnalysis, OxyColor.Parse("#000000")));
            }
            CpuPlotModel.PlotAreaBorderThickness = new OxyThickness(1,0,0,1);
            PlotModels[0] = CpuPlotModel;
            

            DurationPlotModel = InitializePlotModel();
            DurationPlotModel.Axes[1].Title = "Duration (ms)";
            DurationPlotModel.Title = "Duration";
            DurationPlotModel.Series.Add(LoadDurationSeries(_baselineWorkloadAnalysis, OxyColor.Parse("#01B8AA")));
            if (_options.BenchmarkServer != null)
            {
                DurationPlotModel.Series.Add(LoadDurationSeries(_benchmarkWorkloadAnalysis, OxyColor.Parse("#000000")));
            }
            DurationPlotModel.PlotAreaBorderThickness = new OxyThickness(1, 0, 0, 1);
            PlotModels[1] = DurationPlotModel;

            BatchesPlotModel = InitializePlotModel();
            BatchesPlotModel.Axes[1].Title = "Batches/second";
            BatchesPlotModel.Title = "Batches/second";
            BatchesPlotModel.Series.Add(LoadBatchesSeries(_baselineWorkloadAnalysis, OxyColor.Parse("#01B8AA")));
            if (_options.BenchmarkServer != null)
            {
                BatchesPlotModel.Series.Add(LoadBatchesSeries(_benchmarkWorkloadAnalysis, OxyColor.Parse("#000000")));
            }
            BatchesPlotModel.PlotAreaBorderThickness = new OxyThickness(1, 0, 0, 1);
            PlotModels[2] = BatchesPlotModel;
        }


        private PlotModel InitializePlotModel()
        {
            PlotModel plotModel = new PlotModel();
            plotModel.LegendOrientation = LegendOrientation.Horizontal;
            plotModel.LegendPlacement = LegendPlacement.Inside;
            plotModel.LegendPosition = LegendPosition.TopLeft;
            plotModel.LegendBackground = OxyColor.FromAColor(200, OxyColors.White);

            LinearAxis offsetAxis = new LinearAxis() {
                MajorGridlineStyle = LineStyle.Dot,
                MinorGridlineStyle = LineStyle.None,
                Position = AxisPosition.Bottom,
                Title = "Offset minutes",
                AbsoluteMinimum = 0,
                MinorTickSize = 0
            };
            plotModel.Axes.Add(offsetAxis);
            LinearAxis valueAxis1 = new LinearAxis() {
                MajorGridlineStyle = LineStyle.Dot,
                MinorGridlineStyle = LineStyle.None,
                Position = AxisPosition.Left,
                StringFormat = "N0",
                IsZoomEnabled = false,
                AbsoluteMinimum = 0,
                MaximumPadding = 0.2,
                MinorTickSize = 0
            };
            plotModel.Axes.Add(valueAxis1);

            plotModel.PlotMargins = new OxyThickness(70, 0, 0, 30);
            plotModel.Series.Clear();

            foreach (var ax in plotModel.Axes)
            {
                ax.AxisChanged += (sender, e) => SynchronizeCharts(plotModel, sender, e);
            }

            return plotModel;
        }

        private void SynchronizeCharts(PlotModel plotModel, object sender, AxisChangedEventArgs e)
        {
            if (DateTime.Now.Subtract(_lastAxisAdjust).TotalMilliseconds < 100)
            {
                return;
            }
            _lastAxisAdjust = DateTime.Now;

            try
            {

                double xstart = plotModel.DefaultXAxis.ActualMinimum;
                double xend = plotModel.DefaultXAxis.ActualMaximum;

                if (xstart < 0) xstart = 0;

                foreach (var pm in PlotModels)
                {
                    // set x zoom only for the charts not being zoomed
                    if (pm.Title != plotModel.Title)
                    {
                        pm.DefaultXAxis.Zoom(xstart, xend);
                    }
                    pm.InvalidatePlot(true);
                }

            }
            finally
            {
                _lastAxisAdjust = DateTime.Now;
            }
        }

        private Series LoadCpuSeries(WorkloadAnalysis analysis, OxyColor color)
        {
            LineSeries cpuSeries = new LineSeries()
            {
                StrokeThickness = 2,
                MarkerSize = 3,
                MarkerStroke = OxyColor.Parse("#FF0000"), //Red
                MarkerType = MarkerType.None,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "Offset: {2:0}\n{0}: {4:0}",
                Title = analysis.Name,
                Color = color,
                Smooth = false
            };

            var Table = from t in analysis.Points
                        group t by new
                        {
                            offset = t.OffsetMinutes
                        }
                        into grp
                        orderby grp.Key.offset
                        select new
                        {
                            offset_minutes = grp.Key.offset,
                            cpu = grp.Sum(t => t.SumCpuMs)
                        };

            foreach (var p in Table)
            {
                cpuSeries.Points.Add(new DataPoint(p.offset_minutes , p.cpu));
            }

            return cpuSeries;
        }

        private Series LoadDurationSeries(WorkloadAnalysis analysis, OxyColor color)
        {
            LineSeries durationSeries = new LineSeries()
            {
                StrokeThickness = 2,
                MarkerSize = 3,
                MarkerStroke = OxyColor.Parse("#FF0000"), //Red
                MarkerType = MarkerType.None,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "Offset: {2:0}\n{0}: {4:0}",
                Title = analysis.Name,
                Color = color, 
                Smooth = false
            };

            var Table = from t in analysis.Points
                        group t by new
                        {
                            offset = t.OffsetMinutes
                        }
                        into grp
                        orderby grp.Key.offset
                        select new
                        {
                            offset_minutes = grp.Key.offset,
                            duration = grp.Sum(t => t.SumDurationMs)
                        };

            foreach (var p in Table)
            {
                durationSeries.Points.Add(new DataPoint(p.offset_minutes, p.duration));
            }

            return durationSeries;
        }


        private Series LoadBatchesSeries(WorkloadAnalysis analysis, OxyColor color)
        {
            LineSeries batchesSeries = new LineSeries()
            {
                StrokeThickness = 2,
                MarkerSize = 3,
                MarkerStroke = OxyColor.Parse("#FF0000"), //Red
                MarkerType = MarkerType.None,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "Offset: {2:0}\n{0}: {4:0}",
                Title = analysis.Name,
                Color = color,
                Smooth = false
            };


            var Table = from t in analysis.Points
                        group t by new
                        {
                            offset = t.OffsetMinutes
                        }
                        into grp
                        orderby grp.Key.offset
                        select new
                        {
                            offset_minutes = grp.Key.offset,
                            execution_count = grp.Sum(t => t.ExecutionCount / (t.DurationMinutes * 60))
                        };

            foreach (var p in Table)
            {
                batchesSeries.Points.Add(new DataPoint(p.offset_minutes, p.execution_count));
            }

            return batchesSeries;
        }


        private void InitializeFilters()
        {
            ApplicationList = new List<FilterDefinition>();
            RaisePropertyChanged("ApplicationList");
        }

    }
}