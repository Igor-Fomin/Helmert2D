using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Helmert2D.Core
{
    public partial class HelmertView : Window
    {
        public ObservableCollection<PointPairViewModel> Points { get; set; }
        
        public event Action? PickPointsRequested;
        public event Action<HelmertResult>? ApplyTransformationRequested;

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
                var validPoints = Points.Where(p => true).ToList(); // Get all
                
                // Construct lists
                var source = validPoints.Select(p => new Point2D(p.SourceX, p.SourceY)).ToList();
                var target = validPoints.Select(p => new Point2D(p.TargetX, p.TargetY)).ToList();

                bool computeScale = ChkComputeScale.IsChecked ?? true;
                _lastResult = HelmertSolver.Solve(source, target, computeScale);

                TxtTransX.Text = _lastResult.TranslationX.ToString("F4");
                TxtTransY.Text = _lastResult.TranslationY.ToString("F4");
                TxtRotation.Text = _lastResult.RotationDeg.ToString("F6");
                TxtScale.Text = _lastResult.Scale.ToString("F6");
                TxtRMSE.Text = _lastResult.Rmse.ToString("F6");

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
                ApplyTransformationRequested?.Invoke(_lastResult);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }

    public class PointPairViewModel
    {
        public double SourceX { get; set; }
        public double SourceY { get; set; }
        public double TargetX { get; set; }
        public double TargetY { get; set; }
    }
}
