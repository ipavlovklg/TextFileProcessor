namespace TextFileProcessorService
{
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