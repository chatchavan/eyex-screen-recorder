using System;
using System.Collections.Generic;
using System.Text;
using AForge.Video.FFMPEG;
using System.Drawing;
using System.Drawing.Imaging;
using EyeXFramework;
using Tobii.EyeX.Framework;
using CsvHelper;
using System.IO;
using System.Globalization;

namespace screencapture
{
    class VideoRecorder
    {
        private VideoFileWriter videoWriter;
        private System.Timers.Timer frameTimer;
        const int intervalBetweenFrames = 40; // 1000 / 25fps

        private bool isRecording;
        private bool writing = false;
        double gazeX, gazeY;
        const int prevGazeQueueSize = 10;
        Queue<double> prevGazeXQ = new Queue<double>(prevGazeQueueSize);
        Queue<double> prevGazeYQ = new Queue<double>(prevGazeQueueSize);
        
        EyeXHost _eyeXHost;
        GazePointDataStream _lightlyFilteredGazeDataStream;

        private TextWriter textWriter;
        private CsvWriter csv;
        private CultureInfo usCulture = new CultureInfo("en-US"); 

        private Size screenSize;

        public VideoRecorder()
        {
            Rectangle bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            screenSize = new Size(bounds.Width, bounds.Height);
            frameTimer = new System.Timers.Timer(intervalBetweenFrames);
            frameTimer.Elapsed += ProcessFrame;
            frameTimer.AutoReset = true;
            isRecording = false;
        }

        public void StartRecording(String filenameToSave){
            videoWriter = new VideoFileWriter();
            videoWriter.Open(filenameToSave, screenSize.Width, screenSize.Height, 25, VideoCodec.MPEG4, 8000000);

            // init csv
            textWriter = File.CreateText(Path.ChangeExtension(filenameToSave, ".csv"));
            csv = new CsvWriter(textWriter);
            csv.Configuration.CultureInfo = usCulture;
            csv.WriteField("Timestamp");
            csv.WriteField("GazeX");
            csv.WriteField("GazeY");
            csv.NextRecord();


            frameTimer.Start();
            isRecording = true;
            StartEyeStream();
        }

        public void EndRecording(){
            isRecording = false;
            frameTimer.Stop();
            while (writing) { }     // prevent closing in the middle of writing
            videoWriter.Close();
            textWriter.Close();
            StopEyeStream();
        }

        private void StartEyeStream()
        {
            _eyeXHost = new EyeXHost();
            _lightlyFilteredGazeDataStream = _eyeXHost.CreateGazePointDataStream(GazePointDataMode.LightlyFiltered);
            _eyeXHost.Start();

                    // Write the data to the console.
            _lightlyFilteredGazeDataStream.Next += gazeDataStreamNext;
            Console.WriteLine("Eyex setup");
        }

        private void gazeDataStreamNext(object s, GazePointEventArgs e)
        {
            gazeX = e.X;
            gazeY = e.Y;
            
            // record CSV
            csv.WriteField(e.Timestamp);
            csv.WriteField(gazeX);
            csv.WriteField(gazeY);
            csv.NextRecord();

            // add to queue
            prevGazeXQ.Enqueue(gazeX);
            prevGazeYQ.Enqueue(gazeY);
            if (prevGazeXQ.Count >= prevGazeQueueSize) {
                prevGazeXQ.Dequeue();
                prevGazeYQ.Dequeue();
            }
        }

        private void StopEyeStream()
        {
            _lightlyFilteredGazeDataStream.Dispose();
            _eyeXHost.Dispose();
        }

        private void ProcessFrame(Object source, System.Timers.ElapsedEventArgs e)
        {
           
            {
                Bitmap frameImage = new Bitmap(screenSize.Width, screenSize.Height);
                // record a frame of screen
                using (Graphics g = Graphics.FromImage(frameImage))
                {
                    g.CopyFromScreen(0, 0, 0, 0, screenSize, CopyPixelOperation.SourceCopy);
                    
                    int ellipseSize = 10;
                    
                    // previous gaze points
                    double[] prevGazeXs = new double[prevGazeXQ.Count];
                    double[] prevGazeYs = new double[prevGazeYQ.Count];
                    prevGazeXQ.CopyTo(prevGazeXs, 0);
                    prevGazeYQ.CopyTo(prevGazeYs, 0);
                    Point[] prevGazePoints = new Point[prevGazeXQ.Count];
                    for (int i = 0; i < prevGazeXQ.Count; i++) {
                        Brush prevRedBrush = new SolidBrush(Color.FromArgb((i * 245/prevGazeQueueSize) +10, 0xff, 0, 0));
                        g.FillEllipse(prevRedBrush, (int) prevGazeXs[i] - (ellipseSize / 2), (int) prevGazeYs[i] - (ellipseSize / 2), ellipseSize, ellipseSize);
                        prevGazePoints[i] = new Point((int) prevGazeXs[i], (int) prevGazeYs[i]);
                    }
                    if (prevGazeXQ.Count > 2) {
                        Pen gazeLinePen = new Pen(new SolidBrush(Color.FromArgb(180, 0xff, 0, 0)));
                        g.DrawLines(gazeLinePen, prevGazePoints);
                    }
                    

                    // latest gaze point
                    Color red = Color.FromArgb(255, 0xff, 0, 0);
                    Brush redBrush = new SolidBrush(red);
                    g.FillEllipse(redBrush, (int) gazeX - (ellipseSize / 2), (int) gazeY - (ellipseSize / 2), ellipseSize, ellipseSize);
                }
                if (isRecording)
                {
                    if (!writing)
                    {
                        writing = true;
                        videoWriter.WriteVideoFrame(frameImage);
                        writing = false;
                    }
                }
                frameImage.Dispose();
            }
        }
    }
}
