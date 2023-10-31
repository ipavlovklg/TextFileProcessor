using TextFileProcessorService;

namespace Application
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

            WriteLine(0, "File Processor Service");
            WriteLine(1, $"will process files in {Path.GetFullPath(inputFolder)}");
            WriteLine(2, $"and write results in {Path.GetFullPath(outputFolder)}");
            WriteLine(3);

            TextFileProcessor processorService = null;
            try
            {
                processorService = new TextFileProcessor(inputFolder, outputFolder);
            }
            catch (Exception ex)
            {
                WriteLine(4, "STATUS: ERROR", ConsoleColor.Red);
                WriteLine(5);
                WriteLine(6, ex.Message);

                return;
            }

            WriteLine(4, "STATUS: STOPPED", ConsoleColor.White);
            WriteLine(5);
            WriteLine(6, "Press any key to start");

            bool isStarted = false;

            while (Console.ReadKey().Key != ConsoleKey.Escape) {
                if(!isStarted)
                {
                    isStarted = true;
                    processorService.Start();

                    WriteLine(4, "STATUS: STARTED", ConsoleColor.Green);
                    WriteLine(5);
                    WriteLine(6, "Press any key to stop");
                }
                else
                {
                    WriteLine(4, "STATUS: TERMINATING...", ConsoleColor.Yellow);
                    
                    processorService.WaitForStop();
                    isStarted = false;

                    WriteLine(4, "STATUS: STOPPED", ConsoleColor.White);
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
}