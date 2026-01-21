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
        private static HelmertView? _view;

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
                var assemblyName = new AssemblyName(args.Name).Name;
                if (string.IsNullOrEmpty(assemblyName)) return null;

                string? assemblyLoc = typeof(CadCommand).Assembly.Location;
                string? assemblyPath = null;

                if (!string.IsNullOrEmpty(assemblyLoc))
                {
                    assemblyPath = Path.GetDirectoryName(assemblyLoc);
                }
                else
                {
                    assemblyPath = @"D:\Visual Studio Projects\Helmert2D\Helmert2D\bin\x64\Debug\net8.0-windows";
                }

                if (string.IsNullOrEmpty(assemblyPath)) return null;

                string targetPath = Path.Combine(assemblyPath, assemblyName + ".dll");

                if (File.Exists(targetPath))
                {
                    return Assembly.LoadFrom(targetPath);
                }
            }
            catch
            {
                return null;
            }

            return null;
        }

        [CommandMethod("Helmert2D")]
        public void RunHelmertTool()
        {
            try
            {
                if (_view != null && _view.IsLoaded)
                {
                    _view.Show();
                    _view.Activate();
                    return;
                }

                _view = new HelmertView();
                _view.Closed += (s, e) => _view = null;

                _view.PickPointsRequested += () => PickPoints(_view, append: false);
                _view.AddPointRequested += () => PickPoints(_view, append: true);
                _view.ApplyTransformationRequested += (result, transformCopy) => ApplyTransformation(result, transformCopy);

                Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(_view);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error launching tool: {ex.Message}\n\n{ex.StackTrace}");
            }
        }

        private void PickPoints(HelmertView view, bool append)
        {
            view.Hide();

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            var pickedPoints = new List<PointPairViewModel>();

            try
            {
                while (true)
                {
                    var pPtOptsSource = new PromptPointOptions(append ? "\nPick Additional Source Point (or ESC to finish): " : "\nPick Source Point (or ESC to finish): ");
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
                if (append)
                {
                    view.AppendPoints(pickedPoints);
                }
                else
                {
                    view.UpdatePoints(pickedPoints);
                }
                view.Show();
                Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
            }
        }

        private void ApplyTransformation(HelmertResult result, bool transformCopy)
        {
            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();

            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            var db = doc.Database;

            var pSelOpts = new PromptSelectionOptions();
            pSelOpts.MessageForAdding = "\nSelect objects to transform: ";
            var pSelRes = ed.GetSelection(pSelOpts);

            if (pSelRes.Status != PromptStatus.OK)
                return;

            // Create affine transform matrix with Z-scale fixed at 1.0 to prevent Z-scaling drift
            double[] matData = new double[] {
                result.A, -result.B, 0,            result.TranslationX,
                result.B,  result.A, 0,            result.TranslationY,
                0,         0,        1.0,          0,
                0,         0,        0,            1
            };

            var mat = new Matrix3d(matData);

            // Create uniform transform matrix as a fallback for entities that don't support non-uniform scaling
            double[] matDataUniform = new double[] {
                result.A, -result.B, 0,            result.TranslationX,
                result.B,  result.A, 0,            result.TranslationY,
                0,         0,        result.Scale, 0,
                0,         0,        0,            1
            };

            var matUniform = new Matrix3d(matDataUniform);

            using (doc.LockDocument())
            {
                int count = 0;
                try
                {
                    using (Transaction tr = doc.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord? btr = null;
                        if (transformCopy)
                        {
                            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                            btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                        }

                        foreach (SelectedObject so in pSelRes.Value)
                        {
                            Entity? ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                            if (ent != null)
                            {
                                string typeName = ent.GetType().Name;
                                bool isSurface = typeName.Contains("Surface") && !typeName.Contains("Label");
                                
                                // Civil 3D entities (CogoPoints, Alignments, etc.) should generally NOT be cloned via standard CAD cloning
                                // as this creates ID conflicts or duplicates that don't behave correctly. 
                                bool isCivilEntity = typeName.Contains("CogoPoint") || 
                                                     typeName.Contains("Alignment") || 
                                                     typeName.Contains("FeatureLine") || 
                                                     typeName.Contains("Corridor") || 
                                                     typeName.Contains("Pipe") || 
                                                     typeName.Contains("Structure");

                                // Avoid cloning Surfaces (DTM) or complex Civil objects that shouldn't be duplicated
                                // If it's a Civil Entity, we force MOVE (shouldCopy = false)
                                bool shouldCopy = transformCopy && btr != null && !isSurface && !isCivilEntity;

                                Entity targetEnt;
                                if (shouldCopy)
                                {
                                    targetEnt = (Entity)ent.Clone();
                                    btr!.AppendEntity(targetEnt);
                                    tr.AddNewlyCreatedDBObject(targetEnt, true);
                                }
                                else
                                {
                                    targetEnt = ent;
                                    targetEnt.UpgradeOpen();
                                }

                                // Special handling for Line entities (slanted lines need both ends fixed)
                                if (targetEnt is Line line)
                                {
                                    double oldStartZ = line.StartPoint.Z;
                                    double oldEndZ = line.EndPoint.Z;

                                    try
                                    {
                                        targetEnt.TransformBy(mat);
                                    }
                                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                                    {
                                        if (ex.ErrorStatus == ErrorStatus.CannotScaleNonUniformly)
                                            targetEnt.TransformBy(matUniform);
                                        else if (ex.ErrorStatus == ErrorStatus.NotApplicable)
                                        { /* Ignore labels/entities that refuse transform */ }
                                        else throw;
                                    }

                                    // Explicitly restore Z for both ends to preserve slope/elevation
                                    line.StartPoint = new Point3d(line.StartPoint.X, line.StartPoint.Y, oldStartZ);
                                    line.EndPoint = new Point3d(line.EndPoint.X, line.EndPoint.Y, oldEndZ);

                                    count++;
                                    continue; // Skip generic correction
                                }

                                // Capture original Z for point-based objects
                                double? originalZ = GetElevation(targetEnt);

                                try
                                {
                                    targetEnt.TransformBy(mat);
                                }
                                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                                {
                                    if (ex.ErrorStatus == ErrorStatus.CannotScaleNonUniformly)
                                    {
                                        // Fallback to uniform scaling if non-uniform is not supported
                                        targetEnt.TransformBy(matUniform);
                                    }
                                    else if (ex.ErrorStatus == ErrorStatus.NotApplicable)
                                    {
                                        // Ignore entities that refuse transformation (e.g., dynamic labels tied to parent)
                                        // Do not increment count if we didn't do anything? Or count it as handled?
                                        // Let's count it as processed to avoid confusion.
                                    }
                                    else
                                    {
                                        throw;
                                    }
                                }

                                // Restore Z if it drifted (Capture-Transform-Restore)
                                if (originalZ.HasValue)
                                {
                                    double? newZ = GetElevation(targetEnt);
                                    if (newZ.HasValue && Math.Abs(newZ.Value - originalZ.Value) > 1e-6)
                                    {
                                        var correction = Matrix3d.Displacement(new Vector3d(0, 0, originalZ.Value - newZ.Value));
                                        try 
                                        {
                                            targetEnt.TransformBy(correction);
                                        } 
                                        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (ex.ErrorStatus == ErrorStatus.NotApplicable) 
                                        { /* Ignore */ }
                                    }
                                }

                                count++;
                            }
                        }
                        tr.Commit();
                    }

                    // Secondary transaction to update Civil 3D Point Groups (Fix for spatial index/ghosting)
                    try
                    {
                        using (Transaction tr2 = doc.TransactionManager.StartTransaction())
                        {
                            // Resolve CivilDocument dynamically or via reference to support Civil 3D environment
                            var civilDoc = Autodesk.Civil.ApplicationServices.CivilDocument.GetCivilDocument(db);
                            var allPointsId = civilDoc.PointGroups.AllPointsPointGroupId;
                            var allPointsGroup = tr2.GetObject(allPointsId, OpenMode.ForWrite) as Autodesk.Civil.DatabaseServices.PointGroup;
                            
                            if (allPointsGroup != null)
                            {
                                allPointsGroup.Update();
                            }
                            tr2.Commit();
                        }
                    }
                    catch
                    {
                        // Ignore if not running in Civil 3D or if Civil entities were not involved
                    }

                    ed.Regen();
                    ed.WriteMessage($"\nSuccessfully transformed {count} objects" + (transformCopy ? " (Copies created)." : ".") + "\n");
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nError during transformation: {ex.Message}\n");
                    // Transaction is implicitly aborted if not committed when disposed
                }
            }

            Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
        }

        private double? GetElevation(Entity ent)
        {
            // Point-based
            if (ent is BlockReference br) return br.Position.Z;
            if (ent is DBPoint pt) return pt.Position.Z;
            if (ent is Circle c) return c.Center.Z;
            if (ent is DBText txt) return txt.Position.Z;
            if (ent is MText mtxt) return mtxt.Location.Z;

            // Linear / Curve
            if (ent is Line line) return line.StartPoint.Z;
            if (ent is Polyline pl) return pl.Elevation; // LWPolyline
            if (ent is Polyline2d pl2d) return pl2d.Elevation;
            if (ent is Polyline3d pl3d)
            {
                // For 3D Polyline, accessing vertices requires transaction and opening objects.
                // Keeping it simple for now as 2D Helmert is the focus.
                return null;
            }
            if (ent is Arc arc) return arc.Center.Z;
            if (ent is Ellipse el) return el.Center.Z;

            // Check for Civil 3D CogoPoint using dynamic typing
            if (ent.GetType().Name == "CogoPoint")
            {
                try
                {
                    dynamic cogo = ent;
                    return (double)cogo.Location.Z;
                }
                catch
                {
                    return null;
                }
            }

            // Generic fallback for any entity with bounds (Surfaces, Feature Lines, Alignments, Labels, etc.)
            // This ensures we capture the vertical position of complex objects.
            if (ent.Bounds.HasValue)
            {
                return ent.Bounds.Value.MinPoint.Z;
            }

            return null;
        }
    }
}
