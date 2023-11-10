namespace TextFileGenerator
{
    internal class Program
    {
        static string inputFolder = "input";

        const int minSizeChars = 1;
        const int maxSizeChars = 100;

        static int fileCounter = 0;

        static void Main(string[] args)
        {
            Console.CursorVisible = false;

            if (args.Length == 1)
            {
                inputFolder = args[0];
            }

            Console.WriteLine("Sample file generator");
            Console.WriteLine($"will generate text files ({minSizeChars} - {maxSizeChars} chars) in {Path.GetFullPath(inputFolder)}");
            Console.WriteLine();
            Console.WriteLine("Press any key to start");

            Directory.CreateDirectory(inputFolder);

            Random random = new Random();

            bool isStarted = false;
            CancellationTokenSource cancellationSource = null;
            Task workingTask = null;

            while (Console.ReadKey().Key != ConsoleKey.Escape)
            {
                if (!isStarted)
                {
                    isStarted = true;

                    cancellationSource = new CancellationTokenSource();

                    workingTask = Task.Run(() =>
                    {
                        while(!cancellationSource.Token.IsCancellationRequested)
                        {
                            int charCount = random.Next(minSizeChars, maxSizeChars + 1);
                            string fileName = $"{fileCounter++:00000}-({charCount} chars).txt";

                            using (var writer = new StreamWriter(Path.Combine(inputFolder, fileName)))
                            {
                                string sampleLine = "".PadRight(charCount, 'a');
                                writer.Write(sampleLine);
                            }
                            Console.WriteLine($"File {fileName} added");

                            // emulating a slowness
                            Task.Delay(100).Wait();
                        }
                    });
                }
                else
                {
                    isStarted = false;
                    
                    cancellationSource?.Cancel();
                    workingTask?.Wait();
                    Console.WriteLine("Stopped");
                    Console.WriteLine();
                    Console.WriteLine("Press any key to start");
                }
            }

        }
    }
}