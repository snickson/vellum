namespace Vellum.Automation
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;

    using Vellum.Extension;

    public class RenderManager : Manager
    {
        public RunConfiguration RunConfig;

        private readonly ProcessManager _bds;

        private Process _renderer;

        public RenderManager(ProcessManager p, RunConfiguration runConfig)
        {
            _bds = p;
            RunConfig = runConfig;
        }

        public void Start(string worldPath)
        {
            Processing = true;

            // Send tellraw message 1/2
            _bds.SendTellraw("Rendering map...");

            Log($"{Tag}Initializing map rendering...");

            CallHook((byte)Hook.BEGIN);

            // Create temporary copy of latest backup to initiate render on
            var prfx = "_";
            var tempPathCopy = worldPath.Replace(Path.GetFileName(worldPath), prfx + Path.GetFileName(worldPath));
            BackupManager.CopyDirectory(worldPath, tempPathCopy);

            // Prepare map render output directory
            if (!Directory.Exists(RunConfig.Renders.PapyrusOutputPath))
            {
                Directory.CreateDirectory(RunConfig.Renders.PapyrusOutputPath);
            }

            for (var i = 0; i < RunConfig.Renders.PapyrusTasks.Length; i++)
            {
                var placeholderReplacements = new Dictionary<string, string>
                {
                    { "$WORLD_PATH", $"\"{tempPathCopy}\"" },
                    { "$OUTPUT_PATH", $"\"{RunConfig.Renders.PapyrusOutputPath}\"" },
                    { "${WORLD_PATH}", $"\"{tempPathCopy}\"" },
                    { "${OUTPUT_PATH}", $"\"{RunConfig.Renders.PapyrusOutputPath}\"" },
                };

                var args = RunConfig.Renders.PapyrusGlobalArgs;

                foreach (var kv in placeholderReplacements)
                    args = args.Replace(kv.Key, kv.Value);

                _renderer = new Process();
                _renderer.StartInfo.FileName = RunConfig.Renders.PapyrusBinPath;
                _renderer.StartInfo.WorkingDirectory = Path.GetDirectoryName(RunConfig.Renders.PapyrusBinPath);
                _renderer.StartInfo.Arguments = $"{args} {RunConfig.Renders.PapyrusTasks[i]}";
                _renderer.StartInfo.RedirectStandardOutput = RunConfig.HideStdout;
                _renderer.StartInfo.RedirectStandardInput = true;

                Log($"{Tag}{Indent}Rendering map {i + 1}/{RunConfig.Renders.PapyrusTasks.Length}...");

                // To pre-emptively start a process with defined priority you need to set calling process to said priority.
                var parentProcess = Process.GetCurrentProcess();
                var parentPriority = parentProcess.PriorityClass;
                if (RunConfig.Renders.LowPriority)
                {
                    parentProcess.PriorityClass = ProcessPriorityClass.Idle;
                }

                _renderer.Start();

                CallHook((byte)Hook.NEXT, new HookEventArgs { Attachment = i });

                if (RunConfig.Renders.LowPriority)
                {
                    // Set back parent process to original priority
                    parentProcess.PriorityClass = parentPriority;
                }

                _renderer.WaitForExit();
            }

            Log($"{Tag}{Indent}Cleaning up...");

            Directory.Delete(tempPathCopy, true);

            Log(string.Format("{0}Rendering done!", Tag, Indent));

            // Send tellraw message 2/2
            _bds.SendTellraw("Done rendering!");

            CallHook((byte)Hook.END);

            Processing = false;
        }

        public bool Abort()
        {
            var result = false;
            if (_renderer != null)
            {
                _renderer.Kill();
                result = true;
            }
            else
            {
                result = false;
            }

            CallHook((byte)Hook.ABORT, new HookEventArgs { Attachment = result });

            return result;
        }

        #region PLUGIN

        public Version Version { get; }

        public enum Hook
        {
            BEGIN,

            ABORT,

            NEXT,

            END,
        }

        #endregion
    }
}
