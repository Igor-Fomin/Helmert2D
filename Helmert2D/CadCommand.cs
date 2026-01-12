using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Helmert2D.Core;

namespace Helmert2D
{
    public class CadCommand : IExtensionApplication
    {
        public void Initialize()
        {
            // Subscribe to the AssemblyResolve event to help AutoCAD find adjacent DLLs
            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;
        }

        public void Terminate()
        {
            // Unsubscribe when the plugin is unloaded (optional, but good practice)
            AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomain_AssemblyResolve;
        }

        private Assembly? CurrentDomain_AssemblyResolve(object? sender, ResolveEventArgs args)
        {
            // Get the Name of the assembly being requested
            string assemblyName = new AssemblyName(args.Name).Name;

            // Get the folder where our Helmert2D.dll is located
            string assemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            
            // Construct the path to the requested DLL (e.g., MathNet.Numerics.dll)
            string targetPath = Path.Combine(assemblyPath, assemblyName + ".dll");

            // If the file exists, load it
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
                
                // Show the window modally within AutoCAD
                Autodesk.AutoCAD.ApplicationServices.Application.ShowModalWindow(view); 
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }
    }
}