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
            try
            {
                // 1. Robustly get the assembly name we are looking for
                var assemblyName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(assemblyName)) return null;

                // 2. CRITICAL FIX: Use typeof(CadCommand).Assembly instead of GetExecutingAssembly()
                // This ensures we get the location of THIS dll, not the generic runtime.
                string? assemblyLoc = typeof(CadCommand).Assembly.Location;
                string? assemblyPath = null;

                if (!string.IsNullOrEmpty(assemblyLoc))
                {
                    assemblyPath = Path.GetDirectoryName(assemblyLoc);
                }
                else
                {
                    // Fallback for in-memory loading (e.g. DevLoader)
                    // If Location is empty, we assume we are running from the debug build folder.
                    assemblyPath = @"D:\Visual Studio Projects\Helmert2D\Helmert2D\bin\x64\Debug\net8.0-windows";
                }

                // 3. Safety check: If for some reason we can't find our own location, stop.
                if (string.IsNullOrEmpty(assemblyPath)) return null;

                // 4. Combine paths
                string targetPath = Path.Combine(assemblyPath, assemblyName + ".dll");

                if (File.Exists(targetPath))
                {
                    return Assembly.LoadFrom(targetPath);
                }
            }
            catch
            {
                // Never throw an exception inside an AssemblyResolver, it crashes the app.
                return null;
            }

            return null;
        }

        [CommandMethod("Helmert2D")]
        public void RunHelmertTool()
        {
            try
            {
                HelmertView view = new HelmertView();

                view.PickPointsRequested += () => PickPoints(view);
                view.ApplyTransformationRequested += (result, transformCopy) => ApplyTransformation(result, transformCopy);

                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(view);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error launching tool: {ex.Message}\n\n{ex.StackTrace}");
            }
        }

        private void PickPoints(HelmertView view)
        {
            view.Hide();

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pickedPoints = new List<PointPairViewModel>();

            try
            {
                while (true)
                {
                    var pPtOptsSource = new PromptPointOptions("\nPick Source Point (or ESC to finish): ");
                    pPtOptsSource.AllowNone = true;
                    var pPtResSource = ed.GetPoint(pPtOptsSource);

                    if (pPtResSource.Status == PromptStatus.Cancel || pPtResSource.Status == PromptStatus.None)
                        break;

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

                    ed.DrawVector(pPtResSource.Value, pPtResTarget.Value, 1, false);
                }
            }
            finally
            {
                view.UpdatePoints(pickedPoints);
                view.Show();
            }
        }

        private void ApplyTransformation(HelmertResult result, bool transformCopy)
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            var pSelOpts = new PromptSelectionOptions();
            pSelOpts.MessageForAdding = "\nSelect objects to transform: ";
            var pSelRes = ed.GetSelection(pSelOpts);

            if (pSelRes.Status != PromptStatus.OK)
                return;

            double[] matData = new double[] {
                result.A, -result.B, 0, result.TranslationX,
                result.B,  result.A, 0, result.TranslationY,
                0,         0,        1, 0,
                0,         0,        0, 1
            };

            var mat = new Matrix3d(matData);

            using (doc.LockDocument())
            using (Transaction tr = doc.TransactionManager.StartTransaction())
            {
                try
                {
                    BlockTableRecord? btr = null;
                    if (transformCopy)
                    {
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                    }

                    int count = 0;
                    foreach (SelectedObject so in pSelRes.Value)
                    {
                        Entity? ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent != null)
                        {
                            if (transformCopy && btr != null)
                            {
                                Entity clone = (Entity)ent.Clone();
                                btr.AppendEntity(clone);
                                tr.AddNewlyCreatedDBObject(clone, true);
                                clone.TransformBy(mat);
                            }
                            else
                            {
                                ent.UpgradeOpen();
                                ent.TransformBy(mat);
                            }
                            count++;
                        }
                    }
                    tr.Commit();
                    ed.Regen();
                    ed.WriteMessage($"\nSuccessfully transformed {count} objects" + (transformCopy ? " (Copies created)." : ".") + "\n");
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