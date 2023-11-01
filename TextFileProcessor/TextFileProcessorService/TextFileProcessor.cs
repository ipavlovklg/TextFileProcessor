using System.Collections.Concurrent;
using System.Diagnostics;

namespace TextFileProcessorService
{
    /// <summary>
    /// The text file processor service.
    /// </summary>
    class TextFileProcessor
    {
        const int NumberOfWorkers = 4;

        readonly string inputFolder;
        readonly string outputFolder;

        Task serviceTask;
        ConcurrentBag<WorkItem> workItems;
        CancellationTokenSource cancellationSource;

        List<Worker> workers = new List<Worker>();
        object workersSyncRoot = new object();

        public TextFileProcessor(string inputFolder, string outputFolder)
        {
            this.inputFolder = inputFolder;
            this.outputFolder = outputFolder;
        }

        /// <summary>
        /// Starts the service workers and file system listener.
        /// </summary>
        public void Start()
        {
            if (cancellationSource != null)
            {
                return;
            }

            Directory.CreateDirectory(inputFolder);
            Directory.CreateDirectory(outputFolder);

            cancellationSource = new CancellationTokenSource();

            WorkItem[] initialWorkItems = Directory.GetFiles(inputFolder, "*.txt")
                .Select(inputFile => new WorkItem(inputFile, outputFolder))
                .ToArray();

            workItems = new ConcurrentBag<WorkItem>(initialWorkItems);

            serviceTask = Task.Run(() =>
            {
                Trace.WriteLine("Service task started");

                ActualizeWorkers();

                // TODO: need a better logic to handle pasting of multiple files: some pasted files can be lost now
                // https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemwatcher.internalbuffersize
                using (var watcher = new FileSystemWatcher(inputFolder)
                {
                    InternalBufferSize = 64 * 1024,
                    EnableRaisingEvents = false,
                    IncludeSubdirectories = false,
                    Filter = "*.txt"
                })
                {
                    while (!cancellationSource.Token.IsCancellationRequested)
                    {
                        var result = watcher.WaitForChanged(WatcherChangeTypes.Created, 1000);
                        if (result.TimedOut)
                        {
                            continue;
                        }

                        string inputFileName = Path.Combine(inputFolder, result.Name);
                        workItems.Add(new WorkItem(inputFileName, outputFolder));

                        ActualizeWorkers();
                    }
                };

                Trace.WriteLine("Service task finished");
            });
        }

        /// <summary>
        /// Sends the stop command and waits for graceful service termination
        /// </summary>
        public void WaitForStop()
        {
            if (cancellationSource == null)
            {
                return;
            }

            if (!cancellationSource.IsCancellationRequested)
            {
                cancellationSource.Cancel();
            }

            lock (workersSyncRoot)
            {
                var nonFinishedWorkerTasks = workers.Where(x => !x.Finished).Select(x => x.Task).ToArray();

                if (nonFinishedWorkerTasks.Any())
                {
                    Trace.WriteLine($"Service is waiting for {nonFinishedWorkerTasks.Length} worker tasks for termination...");

                    Task.WaitAll(nonFinishedWorkerTasks);
                }
            }

            if (serviceTask.Status == TaskStatus.Running)
            {
                Trace.WriteLine($"Service task is terminating...");

                serviceTask.Wait();
            }

            cancellationSource.Dispose();
            cancellationSource = null;

            Trace.WriteLine($"Service stopped");
        }

        /// <summary>
        /// Makes up to <see cref="NumberOfWorkers"/> workers to work on the input files.
        /// </summary>
        void ActualizeWorkers()
        {
            lock (workersSyncRoot)
            {
                workers = workers.Where(x => !x.Finished).ToList();

                int nonFinishedWorkersCount = workers.Count;
                int requiredWorkersCount = Math.Min(workItems.Count, NumberOfWorkers);

                int addCount = requiredWorkersCount - nonFinishedWorkersCount;
                if (addCount > 0)
                {
                    for (int i = 0; i < addCount; i++)
                    {
                        var worker = new Worker(workItems, cancellationSource.Token);
                        workers.Add(worker);
                    }
                }

                int count = workers.Where(x => !x.Finished).Count();
                Trace.WriteLine($"Workers actualized: {count} workers are processing {workItems.Count} files");
            }
        }
    }
}