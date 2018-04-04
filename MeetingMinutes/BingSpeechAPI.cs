using CognitiveServicesAuthorization;
using Microsoft.Bing.Speech;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MeetingMinutes
{
    public class BingSpeechAPI : BaseSpeechAPI
    {
        /// <summary>
        /// Short phrase mode URL
        /// </summary>
        private static readonly Uri ShortPhraseUrl = new Uri(@"wss://speech.platform.bing.com/api/service/recognition");

        /// <summary>
        /// The long dictation URL
        /// </summary>
        private static readonly Uri LongDictationUrl = new Uri(@"wss://speech.platform.bing.com/api/service/recognition/continuous");
        /// <summary>
        /// A completed task
        /// </summary>
        private static readonly Task CompletedTask = Task.FromResult(true);

        /// <summary>
        /// Cancellation token used to stop sending the audio.
        /// </summary>
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private string key;
        public BingSpeechAPI(string apiKey, string outputTxtPath): base(outputTxtPath)
        {
            this.key = apiKey;
        }
        public override Task<TimeSpan> RecognizeAsync(string inputSnippetPath, TimeSpan timeOffsetOfWholeFile)
        {
            // Todo: implement the AzureAPI
            throw new NotImplementedException();
        }

        public async Task Run(string audioFile, Uri serviceUrl, string subscriptionKey)
        {
            // create the preferences object
            var preferences = new Preferences("en-US", serviceUrl, new CognitiveServicesAuthorizationProvider(subscriptionKey));

            // Create a a speech client
            using (var speechClient = new SpeechClient(preferences))
            {
                speechClient.SubscribeToRecognitionResult(this.OnRecognitionResult);

                // create an audio content and pass it a stream.
                using (var audio = new FileStream(audioFile, FileMode.Open, FileAccess.Read))
                {
                    var deviceMetadata = new DeviceMetadata(DeviceType.Near, DeviceFamily.Desktop, NetworkType.Ethernet, OsName.Windows, "1607", "Dell", "T3600");
                    var applicationMetadata = new ApplicationMetadata("SampleApp", "1.0.0");
                    var requestMetadata = new RequestMetadata(Guid.NewGuid(), deviceMetadata, applicationMetadata, "SampleAppService");

                    await speechClient.RecognizeAsync(new SpeechInput(audio, requestMetadata), this.cts.Token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Invoked when the speech client receives a phrase recognition result(s) from the server.
        /// </summary>
        /// <param name="args">The recognition result.</param>
        /// <returns>
        /// A task
        /// </returns>
        public Task OnRecognitionResult(RecognitionResult args)
        {
            var response = args;
            Console.WriteLine();

            if (response.Phrases != null)
            {
                RecognitionPhrase topPhrase = response.Phrases.FirstOrDefault();
                if (topPhrase != null)
                {
                    Console.WriteLine("[{1}]{0} ", topPhrase.DisplayText, TimeSpan.FromTicks((long)topPhrase.MediaTime));
                    string line = $"{TimeSpan.FromTicks((long)topPhrase.MediaTime)}\t{TimeSpan.FromTicks((long)topPhrase.MediaDuration)}\t{topPhrase.DisplayText}";

                    File.AppendAllLines(this.OutputFilePath, new[] { line });
                }
            }

            Console.WriteLine();
            return CompletedTask;
        }

    }
}
