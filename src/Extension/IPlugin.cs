namespace Vellum.Extension
{
    using System;
    using System.Collections.Generic;

    public interface IPlugin
    {
        public delegate void HookHandler(object sender, EventArgs e);

        // public Version Version { get; }
        public PluginType PluginType { get; }

        public void Initialize(IHost host);

        public void Unload();

        public void RegisterHook(byte id, HookHandler callback);

        public Dictionary<byte, string> GetHooks();
        // public Dictionary<string, object> GetDefaultRunConfig();
    }

    public enum PluginType
    {
        INTERNAL,

        EXTERNAL,
    }

    public class HookEventArgs : EventArgs
    {
        public object Attachment = null;
    }
}
