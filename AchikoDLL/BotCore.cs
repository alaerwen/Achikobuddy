using System;
using System.Threading;

namespace AchikoDLL
{
    public class BotCore : IDisposable
    {
        private bool _running;
        private int _pid;

        public BotCore(int pid)
        {
            _pid = pid;
            _running = false;
            PipeLogger.Log("[BotCore] Created for PID " + pid.ToString());
        }

        public void Run()
        {
            _running = true;
            PipeLogger.Log("[BotCore] Bot loop starting...");

            while (_running)
            {
                Thread.Sleep(1000);
                // Your bot logic here
            }

            PipeLogger.Log("[BotCore] Bot loop stopped");
        }

        public void Dispose()
        {
            _running = false;
            PipeLogger.Log("[BotCore] Disposed");
        }
    }
}