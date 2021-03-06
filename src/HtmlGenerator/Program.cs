﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.SourceBrowser.Common;

namespace Microsoft.SourceBrowser.HtmlGenerator
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            var projects = new List<string>();
            foreach (var arg in args)
            {
                if (arg.StartsWith("/out:"))
                {
                    Paths.SolutionDestinationFolder = arg.Substring("/out:".Length).StripQuotes();
                    continue;
                }

                try
                {
                    var project = Path.GetFullPath(arg);
                    if (File.Exists(project))
                    {
                        if (project.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                            project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
                            project.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                        {
                            projects.Add(project.StripQuotes());
                        }
                    }
                }
                catch
                {
                }
            }

            if (projects.Count == 0)
            {
                PrintUsage();
                return;
            }

            AssertTraceListener.Register();
            AppDomain.CurrentDomain.FirstChanceException += FirstChanceExceptionHandler.HandleFirstChanceException;

            if (Paths.SolutionDestinationFolder == null)
            {
                Paths.SolutionDestinationFolder = Path.Combine(Microsoft.SourceBrowser.Common.Paths.BaseAppFolder, "Index");
            }

            Log.ErrorLogFilePath = Path.Combine(Paths.SolutionDestinationFolder, Log.ErrorLogFile);
            Log.MessageLogFilePath = Path.Combine(Paths.SolutionDestinationFolder, Log.MessageLogFile);

            // Warning, this will delete and recreate your destination folder
            Paths.PrepareDestinationFolder();

            using (Disposable.Timing("Generating website"))
            {
                IndexSolutions(projects);
                FinalizeProjects();
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"Usage: HtmlGenerator [/out:<outputdirectory>] <pathtosolution1.csproj|vbproj|sln> [more solutions/projects..]");
        }

        private static readonly Folder<Project> mergedSolutionExplorerRoot = new Folder<Project>();

        private static void IndexSolutions(IEnumerable<string> solutionFilePaths)
        {
            var solutionGenerators = new List<SolutionGenerator>();
            var assemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in solutionFilePaths)
            {
                using (Disposable.Timing("Loading " + path))
                {
                    var solutionGenerator = new SolutionGenerator(
                        path,
                        Paths.SolutionDestinationFolder);
                    solutionGenerators.Add(solutionGenerator);
                    foreach (var assemblyName in solutionGenerator.GetAssemblyNames())
                    {
                        assemblyNames.Add(assemblyName);
                    }
                }
            }

            foreach (var solutionGenerator in solutionGenerators)
            {
                using (Disposable.Timing("Generating " + solutionGenerator.ProjectFilePath))
                {
                    solutionGenerator.GlobalAssemblyList = assemblyNames;
                    solutionGenerator.Generate(solutionExplorerRoot: mergedSolutionExplorerRoot);
                }
            }
        }

        private static void FinalizeProjects()
        {
            GenerateLooseFilesProject(Constants.MSBuildFiles, Paths.SolutionDestinationFolder);
            GenerateLooseFilesProject(Constants.TypeScriptFiles, Paths.SolutionDestinationFolder);
            using (Disposable.Timing("Finalizing references"))
            {
                try
                {
                    var solutionFinalizer = new SolutionFinalizer(Paths.SolutionDestinationFolder);
                    solutionFinalizer.FinalizeProjects(mergedSolutionExplorerRoot);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, "Failure while finalizing projects");
                }
            }
        }

        private static void GenerateLooseFilesProject(string projectName, string solutionDestinationPath)
        {
            var projectGenerator = new ProjectGenerator(projectName, solutionDestinationPath);
            projectGenerator.GenerateNonProjectFolder();
        }
    }
}
