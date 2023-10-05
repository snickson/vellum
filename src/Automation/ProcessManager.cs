namespace Vellum.Automation
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using System.Timers;

    public class ProcessManager : Manager
    {
        public delegate void MatchHandler(object sender, MatchedEventArgs e);

        private ProcessStartInfo _startInfo;

        private readonly List<string> _ignorePatterns = new List<string>();

        private string _lastMessage = "";

        private string _pattern;

        private string _matchedText;

        private readonly Dictionary<string, MatchHandler> _matchHandlers = new Dictionary<string, MatchHandler>();

        ///<param name="startInfo">Start configuration for the process.</param>
        public ProcessManager(ProcessStartInfo startInfo)
        {
            _startInfo = startInfo;
            Process = new Process();
            Process.StartInfo = startInfo;
            Process.StartInfo.RedirectStandardInput = true;
            Process.StartInfo.RedirectStandardOutput = true;
            Process.StartInfo.UseShellExecute = false;
            Process.OutputDataReceived += OutputTextReceived;
        }

        /// <summary>Starts the underlying process and begins reading it's output.</summary>
        /// <param name="startInfo">Start configuration for the process.</param>
        /// <param name="ignoreMessages">
        ///     Array of messages that should not be redirected when written to the underlying processes
        ///     stdout.
        /// </param>
        public ProcessManager(ProcessStartInfo startInfo, string[] ignorePatterns)
            : this(startInfo)
        {
            _ignorePatterns = new List<string>(ignorePatterns);
        }

        public Process Process { get; }

        public bool HasMatched { get; private set; }

        public bool EnableConsoleOutput { get; set; } = true;

        public bool IsRunning
        {
            get
            {
                bool result;
                try
                {
                    result = Process.HasExited ? false : true;
                }
                catch
                {
                    result = false;
                }

                return result;
            }
        }

        ///<summary>Executes a custom command in the operating systems shell.</summary>
        ///<param name="cmd">Command to execute.</param>
        public static void RunCustomCommand(string cmd)
        {
            var shell = "";
            var args = "";
            switch ((int)Environment.OSVersion.Platform)
            {
                case 0:
                case 1:
                case 2:
                case 3:
                    shell = @"C:\Windows\System32\cmd.exe";
                    args = $"/C \"{cmd}\"";
                    break;
                case 4:
                    shell = "/bin/bash";
                    args = $"-c \"{cmd}\"";
                    break;
            }

            var p = new Process();
            p.StartInfo.FileName = shell;
            p.StartInfo.Arguments = args;

            p.Start();
            p.WaitForExit();
        }

        ///<summary>Starts the underlying process and begins reading it's output.</summary>
        public bool Start()
        {
            if (Process.Start())
            {
                Process.BeginOutputReadLine();
                return true;
            }

            return false;
        }

        ///<summary>Frees the underlying process.</summary>
        public void Close()
        {
            Process.CancelOutputRead();
            Process.Close();
        }

        ///<summary>Sends a command to the underlying processes stdin and executes it.</summary>
        ///<param name="cmd">Command to execute.</param>
        public void SendInput(string cmd)
        {
            Process.StandardInput.Write(cmd + "\n");
        }

        ///<summary>Halt program flow until the specified regex pattern has matched in the underlying processes <c>stdout</c>.</summary>
        ///<param name="pattern">The regex pattern to match</param>
        public void WaitForMatch(string pattern)
        {
            WaitForMatch(pattern, 0);
        }

        /// <summary>
        ///     Halt program flow until the specified regex pattern has matched in the underlying processes <c>stdout</c>
        ///     during the span of the given timeout.
        /// </summary>
        /// <param name="pattern">The regex pattern to match</param>
        /// <param name="timeout">Timeout in milliseconds (0 or less for no timeout)</param>
        public void WaitForMatch(string pattern, double timeout)
        {
            var ready = false;
            var timedOut = false;
            var count = -1;

            if (timeout > 0)
            {
                var timeoutTimer = new Timer(timeout);
                timeoutTimer.AutoReset = false;
                timeoutTimer.Elapsed += (sender, e) => timedOut = true;
                timeoutTimer.Start();
            }

            while (!ready && !timedOut)
            {
                count = Regex.Matches(_lastMessage, pattern).Count;
                ready = count >= 1 ? true : false;
            }
        }

        public void SetMatchPattern(string pattern)
        {
            HasMatched = false;
            _pattern = pattern;
        }

        public void AddIgnorePattern(string pattern)
        {
            _ignorePatterns.Add(pattern);
        }

        /// <summary>
        ///     Registers a function which should be called upon a matching stdout line given by the specified pattern.
        ///     MatchHandlers should be registered before the process is started to avoid InvalidOperationException exceptions.
        /// </summary>
        public void RegisterMatchHandler(string pattern, MatchHandler handler)
        {
            if (!_matchHandlers.ContainsKey(pattern))
                _matchHandlers.Add(pattern, handler);
            else
                _matchHandlers[pattern] += handler;
        }

        public string GetMatchedText()
        {
            return _matchedText;
        }

        public void SendTellraw(string message)
        {
            if (IsRunning && Host != null && !Host.RunConfig.QuietMode)
            {
#if !IS_LIB
                message = Regex.Replace(message, @"§", "\\u00a7");
                SendInput("tellraw @a {\"rawtext\":[{\"text\":\"\\u00a7l[VELLUM]\"},{\"text\":\"\\u00a7r " + message + "\"}]}");
#endif
            }
        }

        private void OutputTextReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _lastMessage = e.Data;

                if (!HasMatched && _pattern != null)
                {
                    if (Regex.Matches(e.Data, _pattern).Count >= 1)
                    {
                        HasMatched = true;
                        _pattern = null;
                        _matchedText = e.Data;
                    }
                }

                // Custom match handlers
                if (_matchHandlers.Count > 0)
                {
                    foreach (var key in _matchHandlers.Keys)
                    {
                        var matches = Regex.Matches(e.Data, key);
                        if (matches.Count > 0)
                            _matchHandlers[key].Invoke(this, new MatchedEventArgs { Matches = matches });
                    }
                }

                if (EnableConsoleOutput)
                {
                    var showMsg = true;

                    if (_ignorePatterns.Count > 0)
                    {
                        foreach (var pattern in _ignorePatterns)
                        {
                            if (!string.IsNullOrWhiteSpace(e.Data) && Regex.Matches(e.Data, pattern).Count > 0)
                            {
                                showMsg = false;
                                break;
                            }
                        }
                    }

                    if (showMsg)
                        Console.WriteLine(e.Data);
                }
            }
        }
    }

    public class MatchedEventArgs : EventArgs
    {
        public MatchCollection Matches;
    }
}
