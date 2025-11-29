using System;
using System.Diagnostics;

namespace AchikoDLL
{
    public class Loader
    {
        public static int Start(string args)
        {
            try
            {
                PipeLogger.Start();
                PipeLogger.Log("===========================================");
                PipeLogger.Log("[AchikoDLL] Loader.Start() called!");
                PipeLogger.Log("[AchikoDLL] C# IS RUNNING INSIDE WOW!");
                PipeLogger.Log("===========================================");

                // ... rest of your code

                int pid = Process.GetCurrentProcess().Id;
                PipeLogger.Log("[AchikoDLL] WoW PID: " + pid.ToString());

                BotCore botCore = new BotCore(pid);
                PipeLogger.Log("[AchikoDLL] BotCore initialized");

                System.Threading.ThreadPool.QueueUserWorkItem(new System.Threading.WaitCallback(RunBot), botCore);
                PipeLogger.Log("[AchikoDLL] Bot loop started on background thread");

                return 0; // SUCCESS
            }
            catch (Exception ex)
            {
                PipeLogger.Log("[AchikoDLL] FATAL: " + ex.Message);
                return 1; // FAILURE
            }
        }

        private static void RunBot(object state)
        {
            BotCore bot = (BotCore)state;
            bot.Run();
        }
    }
}