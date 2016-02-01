namespace MicroServicesStarter
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Windows.Forms;
    using MicroServicesStarter.Debug;
    using MicroServicesStarter.ServiceManagement;
    using MicroServicesStarter.ServiceManagement.Action;
    using Services.Packages;
    using Services.Packages.Client.Actions;

    static class Program
    {
        private const string MicroServicesStarterFolder = @"..\..\";
        private const string AdminFolder = @"..\..\..\Admin\";
        private const string DebugFlag = "--debug";
        private const string UpdateFlag = "--update";

        [STAThread]
        static void Main(string[] args)
        {
            // This runs when the update script runs
            if (args.Length > 0 && args[0] == UpdateFlag)
            {
                UpdateApplication();

                return;
            }

            // This runs when we want to detach debugger and create another process
            if (args.Length == 0)
            {
                Process.Start(new ProcessStartInfo()
                {
                    FileName = "MicroServicesStarter.exe",
                    Arguments = DebugFlag,
                    UseShellExecute = true,
                });

                return;
            }

#if DEBUG
            Thread.Sleep(100);

            if (args[0] == DebugFlag)
            {
                // Attach on the process after debugging. This ensures that the application stays on after we stop debugging
                var tries = 10;

                while (true)
                {
                    try
                    {
                        new AdminSetupContext().Do(new AttachDebuggerToProcess(Process.GetCurrentProcess()));

                        break;
                    }
                    catch
                    {
                        if (tries == 0)
                        {
                            throw;
                        }

                        tries--;

                        Thread.Sleep(1000);
                    }
                }
            }
#endif

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#if DEBUG
            Application.Run(new InitForm(SetupType.Debug));
#elif DEPLOY
            Application.Run(new InitForm(SetupType.Deploy));
#elif INTEGRATIONTEST
            Application.Run(new InitForm(SetupType.IntegrationTest));
#else
            Application.Run(new InitForm(SetupType.Release));
#endif
        }

        private static void UpdateApplication()
        {
            Console.WriteLine("Updating the project...");

            UpdateOnPath(
                MicroServicesStarterFolder,
                new[]
                {
                    "Developer.MicroServicesStarter"
                });

            Console.WriteLine("Updating the admin...");

            UpdateOnPath(
                AdminFolder,
                new[]
                {
                    "Services.Executioner"
                });

            Console.WriteLine("Done!");
        }

        private static void UpdateOnPath(string relativePath, string[] packages)
        {
            var fullPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath));

            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            var applicationRepository = new Repository(fullPath);

            foreach (var package in packages)
            {
                applicationRepository.RegisterPackage(package);
            }

            applicationRepository.Do(new UpdateClientApplication("update.msd.am", 12345, fullPath));
        }
    }
}
