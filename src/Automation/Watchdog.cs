namespace Vellum.Automation
{
    using System;

    using Vellum.Extension;

    public class Watchdog : InternalPlugin
    {
        public uint RetryLimit = 3;

        private uint _failRetryCount;

        private bool _enable = true;

        public Watchdog(ProcessManager processManager)
        {
            processManager.Process.EnableRaisingEvents = true;
            processManager.Process.Exited += (sender, e) =>
            {
                if (processManager.Process.ExitCode != 0 && _enable)
                {
                    CallHook((byte)Hook.CRASH, new HookEventArgs { Attachment = processManager.Process.ExitCode });

                    processManager.Close();

                    Console.WriteLine("BDS process unexpectedly exited");

                    if (++_failRetryCount <= RetryLimit)
                    {
                        Console.WriteLine($"Retry #{_failRetryCount} to start BDS process");
                        processManager.Start();
                        CallHook((byte)Hook.RETRY, new HookEventArgs { Attachment = _failRetryCount });
                    }
                    else
                    {
                        Console.WriteLine("Maximum retry limit reached!");
                        CallHook((byte)Hook.LIMIT_REACHED, new HookEventArgs { Attachment = _failRetryCount });
                    }
                }
            };

            processManager.RegisterMatchHandler(
                CommonRegex.ServerStarted,
                (sender, e) =>
                {
                    CallHook((byte)Hook.STABLE, new HookEventArgs { Attachment = _failRetryCount });
                    _failRetryCount = 0;
                });
        }

        public void Disable()
        {
            _enable = false;
        }

        public void Enable()
        {
            _enable = true;
        }

        #region PLUGIN

        public Version Version { get; }

        public enum Hook
        {
            CRASH,

            RETRY,

            LIMIT_REACHED,

            STABLE,
        }

        #endregion
    }
}
