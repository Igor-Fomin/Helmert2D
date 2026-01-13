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
        public event Action? AddPointRequested;
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

        public void AppendPoints(IEnumerable<PointPairViewModel> newPoints)
        {
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
                // Use all points in the list since we disabled empty row addition
                var validPoints = Points.ToList();

                var activePoints = validPoints.Where(p => p.IsActive).ToList();

                if (activePoints.Count < 2)
                {
                    MessageBox.Show("Please define and activate at least 2 control points.", "Insufficient Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var source = activePoints.Select(p => new Point2D(p.SourceX, p.SourceY)).ToList();
                var target = activePoints.Select(p => new Point2D(p.TargetX, p.TargetY)).ToList();

                bool computeScale = ChkComputeScale.IsChecked ?? true;
                _lastResult = HelmertSolver.Solve(source, target, computeScale);

                TxtTransX.Text = _lastResult.TranslationX.ToString("F4");
                TxtTransY.Text = _lastResult.TranslationY.ToString("F4");
                TxtRotation.Text = FormatToDMS(_lastResult.RotationDeg);
                TxtScale.Text = _lastResult.Scale.ToString("F6");
                TxtRMSE.Text = _lastResult.Rmse.ToString("F6");

                // Calculate and update residuals for ALL valid points (even inactive ones, acting as check points)
                foreach (var point in validPoints)
                {
                    double predX = _lastResult.TranslationX + _lastResult.A * point.SourceX - _lastResult.B * point.SourceY;
                    double predY = _lastResult.TranslationY + _lastResult.B * point.SourceX + _lastResult.A * point.SourceY;

                    double rx = predX - point.TargetX;
                    double ry = predY - point.TargetY;

                    point.ResidualX = rx;
                    point.ResidualY = ry;
                    point.ResidualDist = Math.Sqrt(rx * rx + ry * ry);
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
                try
                {
                    BtnApply.IsEnabled = false;
                    bool transformCopy = ChkTransformCopy.IsChecked ?? false;
                    ApplyTransformationRequested?.Invoke(_lastResult, transformCopy);
                }
                finally
                {
                    BtnApply.IsEnabled = true;
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            AddPointRequested?.Invoke();
        }

        private void DeletePoint_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = PointsGrid.SelectedItems.Cast<PointPairViewModel>().ToList();
            if (selectedItems.Any())
            {
                foreach (var item in selectedItems)
                {
                    Points.Remove(item);
                }
            }
        }

        private string FormatToDMS(double decimalDegrees)
        {
            double absDegrees = Math.Abs(decimalDegrees);
            int d = (int)absDegrees;
            double mFull = (absDegrees - d) * 60.0;
            int m = (int)mFull;
            double s = (mFull - m) * 60.0;

            string sign = decimalDegrees < 0 ? "-" : "";
            return $"{sign}{d}° {m}' {s:F2}\"";
        }
    }

    public class PointPairViewModel : INotifyPropertyChanged
    {
        private bool _isActive = true;
        private double _sourceX;
        private double _sourceY;
        private double _targetX;
        private double _targetY;
        private double? _residualX;
        private double? _residualY;
        private double? _residualDist;

        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

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
        public double? ResidualDist
        {
            get => _residualDist;
            set { _residualDist = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}