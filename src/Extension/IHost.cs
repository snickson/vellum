namespace Vellum.Extension
{
    using System;
    using System.Collections.Generic;

    public interface IHost
    {
        public RunConfiguration RunConfig { get; }

        public string PluginDirectory { get; }

        public T LoadPluginConfiguration<T>(Type type);

        public void SetPluginDirectory(string dir);

        public uint LoadPlugins();

        public void AddPlugin(IPlugin plugin);

        public List<IPlugin> GetPlugins();

        public IPlugin GetPluginByName(string name);
    }
}
