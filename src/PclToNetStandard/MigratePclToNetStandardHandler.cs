using System;
using System.Threading;
using System.Threading.Tasks;
using MonoDevelop.Components.Commands;
using MonoDevelop.Core.Logging;
using MonoDevelop.Ide;
using MonoDevelop.Projects;
using MonoDevelop.Ide.Gui;
using MonoDevelop.Ide.Gui.Content;
using System.IO;
using System.Xml;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using MonoDevelop.Core;
using MonoDevelop.Core.ProgressMonitoring;

namespace PclToNetStandard
{
    //https://github.com/dotnet/docs/blob/master/docs/core/tools/csproj.md

    public class MigratePclToNetStandardHandler : CommandHandler
    {
        public MigratePclToNetStandardHandler()
        {
        }
        /*
        void RefreshProjectReferences()
        {
            foreach (DotNetProject dotNetProject in solution.GetAllDotNetProjects())
            {
                dotNetProject.RefreshReferenceStatus();
            }
        }*/

        protected override void Run()
        {
            lock(this)
            {
                if (IsEnabled())
                {
                    var solution = IdeApp.ProjectOperations.CurrentSelectedSolution;
                    var project = (IdeApp.ProjectOperations.CurrentSelectedProject as DotNetProject);

                    var solutionPath = solution.FileName.ParentDirectory;
                    var projectPath = project.FileName.ParentDirectory;

                    var packages = UpdatePackageConfigFile(projectPath).ToArray();
                    UpdateProjectFile(packages, projectPath.ToString());
                    RemoveAssemblyInfoFile(project.FileName);

                    Runtime.RunInMainThread(async () => {
                        var sol = (Solution) await Services.ProjectService.ReadWorkspaceItem(GetMonitor(), solution.FileName.FullPath.ToString());
                        project = sol.GetAllItems<DotNetProject>().FirstOrDefault();
                        await project.SaveAsync(GetMonitor());
                        await solution.SaveAsync(GetMonitor());
                    });

                }
            }
        }

        protected override void Update(CommandInfo info)
        {
            if (IsEnabled())
            {
                info.Enabled = true;
                info.Bypass = false;
            }
            else
            {
                info.Enabled = false;
                info.Bypass = true;
            }
        }

        protected virtual bool IsEnabled()
        {
            return IsPclProjectSelected();
        }

        protected bool IsPclProjectSelected()
        {
            var project = (IdeApp.ProjectOperations.CurrentSelectedProject as DotNetProject);
            if (project == null)
                return false;

            return project.IsPortableLibrary;
        }

        #region Custom Handler Logic

        private IEnumerable<Models.Package> UpdatePackageConfigFile(MonoDevelop.Core.FilePath solutionPath)
        {
            var packageConfigFilePath = String.Empty;
            FindRecursive(solutionPath.ParentDirectory.ToString(), "packages.config", out packageConfigFilePath);

            var packages = XElement.Load(packageConfigFilePath).Elements("package");
            var root = packages.FirstOrDefault()?.Parent;
            foreach (var package in packages) 
            {
                var packageModel = new Models.Package() 
                { 
                    Id = package.Attribute("id").Value,
                    TargetFramework = package.Attribute("targetFramework").Value,
                    Version = package.Attribute("version").Value,
                };

                yield return packageModel;
            }

            File.Delete(packageConfigFilePath);
        }

        private void RemoveAssemblyInfoFile(MonoDevelop.Core.FilePath projectFilePath)
        {
            var assemblyInfoFilePath = projectFilePath.ParentDirectory.ToString() + "/Properties/AssemblyInfo.cs";

            if (File.Exists(assemblyInfoFilePath))
            {
                File.Delete(assemblyInfoFilePath);
            }
        }

        private void UpdateProjectFile(Models.Package[] packages, MonoDevelop.Core.FilePath projectFilePath)
        {
            var fallBackString = $@"<PropertyGroup>
                                    <TargetFramework>netstandard2.0</TargetFramework>
                                       <AssetTargetFallback>{GetFallbackPlatforms(packages)}</AssetTargetFallback>
                                     </PropertyGroup>";
            
            var xmlString = $@"<?xml version=""1.0"" encoding=""utf-8""?>
                                                    <Project Sdk = ""Microsoft.NET.Sdk"" >
                                                    <PropertyGroup>
                                                        <TargetFramework>netstandard2.0</TargetFramework>
                                                    </PropertyGroup>{fallBackString}
                                                    <ItemGroup>";




            var packageReferences = String.Empty;
            var packagesArray = packages.ToArray();
            foreach (var package in packagesArray)
            {
                packageReferences += $"<PackageReference Include=\"{package.Id}\" Version=\"{package.Version}\" />";
            }


            xmlString = xmlString + packageReferences + "</ItemGroup></Project>";
            
            var currentItem = IdeApp.ProjectOperations.CurrentSelectedSolution.Items.FirstOrDefault(x => x.FileName.ParentDirectory.ToString() == projectFilePath.ToString());
            var csprojPath = currentItem.FileName.ToString();

            File.Delete(csprojPath);
            File.WriteAllText(csprojPath, xmlString);
        }

        private void FindRecursive(string dir, string fileToFind, out string path)
        {
            path = string.Empty;
            foreach (var d in Directory.GetDirectories(dir).Where(x => x != "." && x != ".."))
            {
                foreach (var f in Directory.GetFiles(d))
                {
                    if (f.EndsWith("/" + fileToFind, StringComparison.InvariantCultureIgnoreCase))
                    {
                        path = f;
                        return;
                    }
                }
                if (String.IsNullOrWhiteSpace(path))
                    FindRecursive(d, fileToFind, out path);
            }
        }

        private string GetFallbackPlatforms(Models.Package[] packages)
        {
            return String.Join(";", packages.Select(x => x.TargetFramework).Distinct());
        }

        public static ProgressMonitor GetMonitor()
        {
            ConsoleProgressMonitor m = new ConsoleProgressMonitor();
            m.IgnoreLogMessages = true;
            return m;
        }

        #endregion 
    }
}
