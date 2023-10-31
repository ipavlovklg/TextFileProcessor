namespace TextFileGenerator
{
    internal class Program
    {
        static string inputFolder = "input";

        static void Main(string[] args)
        {
            Console.CursorVisible = false;

            if (args.Length == 1)
            {
                inputFolder = args[0];
            }

            Console.WriteLine("Sample file generator");
            Console.WriteLine($"will generate text files (1 - 10 mb) in {Path.GetFullPath(inputFolder)}");
            Console.WriteLine();
            Console.WriteLine("Press any key to start");

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
                            const int megabyte = 1024 * 1024;
                            int estimatedFileSize = random.Next(1 * megabyte, 10 * megabyte);
                            string fileName = Guid.NewGuid() + $"-({estimatedFileSize / megabyte} mb)" + ".txt";

                            using (var writer = new StreamWriter(Path.Combine(inputFolder, fileName)))
                            {
                                while (writer.BaseStream.Length < estimatedFileSize)
                                {
                                    string sampleLine = "".PadRight(4 * 1024, 'a');
                                    writer.Write(sampleLine);
                                }
                            }
                            Console.WriteLine($"File {fileName} ({estimatedFileSize / megabyte} mb) added");

                            // emulating a slowness
                            Task.Delay(500).Wait();
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