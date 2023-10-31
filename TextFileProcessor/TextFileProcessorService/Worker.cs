using System.Collections.Concurrent;
using System.Diagnostics;

namespace TextFileProcessorService
{
    /// <summary>
    /// Worker task wrapper. Starts immediately after creation. Stops when workItems are empty. It also can be gracefully canceled with a token.
    /// </summary>
    class Worker
    {
        // only for tracing
        readonly Guid workerId = Guid.NewGuid();

        public Task Task { get; private set; }

        public bool Finished { get => Task.Status != TaskStatus.Running; }

        public Worker(ConcurrentBag<WorkItem> workItems, CancellationToken cancellationToken)
        {
            Task = Task.Run(() =>
            {
                while (!cancellationToken.IsCancellationRequested
                    && workItems.TryTake(out var workItem))
                {
                    if (!File.Exists(workItem.InputFileName)) continue;

                    try
                    {
                        int totalChars = GetTotalChars(workItem.InputFileName);
                        WriteOutputFile(workItem.OutputFileName, totalChars);
                    }
                    catch (Exception e)
                    {
                        Trace.WriteLine($"Worker {workerId}: Exception when processing {Path.GetFileName(workItem.InputFileName)}: {e.Message}");
                    }

                    Trace.WriteLine($"Worker {workerId}: file {Path.GetFileName(workItem.InputFileName)} processed successfully");

                    // emulating a slowness
                    Task.Delay(3000).Wait();
                }

                Trace.WriteLine($"Worker {workerId} finished");
            }, cancellationToken);
        }

        int GetTotalChars(string fileName)
        {
            int totalChars = 0;

            using (var reader = new StreamReader(fileName))
            {
                const int blockSize = 4096;
                char[] buffer = new char[blockSize];

                int count = 0;
                while ((count = reader.ReadBlock(buffer, 0, blockSize)) > 0)
                {
                    totalChars += count;
                }
            }

            return totalChars;
        }

        void WriteOutputFile(string fileName, int count)
        {
            using (var writer = new StreamWriter(fileName))
            {
                writer.Write(count);
                writer.Close();
            }
        }
    }
}