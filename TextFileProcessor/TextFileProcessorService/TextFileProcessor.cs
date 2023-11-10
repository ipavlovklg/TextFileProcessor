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

        FileSystemWatcher fsWatcher;

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

            InitAndStartFsWatcher();

            WorkItem[] initialWorkItems = Directory.GetFiles(inputFolder, "*.txt")
                .Select(inputFile => new WorkItem(inputFile, outputFolder))
                .ToArray();

            workItems = new ConcurrentBag<WorkItem>(initialWorkItems);

            Trace.WriteLine("Service started");

            ActualizeWorkers();

            fsWatcher.EnableRaisingEvents = true;
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

            StopAndDisposeFsWatcher();

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

            cancellationSource.Dispose();
            cancellationSource = null;

            Trace.WriteLine($"Service stopped");
        }

        void InitAndStartFsWatcher()
        {
            fsWatcher = new FileSystemWatcher(inputFolder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName,
                Filter = "*.txt"
            };
            fsWatcher.Created += OnFileAdded;
            fsWatcher.EnableRaisingEvents = true;
        }

        void StopAndDisposeFsWatcher()
        {
            fsWatcher.EnableRaisingEvents = false;
            fsWatcher.Dispose();
            fsWatcher = null;
        }

        void OnFileAdded(object sender, FileSystemEventArgs e)
        {
            Trace.WriteLine($"File added: {e.Name}");

            string inputFileName = Path.Combine(inputFolder, e.Name);
            workItems.Add(new WorkItem(inputFileName, outputFolder));

            ActualizeWorkers();
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
                int workItemsCount = workItems.Count;
                int requiredWorkersCount = Math.Min(workItemsCount, NumberOfWorkers);

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
                Trace.WriteLine($"Workers actualized: {count} workers are processing {workItemsCount} files");
            }
        }
    }
}