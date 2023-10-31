using System.Collections.Concurrent;
using System.Diagnostics;

namespace TextFileProcessor
{
    class Program
    {
        static string inputFolder = "input";
        static string outputFolder = "output";

        static void Main(string[] args)
        {
            Console.CursorVisible = false;

            if (args.Length == 2)
            {
                inputFolder = args[0];
                outputFolder = args[1];
            }

            var processor = new TextFileProcessor(inputFolder, outputFolder);

            WriteLine(0, "File Processor Service");
            WriteLine(1, $"will process files in {Path.GetFullPath(inputFolder)}");
            WriteLine(2, $"and write results in {Path.GetFullPath(outputFolder)}");
            WriteLine(3);
            WriteLine(4, "STATUS: STOPPED", ConsoleColor.Red);
            WriteLine(5);
            WriteLine(6, "Press any key to start");

            bool isStarted = false;

            while (Console.ReadKey().Key != ConsoleKey.Escape) {
                if(!isStarted)
                {
                    isStarted = true;
                    processor.Start();

                    WriteLine(4, "STATUS: STARTED", ConsoleColor.Green);
                    WriteLine(5);
                    WriteLine(6, "Press any key to stop");
                }
                else
                {
                    WriteLine(4, "STATUS: TERMINATING...", ConsoleColor.Yellow);
                    
                    processor.WaitForStop();
                    isStarted = false;

                    WriteLine(4, "STATUS: STOPPED", ConsoleColor.Red);
                    WriteLine(5);
                    WriteLine(6, "Press any key to start");
                }
            }
        }

        static void WriteLine(int lineNum, string text = null, ConsoleColor? foreColor = null)
        {
            ConsoleColor colorBefore = Console.ForegroundColor;

            if (foreColor != null)
            {
                Console.ForegroundColor = foreColor.Value;
            }

            Console.CursorLeft = 0;
            Console.CursorTop = lineNum;
            Console.WriteLine(text?.PadRight(Console.BufferWidth));

            if (foreColor != null)
            {
                Console.ForegroundColor = colorBefore;
            }
        }
    }

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
            if (!Directory.Exists(inputFolder))
            {
                throw new ArgumentException("Folder must exist", nameof(inputFolder));
            }

            this.inputFolder = inputFolder;
            this.outputFolder = outputFolder;
        }

        /// <summary>
        /// Starts the service workers and the file system listener.
        /// </summary>
        public void Start()
        {
            if (cancellationSource != null)
            {
                return;
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

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
            if(cancellationSource == null)
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

                if(nonFinishedWorkerTasks.Any())
                {
                    Trace.WriteLine($"Service is waiting for {nonFinishedWorkerTasks.Length} worker tasks for termination...");
                    
                    Task.WaitAll(nonFinishedWorkerTasks);
                }
            }

            if(serviceTask.Status == TaskStatus.Running)
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
                if (addCount <= 0)
                {
                    return;
                }

                for (int i = 0; i < addCount; i++)
                {
                    var worker = new Worker(workItems, cancellationSource.Token);
                    workers.Add(worker);
                }
            }

            Trace.WriteLine($"Workers actualized: {workers.Count} workers are processing {workItems.Count} files");
        }
    }

    /// <summary>
    /// Worker task wrapper. Starts immediately after creation. Stops when workItems are empty. It also can be gracefully canceled with a token.
    /// </summary>
    class Worker
    {
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
                    
                    //Task.Delay(3000).Wait();
                }

                Trace.WriteLine($"Worker {workerId} finished");
            });
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
    
    /// <summary>
    /// Job description class.
    /// </summary>
    class WorkItem
    {
        public string InputFileName { get; }

        public string OutputFileName { get; }

        public WorkItem(string inputFileName, string outputFolder)
        {
            InputFileName = inputFileName;

            string outputName = Path.GetFileName(inputFileName);

            OutputFileName = Path.Combine(outputFolder, outputName);
        }
    }
}