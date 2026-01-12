using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Helmert2D
{
    public partial class HelmertView : Window
    {
        public ObservableCollection<PointPairViewModel> Points { get; set; }

        public HelmertView()
        {
            InitializeComponent();
            Points = new ObservableCollection<PointPairViewModel>
            {
                new PointPairViewModel { SourceX = 0, SourceY = 0, TargetX = 10, TargetY = 10 },
                new PointPairViewModel { SourceX = 100, SourceY = 0, TargetX = 110, TargetY = 10 },
                new PointPairViewModel { SourceX = 0, SourceY = 100, TargetX = 10, TargetY = 110 }
            };
            PointsGrid.ItemsSource = Points;
        }

        private void BtnCalculate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var validPoints = Points.Where(p => true).ToList(); // Get all
                
                // Construct lists
                var source = validPoints.Select(p => new Point2D(p.SourceX, p.SourceY)).ToList();
                var target = validPoints.Select(p => new Point2D(p.TargetX, p.TargetY)).ToList();

                var result = HelmertSolver.Solve(source, target);

                TxtTransX.Text = result.TranslationX.ToString("F4");
                TxtTransY.Text = result.TranslationY.ToString("F4");
                TxtRotation.Text = result.RotationDeg.ToString("F6");
                TxtScale.Text = result.Scale.ToString("F6");
                TxtRMSE.Text = result.Rmse.ToString("F6");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Calculation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
