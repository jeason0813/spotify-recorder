﻿using System.ComponentModel;
using System.Configuration;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace SpotifyRecorder.Forms.UI
{
    public partial class MainForm : Form
    {
        private int fileExistCounter = 0;

        private enum ApplicationState
        {
            NotRecording = 1,
            WaitingForRecording = 2,
            Recording = 3,
            Closing = 4
        }

        private SoundCardRecorder SoundCardRecorder { get; set; }

        private Timer songTicker;
        private FolderBrowserDialog folderDialog;
        private ApplicationState _currentApplicationState = ApplicationState.NotRecording;

        public MainForm()
        {
            InitializeComponent();

            //check if it is windows 7
            if (Environment.OSVersion.Version.Major < 6)
            {
                MessageBox.Show("This application is optimized for windows 7");
                Close();
            }

            Load += OnLoad;
            Closing += OnClosing;
        }

        private void ChangeApplicationState(ApplicationState newState)
        {
            ChangeGui(newState);

            switch (_currentApplicationState)
            {
                case ApplicationState.NotRecording:
                    switch (newState)
                    {
                        case ApplicationState.NotRecording:
                            break;
                        case ApplicationState.WaitingForRecording:
                            break;
                        case ApplicationState.Recording:
                            StartRecording((MMDevice)deviceListBox.SelectedItem, songLabel.Text);
                            break;
                        case ApplicationState.Closing:
                            break;
                    }

                    break;
                case ApplicationState.WaitingForRecording:
                    switch (newState)
                    {
                        case ApplicationState.NotRecording:
                            break;
                        case ApplicationState.WaitingForRecording:
                            throw new Exception(string.Format("NY {0} - {1}", _currentApplicationState, newState));
                        case ApplicationState.Recording:
                            StartRecording((MMDevice)deviceListBox.SelectedItem, songLabel.Text);
                            break;
                        case ApplicationState.Closing:
                            Close();
                            break;
                    }
                    break;
                case ApplicationState.Recording:
                    switch (newState)
                    {
                        case ApplicationState.NotRecording:
                            StopRecording();
                            break;
                        case ApplicationState.Recording: //file changed
                            StopRecording();
                            StartRecording((MMDevice)deviceListBox.SelectedItem, songLabel.Text);
                            break;
                        case ApplicationState.WaitingForRecording: //file changed
                            StopRecording();
                            break;
                    }
                    break;

            }
            _currentApplicationState = newState;
        }

        private void ChangeGui(ApplicationState state)
        {
            switch (state)
            {
                case ApplicationState.NotRecording:
                    browseButton.Enabled = true;
                    buttonStartRecording.Enabled = true;
                    buttonStopRecording.Enabled = false;
                    bitrateComboBox.Enabled = true;
                    deviceListBox.Enabled = true;
                    thresholdCheckBox.Enabled = true;
                    thresholdTextBox.Enabled = true;
                    break;
                case ApplicationState.WaitingForRecording:
                    browseButton.Enabled = false;
                    buttonStartRecording.Enabled = false;
                    buttonStopRecording.Enabled = true;
                    bitrateComboBox.Enabled = false;
                    deviceListBox.Enabled = false;
                    thresholdCheckBox.Enabled = false;
                    thresholdTextBox.Enabled = false;
                    break;
                case ApplicationState.Recording:
                    browseButton.Enabled = false;
                    buttonStartRecording.Enabled = false;
                    buttonStopRecording.Enabled = true;
                    bitrateComboBox.Enabled = false;
                    deviceListBox.Enabled = false;
                    thresholdCheckBox.Enabled = false;
                    thresholdTextBox.Enabled = false;
                    break;

            }

        }
        /// <summary>
        /// After the form is created
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void OnLoad(object sender, EventArgs eventArgs)
        {
            //initial loading
            //load the available devices
            LoadWasapiDevicesCombo();

            //load the different bitrates
            LoadBitrateCombo();

            //Load user settings
            LoadUserSettings();

            //check if spotify title is changing
            songTicker = new Timer { Interval = Settings.Default.SongscanInterval };
            songTicker.Tick += SongTickerTick;
            songTicker.Start();

            //set the change event if filePath is 
            songLabel.Text = string.Empty;

            folderDialog = new FolderBrowserDialog { SelectedPath = outputFolderTextBox.Text };

            versionLabel.Text = string.Format("Version {0}", Application.ProductVersion);

            ChangeApplicationState(_currentApplicationState);
        }

        /// <summary>
        /// When the application is closing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="cancelEventArgs"></param>
        private void OnClosing(object sender, CancelEventArgs cancelEventArgs)
        {
            ChangeApplicationState(ApplicationState.Closing);
            Util.SetDefaultBitrate((int)bitrateComboBox.SelectedItem);
            Util.SetDefaultDevice(deviceListBox.SelectedItem.ToString());
            Util.SetDefaultOutputPath(outputFolderTextBox.Text);
            Util.SetDefaultThreshold((int)thresholdTextBox.Value);
            Util.SetDefaultThreshold((int)thresholdTextBox.Value);
            Util.SetDefaultThresholdEnabled(thresholdCheckBox.Checked);
        }

        void SongTickerTick(object sender, EventArgs e)
        {
            //get the current title from spotify
            string song = GetSpotifySong();

            if (!songLabel.Text.Equals(song))
            {
                fileExistCounter = 0;
                songLabel.Text = song;
                if (songLabel.Text.Trim().Length > 0)
                {
                    if (_currentApplicationState != ApplicationState.NotRecording)
                        ChangeApplicationState(ApplicationState.Recording);
                    else if (_currentApplicationState == ApplicationState.Recording)
                        ChangeApplicationState(ApplicationState.WaitingForRecording);
                }
                else
                {
                    if (_currentApplicationState == ApplicationState.Recording)
                        ChangeApplicationState(ApplicationState.WaitingForRecording);

                }
            }
        }


        private void ButtonPlayClick(object sender, EventArgs e)
        {
            if (listBoxRecordings.SelectedItem != null)
            {
                Process.Start(CreateOutputFile((string)listBoxRecordings.SelectedItem, "mp3"));
            }
        }

        private void ButtonDeleteClick(object sender, EventArgs e)
        {
            if (listBoxRecordings.SelectedItem != null)
            {
                try
                {
                    File.Delete(CreateOutputFile((string)listBoxRecordings.SelectedItem, "mp3"));
                    listBoxRecordings.Items.Remove(listBoxRecordings.SelectedItem);
                    if (listBoxRecordings.Items.Count > 0)
                    {
                        listBoxRecordings.SelectedIndex = 0;
                    }
                }
                catch (Exception)
                {
                    MessageBox.Show("Could not delete recording");
                }
            }
        }

        private void ButtonOpenFolderClick(object sender, EventArgs e)
        {
            Process.Start(outputFolderTextBox.Text);
        }

        private void ButtonStartRecordingClick(object sender, EventArgs e)
        {
            ChangeApplicationState(songLabel.Text.Trim().Length > 0
                                       ? ApplicationState.Recording
                                       : ApplicationState.WaitingForRecording);
        }

        private void ButtonStopRecordingClick(object sender, EventArgs e)
        {
            ChangeApplicationState(ApplicationState.NotRecording);
        }

        private void ClearButtonClick(object sender, EventArgs e)
        {
            listBoxRecordings.Items.Clear();
        }

        private string CreateOutputFile(string song, string extension)
        {
            song = RemoveInvalidFilePathCharacters(song, string.Empty);
            return Path.Combine(outputFolderTextBox.Text, string.Format("{0}.{1}", song, extension));
        }

        private void StartRecording(MMDevice device, string song)
        {
            if (!string.IsNullOrEmpty(song) && device != null)
            {
                if (SoundCardRecorder != null)
                    StopRecording();

                CalculateCurrentFileExistCounter(song);

                if (File.Exists(CreateOutputFile(song, "mp3")) || File.Exists(CreateOutputFile(song + "_old" + fileExistCounter, "mp3")))
                {
                    File.Move(CreateOutputFile(song, "mp3"), CreateOutputFile(song + "_old" + fileExistCounter, "mp3"));
                    fileExistCounter += 1;
                }

                SoundCardRecorder = new SoundCardRecorder(
                                device, CreateOutputFile(song, "wav"),
                                songLabel.Text);
                SoundCardRecorder.Start();
            }
        }

        private void CalculateCurrentFileExistCounter(string song)
        {
            var AllSongsInDir = Directory.GetFiles(outputFolderTextBox.Text, "*.*", SearchOption.AllDirectories).Where(s => s.Contains(song));
            if (AllSongsInDir.Count() > 0) fileExistCounter = AllSongsInDir.Count() - 1;
        }


        private void StopRecording()
        {
            string filePath = string.Empty;
            string song = string.Empty;
            TimeSpan duration = new TimeSpan();
            if (SoundCardRecorder != null)
            {
                SoundCardRecorder.Stop();
                filePath = SoundCardRecorder.FilePath;
                song = SoundCardRecorder.Song;
                duration = SoundCardRecorder.Duration;
                SoundCardRecorder.Dispose();
                SoundCardRecorder = null;

                if (duration.TotalSeconds < (int)thresholdTextBox.Value && thresholdCheckBox.Checked)
                    File.Delete(filePath);
                else
                {
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        int newItemIndex;
                        if (fileExistCounter > 0)
                        {
                            newItemIndex = listBoxRecordings.Items.Add(song + " (file already exist...old renamed!)");
                        }
                        else
                        {
                            newItemIndex = listBoxRecordings.Items.Add(song);
                        }

                        listBoxRecordings.SelectedIndex = newItemIndex;
                        PostProcessing(song);
                    }
                }
            }

        }


        private void ConvertToMp3(string filePath, int bitrate)
        {
            if (!File.Exists(CreateOutputFile(filePath, "wav")))
                return;

            //Thread.Sleep(500);
            Process process = new Process();
            process.StartInfo.UseShellExecute = false;
            //process.StartInfo.RedirectStandardOutput = true;
            //process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;

            Mp3Tag tag = Util.ExtractMp3Tag(filePath);

            process.StartInfo.FileName = "lame.exe";
            process.StartInfo.Arguments = string.Format("-b {2} --tt \"{3}\" --ta \"{4}\"  \"{0}\" \"{1}\"",
                CreateOutputFile(filePath, "wav"),
                CreateOutputFile(filePath, "mp3"),
                bitrate,
                tag.Title,
                tag.Artist);

            process.StartInfo.WorkingDirectory = new FileInfo(Application.ExecutablePath).DirectoryName;
            process.Start();
            process.WaitForExit(20000);
            if (!process.HasExited)
                process.Kill();
            File.Delete(CreateOutputFile(filePath, "wav"));
        }

        private void LoadWasapiDevicesCombo()
        {
            var deviceEnum = new MMDeviceEnumerator();
            var devices = deviceEnum.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();

            deviceListBox.DataSource = devices;
            deviceListBox.DisplayMember = "FriendlyName";
        }
        private void LoadBitrateCombo()
        {
            List<int> bitrate = new List<int> { 96, 128, 160, 192, 320 };

            bitrateComboBox.DataSource = bitrate;
        }

        /// <summary>
        /// load the setting from a previous session
        /// </summary>
        private void LoadUserSettings()
        {
            //get/set the device
            string defaultDevice = Util.GetDefaultDevice();

            foreach (MMDevice device in deviceListBox.Items)
            {
                if (device.FriendlyName.Equals(defaultDevice))
                    deviceListBox.SelectedItem = device;
            }

            //set the default output to the music directory
            outputFolderTextBox.Text = Util.GetDefaultOutputPath();

            //set the default bitrate
            bitrateComboBox.SelectedItem = Util.GetDefaultBitrate();

            thresholdTextBox.Value = Util.GetDefaultThreshold();
            thresholdCheckBox.Checked = Util.GetDefaultThresholdEnabled();

        }

        private string GetSpotifySong()
        {
            Process[] process = Process.GetProcessesByName("spotify");
            if (process != null && process.Length > 0)
            {
                var spotifyProcessesCount = Process.GetProcessesByName("spotify").Count();
                var song = string.Empty;

                for (int p = 0; p < spotifyProcessesCount; p++)
                {
                    song = Process.GetProcessesByName("spotify")[p].MainWindowTitle;
                    if (!string.IsNullOrEmpty(song)) break;
                }

                return song.Length > 7 ? song.Trim() : string.Empty;
            }
            return string.Empty;

        }

        private void PostProcessing(string song)
        {
            int bitrate = (int)bitrateComboBox.SelectedItem;
            Task t = new Task(() => ConvertToMp3(song, bitrate));
            t.Start();
        }


        private void DonateLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            MessageBox.Show("Donations for the work done and future work are welcome.\r\nMy paypal account is paypal@atriumstede.nl",
                "Donation");
        }
        public static string RemoveInvalidFilePathCharacters(string filename, string replaceChar)
        {
            string regexSearch = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            Regex r = new Regex(string.Format("[{0}]", Regex.Escape(regexSearch)));
            return r.Replace(filename, replaceChar);
        }

        private void HelpLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("http://spotifyrecorder2.codeplex.com");

        }

        private void BrowseButtonClick(object sender, EventArgs e)
        {
            if (folderDialog.ShowDialog() == DialogResult.OK)
            {
                outputFolderTextBox.Text = folderDialog.SelectedPath;
                Util.SetDefaultOutputPath(folderDialog.SelectedPath);
            }
        }

        private void OpenMixerButtonClick(object sender, EventArgs e)
        {
            Process.Start("sndvol");
        }

        private void linkLabel2_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

        }
    }
}
