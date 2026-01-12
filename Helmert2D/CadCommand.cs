using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Helmert2D.Core;

namespace Helmert2D
{
    public class CadCommand : IExtensionApplication
    {
        public void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        public void Terminate()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
        }


        private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
        {
            // Get the name of the missing assembly (e.g., "Helmert2D.Core")
            string assemblyName = new AssemblyName(args.Name).Name;

            // Get the folder where Helmert2D.dll is currently running from
            string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            // Combine them to find the missing DLL
            string targetPath = Path.Combine(assemblyPath, assemblyName + ".dll");

            // Debugging: If you have a debugger attached, this helps see what's failing
            // System.Diagnostics.Debug.WriteLine($"Looking for: {targetPath}");

            if (File.Exists(targetPath))
            {
                return Assembly.LoadFrom(targetPath);
            }

            return null;
        }
        [CommandMethod("Helmert2D")]
        public void RunHelmertTool()
        {
            try
            {
                HelmertView view = new HelmertView();

                // Wire up events
                view.PickPointsRequested += () => PickPoints(view);
                view.ApplyTransformationRequested += (result) => ApplyTransformation(result);

                // Open as Modal. 
                // Note: When we "Hide" inside PickPoints, we must "Show" to bring it back, not "ShowDialog".
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(view);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void PickPoints(HelmertView view)
        {
            // Hide the window so we can interact with the drawing
            view.Hide();

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pickedPoints = new List<PointPairViewModel>();

            try
            {
                while (true)
                {
                    // Pick Source
                    var pPtOptsSource = new PromptPointOptions("\nPick Source Point (or ESC to finish): ");
                    pPtOptsSource.AllowNone = true;
                    var pPtResSource = ed.GetPoint(pPtOptsSource);

                    if (pPtResSource.Status == PromptStatus.Cancel || pPtResSource.Status == PromptStatus.None)
                        break;

                    // Pick Target
                    var pPtOptsTarget = new PromptPointOptions("\nPick Target Point: ");
                    pPtOptsTarget.UseBasePoint = true;
                    pPtOptsTarget.BasePoint = pPtResSource.Value;
                    var pPtResTarget = ed.GetPoint(pPtOptsTarget);

                    if (pPtResTarget.Status == PromptStatus.Cancel)
                        break;

                    pickedPoints.Add(new PointPairViewModel
                    {
                        SourceX = pPtResSource.Value.X,
                        SourceY = pPtResSource.Value.Y,
                        TargetX = pPtResTarget.Value.X,
                        TargetY = pPtResTarget.Value.Y
                    });

                    // Draw a temporary vector to visualize the pair
                    ed.DrawVector(pPtResSource.Value, pPtResTarget.Value, 1, false);
                }
            }
            finally
            {
                view.UpdatePoints(pickedPoints);

                // FIXED: Use Show() because the window is already initialized as modal
                view.Show();
            }
        }

        private void ApplyTransformation(HelmertResult result)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            // Prompt for selection
            var pSelOpts = new PromptSelectionOptions();
            pSelOpts.MessageForAdding = "\nSelect objects to transform: ";
            var pSelRes = ed.GetSelection(pSelOpts);

            if (pSelRes.Status != PromptStatus.OK)
                return;

            // Construct Transformation Matrix
            // [ A  -B   0  Tx ]
            // [ B   A   0  Ty ]
            // [ 0   0   1   0 ]
            // [ 0   0   0   1 ]

            double[] matData = new double[] {
                result.A, -result.B, 0, result.TranslationX,
                result.B,  result.A, 0, result.TranslationY,
                0,         0,        1, 0,
                0,         0,        0, 1
            };

            var mat = new Matrix3d(matData);

            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                try
                {
                    foreach (SelectedObject so in pSelRes.Value)
                    {
                        Entity? ent = tr.GetObject(so.ObjectId, OpenMode.ForWrite) as Entity;
                        if (ent != null)
                        {
                            ent.TransformBy(mat);
                        }
                    }
                    tr.Commit();
                    ed.Regen();
                    ed.WriteMessage($"\nSuccessfully transformed {pSelRes.Value.Count} objects.\n");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nError during transformation: {ex.Message}\n");
                    tr.Abort();
                }
            }
        }
    }
}