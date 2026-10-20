using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TapTap.Editor
{
    public sealed class ReleaseBuild : IPostprocessBuildWithReport
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64
                || (report.summary.options & BuildOptions.Development) != 0) return;
            string output = Path.GetFullPath(report.summary.outputPath);
            string directory = Path.GetDirectoryName(output);
            string name = Path.GetFileNameWithoutExtension(output);
            foreach (string suffix in new[] { "_BurstDebugInformation_DoNotShip", "_BackUpThisFolder_ButDontShipItWithYourGame" })
            {
                string debugDirectory = Path.Combine(directory, name + suffix);
                if (!Directory.Exists(debugDirectory)) continue;
                string symbols = Path.GetFullPath(Path.Combine("Library", "ReleaseDebugSymbols", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(symbols);
                Directory.Move(debugDirectory, Path.Combine(symbols, Path.GetFileName(debugDirectory)));
            }
        }

        // -executeMethod TapTap.Editor.ReleaseBuild.BuildBatch -releaseOutput <absolute exe path>
        public static void BuildBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-releaseOutput");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing -releaseOutput");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = Path.GetFullPath(args[index + 1]),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            Debug.Log("RELEASE BUILD: " + report.summary.result + "; errors=" + report.summary.totalErrors
                + "; warnings=" + report.summary.totalWarnings);
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
