using Google.Cloud.Speech.V1;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingMinutes
{
    public class GoogleSpeechAPI : BaseSpeechAPI
    {
        public GoogleSpeechAPI(string outputFilePath) : base(outputFilePath)
        {
            // Set the credential file to yours
            // See https://github.com/GoogleCloudPlatform/dotnet-docs-samples/tree/master/speech/api to know how to
            // create the credential json file.
            Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", "Meeting Minutes-bfcad7d264d9.json");
        }
        public async override Task<TimeSpan> RecognizeAsync(string inputPath, TimeSpan timeOffsetOfWholeFile)
        {
            List<string> resultBuffer = new List<string>();
            var speech = Google.Cloud.Speech.V1.SpeechClient.Create();
            var streamingCall = speech.StreamingRecognize();
            // Write the initial request with the config.
            await streamingCall.WriteAsync(
                new StreamingRecognizeRequest()
                {
                    StreamingConfig = new StreamingRecognitionConfig()
                    {
                        Config = new RecognitionConfig()
                        {
                            Encoding =
                            RecognitionConfig.Types.AudioEncoding.Linear16,
                            SampleRateHertz = 16000,
                            LanguageCode = "en",
                            EnableWordTimeOffsets = true,
                        },
                        InterimResults = true,
                    }
                });
            TimeSpan lastTime = TimeSpan.FromTicks(0);
            // Print responses as they arrive.
            Task printResponses = Task.Run(async () =>
            {
                while (await streamingCall.ResponseStream.MoveNext(
                    default(CancellationToken)))
                {
                    foreach (var result in streamingCall.ResponseStream
                        .Current.Results)
                    {
                        if (result.IsFinal)
                        {
                            var topCandidate = result.Alternatives.FirstOrDefault();
                            if (topCandidate != null)
                            {
                                TimeSpan ts = TimeSpan.FromTicks(0);
                                TimeSpan te = TimeSpan.FromTicks(0);
                                var word = topCandidate.Words.FirstOrDefault();
                                if (word != null)
                                {
                                    ts = word.StartTime.ToTimeSpan();
                                    lastTime = topCandidate.Words.Last().EndTime.ToTimeSpan();
                                    te = lastTime - ts;
                                }
                                Console.WriteLine($"[{ts + timeOffsetOfWholeFile}][{te}]{topCandidate.Transcript}");
                                string line = $"{ts + timeOffsetOfWholeFile}\t{te}\t{topCandidate.Transcript}";
                                resultBuffer.Add(line);
                            }
                        }
                    }
                }
            });

            // Stream the file content to the API.  Write 2 32kb chunks per 
            // second.
            using (FileStream fileStream = new FileStream(inputPath, FileMode.Open))
            {
                var buffer = new byte[32 * 1024];
                int bytesRead;
                while ((bytesRead = await fileStream.ReadAsync(
                    buffer, 0, buffer.Length)) > 0)
                {
                    await streamingCall.WriteAsync(
                        new StreamingRecognizeRequest()
                        {
                            AudioContent = Google.Protobuf.ByteString
                            .CopyFrom(buffer, 0, bytesRead),
                        });
                    await Task.Delay(500);
                };
            }
            await streamingCall.WriteCompleteAsync();
            await printResponses;
            File.AppendAllLines(this.OutputFilePath, resultBuffer);
            return lastTime + timeOffsetOfWholeFile;

        }

        // Not used
        static object LongRunningRecognize(string filePath)
        {
            var speech = Google.Cloud.Speech.V1.SpeechClient.Create();
            var longOperation = speech.LongRunningRecognize(new RecognitionConfig()
            {
                Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                SampleRateHertz = 16000,
                LanguageCode = "en",
            }, RecognitionAudio.FromFile(filePath));
            longOperation = longOperation.PollUntilCompleted();
            var response = longOperation.Result;
            foreach (var result in response.Results)
            {
                foreach (var alternative in result.Alternatives)
                {

                    Console.WriteLine(alternative.Transcript);
                }
            }
            return 0;
        }
    }
}
