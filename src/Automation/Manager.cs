namespace Vellum.Automation;

using Vellum.Extension;

public abstract class Manager : InternalPlugin
{
    protected static string Tag = "[         VELLUM         ] ";

    protected static string Indent = "\t-> ";

    public bool Processing { get; protected set; }

    protected static void Log(string text)
    {
#if !IS_LIB
            System.Console.WriteLine(text);
#endif
    }
}
