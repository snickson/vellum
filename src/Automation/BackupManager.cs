namespace Vellum.Automation
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Text.RegularExpressions;
    using System.Threading;

    using Vellum.Extension;

    public class BackupManager : Manager
    {
        private readonly ProcessManager _bds;

        private RunConfiguration _runConfig;

        public BackupManager(ProcessManager p, RunConfiguration runConfig)
        {
            _bds = p;
            _runConfig = runConfig;
        }

        /// <summary>
        ///     Time in milliseconds to wait until sending next <code>save query</code> command to <code>ProcessManager</code>
        ///     's process
        /// </summary>
        public int QueryTimeout { get; set; } = 500;

        /// <summary>
        ///     Compresses a world as a .zip archive to the <code>destinationPath</code> directory and optionally deletes old
        ///     backups.
        /// </summary>
        /// <param name="sourcePath">World to archive</param>
        /// <param name="destinationPath">
        ///     Directory to save archive in (archives will be named like this:
        ///     <code>yyyy-MM-dd_HH-mm_WORLDNAME.zip</code>)
        /// </param>
        /// <param name="archivesToKeep">
        ///     Threshold for archives to keep, archives that exceed this threshold will be deleted,
        ///     <code>-1</code> to not remove any archives
        /// </param>
        public static bool Archive(string sourcePath, string destinationPath, int archivesToKeep)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                Log("Could not create archive because an invalid destination path was specified");
                return false;
            }

            if (!Directory.Exists(destinationPath))
            {
                Directory.CreateDirectory(destinationPath);
            }

            var archiveName = $"{DateTime.Now:yyyy-MM-dd_HH-mm}_{Path.GetFileName(sourcePath)}.zip";
            var archivePath = Path.Join(destinationPath, archiveName);

            bool result;
            if (!File.Exists(archivePath))
            {
                try
                {
                    ZipFile.CreateFromDirectory(sourcePath, archivePath, CompressionLevel.Optimal, false);
                    result = true;
                }
                catch
                {
                    Log($"Could not create archive \"{archiveName}\"!");
                    result = false;
                }
            }
            else
            {
                Log($"Could not create archive \"{archiveName}\" because it already exists!");
                result = false;
            }

            if (archivesToKeep == -1)
            {
                return result;
            }

            // Delete older backups if threshold of archives to keep has been exceeded
            var files = Directory.GetFiles(destinationPath);
            var creationTimes = new DateTime[files.Length];

            for (var i = 0; i < files.Length; i++)
            {
                creationTimes[i] = File.GetCreationTime(files[i]);
            }

            Array.Sort(files, creationTimes);

            if (files.Length <= archivesToKeep)
            {
                return result;
            }

            for (uint i = 0; i < Math.Abs(archivesToKeep - files.Length); i++)
            {
                // System.Console.WriteLine("Deleting: {0}", files[i]);
                try
                {
                    File.Delete(files[i]);
                }
                catch
                {
                    Log($"Could not delete {files[i]}");
                }
            }

            return result;
        }

        ///<summary>Restores an archived backup.</summary>
        public static bool Restore(string archivePath, string destinationPath)
        {
            if (string.IsNullOrEmpty(destinationPath))
            {
                Log("Could not restore because an invalid destination path was specified");
                return false;
            }

            if (File.Exists(archivePath) && (Path.GetExtension(archivePath) == ".zip"))
            {
                if (Directory.Exists(destinationPath))
                {
                    Console.WriteLine("Creating precautionary backup of current world...");
                    var restoreBackupPath = Path.Join(Directory.GetCurrentDirectory(), VellumHost.TempPath, "restore");
                    var currentWorldBackupPath = Path.Join(restoreBackupPath, Path.GetFileName(destinationPath));
                    CopyDirectory(destinationPath, currentWorldBackupPath);

                    if (Archive(currentWorldBackupPath, restoreBackupPath, -1))
                    {
                        Console.WriteLine("A PRECAUTIONARY BACKUP OF YOUR CURRENT WORLD HAS BEEN ARCHIVED TO:\n" + Path.GetFullPath(restoreBackupPath));

                        Console.WriteLine("Deleting current world...");
                        Directory.Delete(currentWorldBackupPath, true);
                        Directory.Delete(destinationPath, true);
                    }
                }
                else
                {
                    Console.WriteLine($"Could not find directory of current world \"{Path.GetFileName(destinationPath)}\", skipping precautionary backup...");
                }

                Console.WriteLine("Restoring world from archive...");
                ZipFile.ExtractToDirectory(archivePath, destinationPath);

                Console.WriteLine("Successfully restored backup!");

                return true;
            }

            Console.WriteLine("Could not restore backup because specified archive does not exist!");

            Console.WriteLine("Failed to restore backup!");

            return false;
        }

        ///<summary>Copies an existing directory.</summary>
        ///<param name="sourceDir">Directory to copy</param>
        ///<param name="targetDir">Directory to create and populate with files</param>
        public static void CopyDirectory(string sourceDir, string targetDir)
        {
            // Create root directory
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var sourceFiles = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);

            foreach (var sFile in sourceFiles)
            {
                var tFile = sFile.Replace(sourceDir, targetDir);

                // Create sub-directory if needed
                var subDir = Path.GetDirectoryName(tFile);
                if (!Directory.Exists(subDir))
                {
                    Directory.CreateDirectory(subDir);
                }

                File.Copy(sFile, tFile, true);
            }
        }

        /// <summary>
        ///     Creates a copy of a world and attempts to archive it as a compressed .zip-archive in the <code>archivePath</code>
        ///     directory.
        /// </summary>
        /// <param name="worldPath">Path to the world to copy.</param>
        /// <param name="destinationPath">Path to copy the world to.</param>
        /// <param name="fullCopy">
        ///     Whether to copy the whole world directory instead of just the updated files. The server must not
        ///     be running for a full copy.
        /// </param>
        /// <param name="archive">Whether to archive the backup as a compressed .zip-file.</param>
        public void CreateWorldBackup(string worldPath, string destinationPath, bool fullCopy, bool archive)
        {
            Processing = true;

            CallHook((byte)Hook.BEGIN);

            #region PRE EXEC

            if (!string.IsNullOrWhiteSpace(_runConfig.Backups.PreExec))
            {
                Log($"{Tag}Executing pre-command...");
                ProcessManager.RunCustomCommand(_runConfig.Backups.PreExec);
            }

            #endregion

            Log($"{Tag}Creating backup...");
            // Send tellraw message 1/2
            _bds.SendTellraw("Creating backup...");

            // Shutdown server and take full backup
            if (_runConfig.Backups.StopBeforeBackup && _bds.IsRunning)
            {
                _bds.SendInput("stop");
                _bds.Process.WaitForExit();
                _bds.Close();
            }

            if (fullCopy || _runConfig.Backups.StopBeforeBackup)
            {
                if (Directory.Exists(destinationPath))
                {
                    Log($"{Tag}{Indent}Clearing local world backup directory...\t");

                    Directory.Delete(destinationPath, true);
                    Directory.CreateDirectory(destinationPath);
                }
                else
                {
                    Directory.CreateDirectory(destinationPath);
                }

                if (Directory.Exists(worldPath))
                {
                    Log($"{Tag}{Indent}Creating full world backup...\t");

                    CopyDirectory(worldPath, destinationPath);
                }
                else
                {
                    Log($"{Tag}{Indent}Invalid world directory. Could not create full world backup!");
                }
            }
            else
            {
                Log($"{Tag}{Indent}Holding world saving...");

                CallHook((byte)Hook.SAVE_HOLD);

                _bds.SendInput("save hold");
                _bds.SetMatchPattern("(" + Path.GetFileName(worldPath) + @"\/)");

                while (!_bds.HasMatched)
                {
                    _bds.SendInput("save query");
                    Thread.Sleep(QueryTimeout);
                }

                var fileListRegex = new Regex("(" + Path.GetFileName(worldPath) + @"\/.+?)\:{1}(\d+)");
                var matches = fileListRegex.Matches(_bds.GetMatchedText());

                var sourceFiles = new string[matches.Count, 2];

                for (var i = 0; i < matches.Count; i++)
                {
                    sourceFiles[i, 0] = matches[i].Groups[1].Value.Replace(Path.GetFileName(worldPath), "");
                    sourceFiles[i, 1] = matches[i].Groups[2].Value;
                    // Console.WriteLine($"File: {sourceFiles[i, 0]}, Bytes: {sourceFiles[i, 1]}");
                }

                Log($"{Tag}{Indent}Copying {sourceFiles.GetLength(0)} files... ");
                try
                {
                    // ACTUAL COPYING BEGINS HERE
                    for (uint i = 0; i < sourceFiles.GetLength(0); i++)
                    {
                        // As of Bedrock Server 1.14, the queried files list doesn't include the "/db/" path, but to take precaution for future versions check if the "/db/" part is present 
                        // The last 3 files always seem to be the world metadata which need to be copied into the worlds root directory instead of the "db"-subdirectory (this only matters if the "/db/" part isn't available in the queried files list)
                        var subDir = (Regex.Match(sourceFiles[i, 0], @"(\/db\/)").Captures.Count < 1) && (i < sourceFiles.GetLength(0) - 3) ? "/db" : "";
                        var filePath = Path.Join(worldPath, subDir, sourceFiles[i, 0]);
                        var targetPath = Path.Join(destinationPath, subDir, sourceFiles[i, 0]);

                        // Console.WriteLine($"Old:\t{sourceFiles[i, 0]}\nNew:\t{filePath}");
                        //Console.WriteLine(Regex.Match(sourceFiles[i, 0], @"(\/db\/)").Captures.Count);
                        //Console.WriteLine("\"{0}\" -> \"{1}\"", filePath, targetPath);

                        var targetDirectory = Path.GetDirectoryName(targetPath);
                        // Console.WriteLine($"Target folder: {targetDirectory}");
                        if (!Directory.Exists(targetDirectory) && !string.IsNullOrEmpty(targetDirectory))
                        {
                            Directory.CreateDirectory(targetDirectory);
                        }

                        using var sourceStream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var targetStream = File.Open(targetPath, FileMode.Create, FileAccess.Write);
                        // Console.WriteLine("Copying: {0}", filePath);

                        // Read bytes until truncate indicator
                        for (var j = 0; j < Convert.ToInt32(sourceFiles[i, 1]); j++)
                        {
                            targetStream.WriteByte((byte)sourceStream.ReadByte());
                        }

                        targetStream.Flush();
                        // Console.WriteLine("Flushed stream");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }

                #region FILE INTEGRITY CHECK

                Console.WriteLine("Verifying file-integrity");
                Log($"{Tag}{Indent}Verifying file-integrity... ");

                try
                {
                    var sourceDbFiles = Directory.GetFiles(worldPath + "/db/");
                    var targetDbFiles = Directory.GetFiles(destinationPath + "/db/");

                    foreach (var tFile in targetDbFiles)
                    {
                        // Console.WriteLine($"Checking {Path.GetFileName(tFile)}");
                        var found = false;
                        if (Array.Exists(sourceDbFiles, sFile => Path.GetFileName(tFile) == Path.GetFileName(sFile)))
                        {
                            // Console.WriteLine($"Found {Path.GetFileName(tFile)}");
                            found = true;
                        }

                        // File isn't in the source world directory anymore, delete!
                        if (found)
                        {
                            continue;
                        }

                        Console.WriteLine("Deleting file \"{0}\"...", tFile);
                        File.Delete(tFile);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }

                #endregion

                Log($"{Tag}{Indent}Resuming world saving...");

                CallHook((byte)Hook.SAVE_RESUME, new HookEventArgs { Attachment = sourceFiles });

                _bds.SendInput("save resume");
                _bds.WaitForMatch("(Changes to the world are resumed.)");
            }

            var tellrawMsg = "Finished creating backup!";

            // Archive
            if (archive)
            {
                CallHook((byte)Hook.ARCHIVE);

                Log($"{Tag}{Indent}Archiving world backup...");
                if (Archive(destinationPath, _runConfig.Backups.ArchivePath, _runConfig.Backups.BackupsToKeep))
                {
                    Log($"{Tag}{Indent}Archiving done!");
                }
                else
                {
                    Log($"{Tag}{Indent}Archiving failed!");
                    tellrawMsg = "Could not archive backup!";
                }
            }

            // Send tellraw message 2/2
            _bds.SendTellraw(tellrawMsg);

            Log($"{Tag}Backup done!");

            if (_runConfig.Backups.StopBeforeBackup && !_bds.IsRunning)
            {
                _bds.Start();
                _bds.WaitForMatch(CommonRegex.ServerStarted);
            }

            #region POST EXEC

            if (!string.IsNullOrWhiteSpace(_runConfig.Backups.PostExec))
            {
                Log($"{Tag}Executing post-command...");
                ProcessManager.RunCustomCommand(_runConfig.Backups.PostExec);
            }

            #endregion

            CallHook((byte)Hook.END);

            Processing = false;
        }

        #region PLUGIN

        public Version Version { get; }

        public enum Hook
        {
            BEGIN,

            SAVE_HOLD,

            SAVE_RESUME,

            COPY,

            COPY_END,

            INTEGRITY,

            INTEGRITY_END,

            ARCHIVE,

            END,
        }

        #endregion
    }
}
