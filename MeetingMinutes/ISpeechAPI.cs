using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetingMinutes
{
    public interface ISpeechAPI
    {
        Task<TimeSpan> RecognizeAsync(string inputPath, TimeSpan timeOffsetOfWholeFile);
    }

    public abstract class BaseSpeechAPI : ISpeechAPI
    {
        public string OutputFilePath { get; set; }
        public BaseSpeechAPI(string outputFilePath)
        {
            this.OutputFilePath = outputFilePath;
        }

        public abstract Task<TimeSpan> RecognizeAsync(string inputPath, TimeSpan timeOffsetOfWholeFile);
        
    }
}
