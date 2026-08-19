using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace Advanced_Combat_Tracker
{
    public static class StubEntryPoint
    {
        [STAThread]
        public static void Main() { }
    }

    public interface IActPluginV1
    {
        void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText);
        void DeInitPlugin();
    }

    public delegate void LogLineEventDelegate(bool isImport, LogLineEventArgs logInfo);

    public sealed class LogLineEventArgs : EventArgs
    {
        public int detectedType;
        public string originalLogLine;
    }

    public sealed class ActPluginData
    {
        public FileInfo pluginFile;
        public object pluginObj;
    }

    public sealed class FormActMain
    {
        public List<ActPluginData> ActPlugins = new List<ActPluginData>();
        public event LogLineEventDelegate OnLogLineRead;
        public void WriteExceptionLog(Exception exception, string message) { }
    }

    public static class ActGlobals
    {
        public static FormActMain oFormActMain = new FormActMain();
    }
}
