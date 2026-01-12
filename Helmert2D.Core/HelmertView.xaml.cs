using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Helmert2D.Core
{
    public partial class HelmertView : Window
    {
        public ObservableCollection<PointPairViewModel> Points { get; set; }

        public event Action? PickPointsRequested;
        public event Action<HelmertResult, bool>? ApplyTransformationRequested;

        private HelmertResult? _lastResult;

        public HelmertView()
        {
            InitializeComponent();
            Points = new ObservableCollection<PointPairViewModel>();
            PointsGrid.ItemsSource = Points;
        }

        public void UpdatePoints(IEnumerable<PointPairViewModel> newPoints)
        {
            Points.Clear();
            foreach (var p in newPoints)
            {
                Points.Add(p);
            }
        }

        private void BtnPick_Click(object sender, RoutedEventArgs e)
        {
            PickPointsRequested?.Invoke();
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Filter out empty lines (0,0 -> 0,0) which can happen if user adds a row in DataGrid but doesn't fill it
                var validPoints = Points
                    .Where(p => Math.Abs(p.SourceX) > 0.000001 || Math.Abs(p.SourceY) > 0.000001)
                    .ToList();

                if (validPoints.Count < 2)
                {
                    MessageBox.Show("Please define at least 2 control points.", "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var source = validPoints.Select(p => new Point2D(p.SourceX, p.SourceY)).ToList();
                var target = validPoints.Select(p => new Point2D(p.TargetX, p.TargetY)).ToList();

                bool computeScale = ChkComputeScale.IsChecked ?? true;
                _lastResult = HelmertSolver.Solve(source, target, computeScale);

                TxtTransX.Text = _lastResult.TranslationX.ToString("F4");
                TxtTransY.Text = _lastResult.TranslationY.ToString("F4");
                TxtRotation.Text = _lastResult.RotationDeg.ToString("F6");
                TxtScale.Text = _lastResult.Scale.ToString("F6");
                TxtRMSE.Text = _lastResult.Rmse.ToString("F6");

                // Calculate and update residuals for each point
                foreach (var point in validPoints)
                {
                    double predX = _lastResult.TranslationX + _lastResult.A * point.SourceX - _lastResult.B * point.SourceY;
                    double predY = _lastResult.TranslationY + _lastResult.B * point.SourceX + _lastResult.A * point.SourceY;

                    point.ResidualX = predX - point.TargetX;
                    point.ResidualY = predY - point.TargetY;
                }

                BtnApply.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Calculation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnApply.IsEnabled = false;
            }
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (_lastResult != null)
            {
                bool transformCopy = ChkTransformCopy.IsChecked ?? false;
                ApplyTransformationRequested?.Invoke(_lastResult, transformCopy);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class PointPairViewModel : INotifyPropertyChanged
    {
        private double _sourceX;
        private double _sourceY;
        private double _targetX;
        private double _targetY;
        private double? _residualX;
        private double? _residualY;

        public double SourceX
        {
            get => _sourceX;
            set { _sourceX = value; OnPropertyChanged(); }
        }
        public double SourceY
        {
            get => _sourceY;
            set { _sourceY = value; OnPropertyChanged(); }
        }
        public double TargetX
        {
            get => _targetX;
            set { _targetX = value; OnPropertyChanged(); }
        }
        public double TargetY
        {
            get => _targetY;
            set { _targetY = value; OnPropertyChanged(); }
        }

        public double? ResidualX
        {
            get => _residualX;
            set { _residualX = value; OnPropertyChanged(); }
        }
        public double? ResidualY
        {
            get => _residualY;
            set { _residualY = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}