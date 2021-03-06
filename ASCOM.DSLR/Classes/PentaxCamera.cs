﻿using ASCOM.DSLR.Enums;
using ASCOM.DSLR.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Logging;

namespace ASCOM.DSLR.Classes
{
    public class PentaxCamera : BaseCamera, IDslrCamera
    {
        private bool _waitingForImage = false;
        private DateTime _exposureStartTime;
        private const int timeout = 60;
        private double _lastDuration;
        private string _lastFileName;

        public PentaxCamera(List<CameraModel> cameraModelsHistory) : base(cameraModelsHistory)
        {
            
        }

        public event EventHandler<ImageReadyEventArgs> ImageReady;
        public event EventHandler<ExposureFailedEventArgs> ExposureFailed;
        public event EventHandler<LiveViewImageReadyEventArgs> LiveViewImageReady;
        private string _modelStr;

        public string Model
        {
            get
            {
                if (string.IsNullOrEmpty(_modelStr))
                {
                    var result = ExecuteCommand("-s");
                    var parsedStatus = ParseStatus(result);
                    if (parsedStatus.ContainsKey("pktriggercord-cli"))
                    {
                        _modelStr = parsedStatus["pktriggercord-cli"];
                    }
                }
                return _modelStr;
            }
        }

        public ConnectionMethod IntegrationApi => ConnectionMethod.Pentax;

        public bool SupportsViewView { get { return false; } }

        public void AbortExposure()
        {

        }

        public void ConnectCamera()
        {

        }

        public void DisconnectCamera()
        {
        }

        public void Dispose()
        {
            
        }

        public override CameraModel ScanCameras()
        {
            var cameraModel = GetCameraModel(Model);

            return cameraModel;
        }

        public void StartExposure(double Duration, bool Light)
        {

            Logger.WriteTraceMessage("PentaxCamera.StartExposure(Duration, Light), duration ='" + Duration.ToString() + "', Light = '" + Light.ToString() + "'");

            string fileName = StorePath + "\\" + GetFileName(Duration, DateTime.Now);
            MarkWaitingForExposure(Duration, fileName);
            watch();

            //ExecuteCommand(string.Format("--file_format dng -o {0} --iso {1} --shutter_speed {2}", fileName + ".dng", Iso, Duration));
            //pktriggercord-cli --file_format dng -o c:\temp\test.dng -i 400 -t 1
            Logger.WriteTraceMessage("--file_format dng -o " + fileName + ".dng -i " + Iso + " -t " + Duration);
            ExecuteCommand(string.Format("--file_format dng -o {0} -i {1} -t {2}", fileName + ".dng", Iso, Duration));

        }

        private string _fileNameWaiting;


        private void MarkWaitingForExposure(double Duration, string fileName)
        {
            _exposureStartTime = DateTime.Now;
            _lastDuration = Duration;
            _waitingForImage = true;
            _fileNameWaiting = fileName;
        }

        FileSystemWatcher watcher;

        private void watch()
        {
            if (!Directory.Exists(StorePath))
            {
                Directory.CreateDirectory(StorePath);
            }

            watcher = new FileSystemWatcher();
            watcher.Path = StorePath;
            watcher.NotifyFilter = NotifyFilters.LastAccess | NotifyFilters.LastWrite
                                   | NotifyFilters.FileName | NotifyFilters.DirectoryName;
            watcher.Filter = "*.dng";
            watcher.Changed += new FileSystemEventHandler(OnChanged);
            watcher.EnableRaisingEvents = true;

            Logger.WriteTraceMessage("watch " + StorePath);
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            var fileName = e.FullPath;

            Logger.WriteTraceMessage("onchanged " + fileName);

            var destinationFilePath = Path.ChangeExtension(Path.Combine(StorePath, Path.Combine(StorePath, _fileNameWaiting)), ".dng");

            Logger.WriteTraceMessage("onchanged dest " + destinationFilePath);

            File.Copy(fileName, destinationFilePath);
            File.Delete(fileName);
            if (ImageReady != null)
            {
                ImageReady(this, new ImageReadyEventArgs(destinationFilePath));
            }
            watcher.Changed -= OnChanged;
            watcher.EnableRaisingEvents = false;
            watcher = null;

            if ((File.Exists(destinationFilePath)) && (SaveFile == false))
            {
                File.Delete(destinationFilePath);
            }

        }

        private string GetAppPath()
        {
            string AppPath;
            AppPath = Assembly.GetExecutingAssembly().Location;
            AppPath = Path.GetDirectoryName(AppPath);

            return AppPath;
        }

        private Dictionary<string, string> ParseStatus(string status)
        {
            var result = new Dictionary<string, string>();

            using (StringReader sr = new StringReader(status))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var parts = line.Split(':').Select(p => p.Trim()).ToList();
                    if (parts.Count == 2)
                    {
                        result.Add(parts[0], parts[1]);
                    }
                }
            }

            return result;
        }

        public string ExecuteCommand(string args)
        {
            Logger.WriteTraceMessage("ExecuteCommand(), args = '" + args + "'");

            string exeDir = Path.Combine(GetAppPath(), "pktriggercord", "pktriggercord-cli.exe");
            ProcessStartInfo procStartInfo = new ProcessStartInfo();

            procStartInfo.FileName = exeDir;
            procStartInfo.Arguments = args + " --timeout 10";
            procStartInfo.RedirectStandardOutput = true;
            procStartInfo.UseShellExecute = false;
            procStartInfo.CreateNoWindow = true;
            Logger.WriteTraceMessage("about to start process with command = '" + procStartInfo.FileName + " " + procStartInfo.Arguments + "'");

            string result = string.Empty;
            using (Process process = new Process())
            {
                process.StartInfo = procStartInfo;
                process.Start();
                process.WaitForExit();

                result = process.StandardOutput.ReadToEnd();
                Logger.WriteTraceMessage("result of command = '" + result + "'");
            }
            //result = "pktriggercord-cli: K-5IIs Connected...";
            return result;
        }

        private void CallExposureFailed(string message, string stackTrace = null)
        {
            _waitingForImage = false;
            ExposureFailed?.Invoke(this, new ExposureFailedEventArgs(message, stackTrace));
        }

        public void StopExposure()
        {
            AbortExposure();
        }
    }
}
