using MathNet.Numerics.LinearAlgebra;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Helmert2D
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
        /// <summary>
        /// Solves for 4-parameter Helmert Transformation (Translation X, Translation Y, Rotation, Scale).
        /// </summary>
        /// <param name="sourcePoints">List of points in the source system.</param>
        /// <param name="targetPoints">List of points in the target system (must match count of source).</param>
        /// <returns>Transformation parameters.</returns>
        public static HelmertResult Solve(List<Point2D> sourcePoints, List<Point2D> targetPoints)
        {
            if (sourcePoints == null || targetPoints == null)
                throw new ArgumentNullException("Points lists cannot be null.");
            
            if (sourcePoints.Count != targetPoints.Count)
                throw new ArgumentException("Source and Target point counts must match.");

            if (sourcePoints.Count < 2)
                throw new ArgumentException("At least 2 common points are required for a unique solution.");

            int n = sourcePoints.Count;
            var M = Matrix<double>.Build;
            var V = Vector<double>.Build;

            // Design Matrix A: (2n x 4)
            // [ x  -y   1   0 ]
            // [ y   x   0   1 ]
            double[,] aData = new double[2 * n, 4];
            double[] lData = new double[2 * n];

            for (int i = 0; i < n; i++)
            {
                double sx = sourcePoints[i].X;
                double sy = sourcePoints[i].Y;
                double tx = targetPoints[i].X;
                double ty = targetPoints[i].Y;

                // First equation for X
                aData[2 * i, 0] = sx;     // a * x
                aData[2 * i, 1] = -sy;    // - b * y
                aData[2 * i, 2] = 1.0;    // Tx
                aData[2 * i, 3] = 0.0;    // 0

                lData[2 * i] = tx;

                // Second equation for Y
                aData[2 * i + 1, 0] = sy; // b * x (Note: coefficient of 'a' is y, coefficient of 'b' is x? Wait.
                                          // Y = Ty + b*x + a*y
                                          // Y = a*y + b*x + Ty
                                          // Matrix order is [a, b, Tx, Ty]
                                          // Coeff of a is y. Coeff of b is x.
                                          
                aData[2 * i + 1, 0] = sy; // a * y
                aData[2 * i + 1, 1] = sx; // b * x
                aData[2 * i + 1, 2] = 0.0;
                aData[2 * i + 1, 3] = 1.0; // Ty

                lData[2 * i + 1] = ty;
            }

            var A = M.DenseOfArray(aData);
            var L = V.Dense(lData);

            // Solve normal equations: (A'A)^-1 A'L
            // Or use QR decomposition which is numerically more stable
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
            double mse = residuals.DotProduct(residuals) / (2 * n); // Mean Squared Error
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
    }
}
