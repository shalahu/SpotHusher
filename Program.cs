using System.Diagnostics;

namespace SpotHusher;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--wait-for-pid" && args.Length > 1 && int.TryParse(args[1], out int pid))
        {
            try
            {
                using var oldProcess = Process.GetProcessById(pid);

                oldProcess.Kill(); 
                oldProcess.WaitForExit(3000);
            }
            catch
            {
                // ignored
            }
        }

        using (_ = new Mutex(true, "Global\\SpotHusher_SingleInstance_Mutex_Key", out var isNewInstance))
        {
            if (!isNewInstance) return;

            ApplicationConfiguration.Initialize();

            var appContext = new TrayAppContext();
            Application.Run(appContext);
        }
    }
}