using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Helmert2D.Core
{
    public class Point2D
    {
        public double X { get; set; }
        public double Y { get; set; }

        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }
    }

    public class HelmertResult
    {
        public double TranslationX { get; set; }
        public double TranslationY { get; set; }
        public double Scale { get; set; }
        public double RotationRad { get; set; }
        public double RotationDeg => RotationRad * (180.0 / Math.PI);
        public double Rmse { get; set; }

        // Parameters a and b where a = s*cos(alpha), b = s*sin(alpha)
        public double A { get; set; }
        public double B { get; set; }
    }

    public static class HelmertSolver
    {
        public static HelmertResult Solve(List<Point2D> sourcePoints, List<Point2D> targetPoints, bool computeScale = true)
        {
            if (sourcePoints == null || targetPoints == null)
                throw new ArgumentNullException("Points lists cannot be null.");

            if (sourcePoints.Count != targetPoints.Count)
                throw new ArgumentException("Source and Target point counts must match.");

            if (sourcePoints.Count < 2)
                throw new ArgumentException("At least 2 common points are required for a unique solution.");

            int n = sourcePoints.Count;

            if (computeScale)
            {
                var M = Matrix<double>.Build;
                var V = Vector<double>.Build;

                // Design Matrix A: (2n x 4)
                double[,] aData = new double[2 * n, 4];
                double[] lData = new double[2 * n];

                for (int i = 0; i < n; i++)
                {
                    double sx = sourcePoints[i].X;
                    double sy = sourcePoints[i].Y;
                    double tx = targetPoints[i].X;
                    double ty = targetPoints[i].Y;

                    // Row 2*i (X equation)
                    aData[2 * i, 0] = sx;     // a * x
                    aData[2 * i, 1] = -sy;    // - b * y
                    aData[2 * i, 2] = 1.0;    // Tx
                    aData[2 * i, 3] = 0.0;    // 0
                    lData[2 * i] = tx;

                    // Row 2*i+1 (Y equation)
                    aData[2 * i + 1, 0] = sy; // a * y
                    aData[2 * i + 1, 1] = sx; // b * x
                    aData[2 * i + 1, 2] = 0.0;
                    aData[2 * i + 1, 3] = 1.0; // Ty
                    lData[2 * i + 1] = ty;
                }

                var A = M.DenseOfArray(aData);
                var L = V.Dense(lData);

                var x = A.Solve(L);

                double a = x[0];
                double b = x[1];
                double transX = x[2];
                double transY = x[3];

                double scale = Math.Sqrt(a * a + b * b);
                double rotation = Math.Atan2(b, a);

                // Calculate Residuals and RMSE
                var predicted = A * x;
                var residuals = predicted - L;
                double mse = residuals.DotProduct(residuals) / (2 * n);
                double rmse = Math.Sqrt(mse);

                return new HelmertResult
                {
                    TranslationX = transX,
                    TranslationY = transY,
                    Scale = scale,
                    RotationRad = rotation,
                    A = a,
                    B = b,
                    Rmse = rmse
                };
            }
            else
            {
                // Fixed Scale = 1.0 (Rigid Body Transformation)
                double meanSx = sourcePoints.Average(p => p.X);
                double meanSy = sourcePoints.Average(p => p.Y);
                double meanTx = targetPoints.Average(p => p.X);
                double meanTy = targetPoints.Average(p => p.Y);

                double num = 0.0;
                double den = 0.0;

                for (int i = 0; i < n; i++)
                {
                    double sx = sourcePoints[i].X - meanSx;
                    double sy = sourcePoints[i].Y - meanSy;
                    double tx = targetPoints[i].X - meanTx;
                    double ty = targetPoints[i].Y - meanTy;

                    num += (sx * ty - sy * tx);
                    den += (sx * tx + sy * ty);
                }

                double rotation = Math.Atan2(num, den);
                double a = Math.Cos(rotation);
                double b = Math.Sin(rotation);

                double transX = meanTx - (a * meanSx - b * meanSy);
                double transY = meanTy - (b * meanSx + a * meanSy);

                double sumSqRes = 0;
                for (int i = 0; i < n; i++)
                {
                    double sx = sourcePoints[i].X;
                    double sy = sourcePoints[i].Y;
                    double tx = targetPoints[i].X;
                    double ty = targetPoints[i].Y;

                    double predX = transX + a * sx - b * sy;
                    double predY = transY + b * sx + a * sy;

                    double dx = predX - tx;
                    double dy = predY - ty;
                    sumSqRes += (dx * dx + dy * dy);
                }

                return new HelmertResult
                {
                    TranslationX = transX,
                    TranslationY = transY,
                    Scale = 1.0,
                    RotationRad = rotation,
                    A = a,
                    B = b,
                    Rmse = Math.Sqrt(sumSqRes / (2 * n))
                };
            }
        }
    }
}