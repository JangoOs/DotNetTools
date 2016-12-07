// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Tools.Internal;

namespace Microsoft.Extensions.SecretManager.Tools.Internal
{
    public class ProjectIdResolver : IDisposable
    {
        private const string TargetsFileName = "FindUserSecretsProperty.targets";
        private const string DefaultConfig = "Debug";
        private readonly IReporter _reporter;
        private readonly string _workingDirectory;
        private readonly List<string> _tempFiles = new List<string>();

        public ProjectIdResolver(IReporter reporter, string workingDirectory)
        {
            _workingDirectory = workingDirectory;
            _reporter = reporter;
        }

        public string Resolve(string project, string configuration)
        {
            var finder = new MsBuildProjectFinder(_workingDirectory);
            var projectFile = finder.FindMsBuildProject(project);

            _reporter.Verbose(Resources.FormatMessage_Project_File_Path(projectFile));

            var targetFile = GetTargetFile();
            var outputFile = Path.GetTempFileName();
            _tempFiles.Add(outputFile);

            configuration = !string.IsNullOrEmpty(configuration)
                ? configuration
                : DefaultConfig;

            var args = new[]
            {
                "msbuild",
                targetFile,
                "/nologo",
                "/t:_FindUserSecretsProperty",
                $"/p:Project={projectFile}",
                $"/p:OutputFile={outputFile}",
                $"/p:Configuration={configuration}"
            };
            var psi = new ProcessStartInfo
            {
                FileName = DotNetMuxer.MuxerPathOrDefault(),
                Arguments = ArgumentEscaper.EscapeAndConcatenate(args),
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

#if DEBUG
            _reporter.Verbose($"Invoking '{psi.FileName} {psi.Arguments}'");
#endif

            var process = Process.Start(psi);
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                _reporter.Verbose(process.StandardOutput.ReadToEnd());
                _reporter.Verbose(process.StandardError.ReadToEnd());
                throw new InvalidOperationException(Resources.FormatError_ProjectFailedToLoad(projectFile));
            }

            var id = File.ReadAllText(outputFile)?.Trim();
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException(Resources.FormatError_ProjectMissingId(projectFile));
            }

            return id;
        }

        private string GetTargetFile()
        {
            var assemblyDir = Path.GetDirectoryName(GetType().GetTypeInfo().Assembly.Location);

            // targets should be in one of these locations, depending on test setup and tools installation
            var searchPaths = new[]
            {
                AppContext.BaseDirectory,
                assemblyDir, // next to assembly
                Path.Combine(assemblyDir, "../../toolassets"), // inside the nupkg
                Path.Combine(assemblyDir, "toolassets"), // for local builds
                Path.Combine(AppContext.BaseDirectory, "../../toolassets"), // relative to packaged deps.json
            };

            return searchPaths
                .Select(dir => Path.Combine(dir, TargetsFileName))
                .Where(File.Exists)
                .First();
        }

        public void Dispose()
        {
            foreach (var file in _tempFiles)
            {
                TryDelete(file);
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // whatever
            }
        }
    }
}