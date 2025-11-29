using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace AchikoDLL
{
    public static class PipeLogger
    {
        private static NamedPipeClientStream _pipe;
        private static Thread _thread;
        private static bool _running;

        public static void Start()
        {
            if (_running) return;
            _running = true;

            _thread = new Thread(new ThreadStart(PipeThreadLoop));
            _thread.IsBackground = true;
            _thread.Name = "AchikoPipe";
            _thread.Start();
        }

        private static void PipeThreadLoop()
        {
            while (_running)
            {
                try
                {
                    // Change this line to use the new pipe name:
                    _pipe = new NamedPipeClientStream(".", "AchikoPipe_Managed", PipeDirection.Out);
                    _pipe.Connect(1000);

                    Log("[AchikoDLL] Pipe connected from C# side");

                    while (_running && _pipe.IsConnected)
                        Thread.Sleep(100);
                }
                catch
                {
                    // Retry connection
                }
                finally
                {
                    try
                    {
                        if (_pipe != null)
                            _pipe.Dispose();
                    }
                    catch { }

                    Thread.Sleep(500);
                }
            }
        }

        public static void Log(string message)
        {
            // Safer approach - copy reference and check
            var pipe = _pipe;
            if (pipe != null && pipe.IsConnected)
            {
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message + "\n");
                    pipe.Write(data, 0, data.Length);
                    pipe.Flush();
                }
                catch
                {
                    // Log failure silently
                }
            }
            // Remove the Queue logic entirely for now
        }

        public static void Stop()
        {
            _running = false;
            try
            {
                if (_pipe != null)
                    _pipe.Dispose();
            }
            catch { }
        }
    }
}