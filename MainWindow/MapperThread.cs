using System.IO;
using System.Threading;
using System;

namespace FestivalInstrumentMapper
{
    internal delegate void ToGipAction(ReadOnlySpan<byte> inputBuffer, Span<byte> gipBuffer);

    internal sealed class MapperThread
    {
        private readonly InstrumentMapperDevice _device;
        private readonly SyntheticController _controller;

        private volatile bool _shouldStop = false;
        private volatile Thread? _readThread = null;

        public bool IsRunning => _readThread != null;

        public MapperThread(InstrumentMapperDevice device, SyntheticController controller)
        {
            _device = device;
            _controller = controller;
        }

        public void Dispose()
        {
            Stop();
            // Let's not take ownership of the device, it can last for more than one run
            // _device.Dispose();
            _controller.Dispose();
        }

        public void Start()
        {
            if (_readThread != null)
                return;

            _device.Open();
            _controller.Connect();

            _shouldStop = false;
            _readThread = new Thread(ReadThread);
            _readThread.Start();
        }

        public void Stop()
        {
            if (_readThread == null)
                return;

            _shouldStop = true;
            _readThread.Join();

            // Automatically done by the thread
            // _readThread = null;
            // _device.Close();
        }

        private void ReadThread()
        {
            try
            {
                Span<byte> inputReport = new byte[_device.GetReadLength()];
                Span<byte> gipReport = new byte[0xE];
                ToGipAction toGip = _device.GetGipConverter();

                while (!_shouldStop)
                {
                    _device.Read(inputReport);
                    toGip(inputReport, gipReport);
                    _controller.SendData(gipReport);

                    // We use an unused bit in the GIP report to indicate the guide button,
                    // which tells us to stop reading - we also check if Select+Start are held
                    if ((gipReport[0] & 0x02) != 0 || (gipReport[0] & 0x0C) == 0x0C)
                        _shouldStop = true;

                    Thread.Yield();
                }
            }
            catch (Exception ex)
            {
                WriteErrorFile(ex); // Call the function to handle error
            }
            finally
            {
                _controller.Disconnect();
                _device.Close();
                _readThread = null;
            }
        }

        private void WriteErrorFile(Exception ex)
        {
            // Get the root directory of the application
            string rootDirectory = AppDomain.CurrentDomain.BaseDirectory;

            // Combine the root directory with the "errors" folder name
            string errorFolderPath = Path.Combine(rootDirectory, "errors");

            // Create the "errors" folder if it doesn't exist
            if (!Directory.Exists(errorFolderPath))
            {
                Directory.CreateDirectory(errorFolderPath);
            }

            // Generate a unique file name for the error log
            string errorFileName = $"error_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";

            // Combine the error folder path with the error file name
            string errorFilePath = Path.Combine(errorFolderPath, errorFileName);

            // Write the exception details to the error file
            File.WriteAllText(errorFilePath, ex.ToString());
        }
    }
}