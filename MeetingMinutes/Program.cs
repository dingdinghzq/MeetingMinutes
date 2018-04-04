// ---------------------------------------------------------------------------------------------------------------------
//  <copyright file="Program.cs" company="Microsoft">
//    Copyright (c) Microsoft Corporation.  All rights reserved.
// MIT LicensePermission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the ""Software""), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  </copyright>
//  ---------------------------------------------------------------------------------------------------------------------

namespace MeetingMinutes
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using CognitiveServicesAuthorization;
    using Microsoft.Bing.Speech;
    using System.Linq;
    using YoutubeExtractor;
    using System.Collections.Generic;
    using System.Diagnostics;
    using NAudio.Wave;
    using Google.Cloud.Speech.V1;

    /// <summary>
    /// This sample program shows how to use <see cref="SpeechClient"/> APIs to perform speech recognition.
    /// </summary>
    public class Program
    {

        private const string SnippetFile = "snippet.wav";

        private const string WorkingFolder = "BSDMeeting";

        /// <summary>
        /// The entry point to this sample program. It validates the input arguments
        /// and sends a speech recognition request using the Microsoft.Bing.Speech APIs.
        /// </summary>
        /// <param name="args">The input arguments.</param>
        public static void Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("Usage: MeetingMinutes <YoutubeUrl>");
                Console.WriteLine("  Example: MeetingMinutes https://youtu.be/nNDMLPP5nq4");
            }
            var p = new Program();
            Console.WriteLine($"Downloading video... {args[0]}");
            string videoFile = p.DownloadVideo(args[0]);
            Console.WriteLine("Convert to audio file...");
            string audioFile = p.ConvertToWav(videoFile);
            Console.WriteLine(audioFile);

            Console.WriteLine("Start Transcribe...");
            var outputFile = $"{WorkingFolder}\\{Path.GetFileNameWithoutExtension(audioFile)}_Subtitle.txt";
            Console.WriteLine($"OutputFile: {outputFile}");

            ISpeechAPI api = new GoogleSpeechAPI(outputFile);
            p.Start(api, outputFile, audioFile);
        }

        private void Start(ISpeechAPI api, string outputFile, string audioFile)
        {
            TimeSpan currentTime = TimeSpan.FromMinutes(0);
            currentTime = GetLastTimeIfPossible(outputFile, currentTime);

            // The Speech API only supports ~1 minutes audio, we split the long audio one by one.
            while (SeekAudioFile(audioFile, currentTime, SnippetFile))
            {
                int retry = 3;
                while (true)
                {
                    try
                    {
                        var newTime = api.RecognizeAsync(SnippetFile, currentTime).Result;
                        // There is no words in this minute
                        if (newTime == currentTime)
                        {
                            Console.WriteLine($"No speech in minute: {currentTime}, skip.");
                            currentTime = currentTime + TimeSpan.FromMinutes(1);
                        }
                        else
                        {
                            currentTime = newTime;
                        }
                        break;
                    }
                    catch (AggregateException e)
                    {
                        Console.WriteLine(e.InnerException.ToString());
                        if (--retry <= 0)
                        {
                            throw;
                        }
                    }
                }
            }
        }

        private static TimeSpan GetLastTimeIfPossible(string outputFile, TimeSpan currentTime)
        {
            if (File.Exists(outputFile))
            {
                var lastLine = File.ReadAllLines(outputFile).Last();
                if (!string.IsNullOrEmpty(lastLine))
                {
                    var parts = lastLine.Split('\t');
                    if (parts.Length == 3)
                    {
                        currentTime = TimeSpan.Parse(parts[0]) + TimeSpan.Parse(parts[1]);
                    }
                }
            }

            return currentTime;
        }

        public string DownloadVideo(string youtubeUrl)
        {

            IEnumerable<VideoInfo> videoInfos = DownloadUrlResolver.GetDownloadUrls(youtubeUrl);

            // Download the one with best audio quality
            VideoInfo video = videoInfos
                .OrderByDescending(info => info.AudioBitrate)
                .First();

            if (video.RequiresDecryption)
            {
                DownloadUrlResolver.DecryptDownloadUrl(video);
            }

            DirectoryInfo di = new DirectoryInfo(WorkingFolder);
            di.Create();
            var filename = CleanupPath(video.Title) + video.VideoExtension;
            string finalPath = Path.Combine(di.FullName, filename);

            var videoDownloader = new VideoDownloader(video, finalPath);

            // Register the ProgressChanged event and print the current progress
            videoDownloader.DownloadProgressChanged += (sender, args) => Console.Write("\r" + args.ProgressPercentage.ToString("0.00") + "%");

            videoDownloader.Execute();

            Console.WriteLine();
            return finalPath;
        }

        // The speech API need 16k mono channel wav file
        public string ConvertToWav(string filename)
        {
            var inputFile = filename;
            var outputFile = filename.Replace(".mp4", ".wav");
             var ffmpegProcess = new Process();
            ffmpegProcess.StartInfo.UseShellExecute = true;
            ffmpegProcess.StartInfo.FileName = "ffmpeg\\ffmpeg.exe";
            // -vn Voice only
            // -ac 1 mono channel
            ffmpegProcess.StartInfo.Arguments = " -y -i \"" + inputFile + "\" -vn -acodec pcm_s16le -ac 1 -ar 16k \"" + outputFile + "\"";
            ffmpegProcess.Start();
            ffmpegProcess.WaitForExit();
            if (!ffmpegProcess.HasExited)
            {
                ffmpegProcess.Kill();
            }
            return outputFile;
        }

        private string CleanupPath(string input)
        {
            var invalids = Path.GetInvalidFileNameChars();
            
            return string.Join("_", input.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }


        private bool SeekAudioFile(string filePath, TimeSpan currentSpan, string snippetFile)
        {
            using (WaveFileReader reader = new WaveFileReader(filePath))
            {
                if (currentSpan >= reader.TotalTime)
                {
                    return false;
                }

                int timeToSlice = Math.Min(60, (int)(reader.TotalTime - currentSpan).TotalSeconds);

                using (WaveFileWriter writer = new WaveFileWriter(snippetFile, reader.WaveFormat))
                {
                    reader.CurrentTime = currentSpan;
                    var format = reader.WaveFormat;
                    int bytesInSecond = format.BitsPerSample * format.Channels * format.SampleRate / 8;
                    byte[] buffer = new byte[bytesInSecond * timeToSlice];
                    reader.Read(buffer, 0, buffer.Length);
                    writer.Write(buffer, 0, buffer.Length);
                    writer.Flush();
                }

                return timeToSlice >= 60;
            }
        }


    }
}
