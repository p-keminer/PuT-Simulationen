using System.IO;
using System.Windows;

namespace PuT3;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var model = new Bench();
        bool selfTest = args is ["--self-test", _, ..];
        using var server = new DaqServer(model, selfTest ? 0 : DaqServer.Port);
        if (selfTest)
        {
            try
            {
                _ = new Application();
                File.WriteAllText(args[1], SelfTest.Run(model, server, args.Length >= 3 ? args[2] : null));
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(args[1], ex.ToString()); return 1; }
        }
        if (args is ["--bench-test-server", var scenario])
        {
            model.Fixture();
            if (scenario == "open-bnc") model.Cables[0] = model.Cables[0] with { Fault = CableFault.Open };
            else if (scenario != "intact") return 2;
            server.Start();
            Thread.Sleep(TimeSpan.FromMinutes(3));
            return 0;
        }
        try { server.Start(); }
        catch (System.Net.Sockets.SocketException)
        {
            MessageBox.Show("Port 48371 ist belegt. Schließe die bereits laufende Steckwerkbank.", "PuT-3");
            return 1;
        }
        var app = new Application();
        return app.Run(new WorkbenchWindow(model, server));
    }
}
