using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace RiotAutoLogin.Controls
{
    public partial class AlertSoundPicker : UserControl
    {
        private string _alertName = "Alert sound";
        private string _selectedPath = string.Empty;

        public event EventHandler? SoundChanged;
        public event EventHandler? PreviewRequested;

        public AlertSoundPicker()
        {
            InitializeComponent();
            UpdateDisplay();
        }

        public string AlertName
        {
            get => _alertName;
            set
            {
                _alertName = string.IsNullOrWhiteSpace(value) ? "Alert sound" : value;
                UpdateDisplay();
            }
        }

        public string SelectedPath
        {
            get => _selectedPath;
            set
            {
                _selectedPath = value ?? string.Empty;
                UpdateDisplay();
            }
        }

        private void BrowseSound_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Choose sound for {_alertName}",
                Filter = "Media files (*.mp4;*.mp3;*.wav;*.m4a;*.wma;*.aac)|*.mp4;*.mp3;*.wav;*.m4a;*.wma;*.aac|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            SelectedPath = dialog.FileName;
            SoundChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ClearSound_Click(object sender, RoutedEventArgs e)
        {
            SelectedPath = string.Empty;
            SoundChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PreviewSound_Click(object sender, RoutedEventArgs e)
        {
            PreviewRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateDisplay()
        {
            if (txtAlertName == null || txtSelectedFile == null || btnDefaultSound == null)
                return;

            bool usesDefault = string.IsNullOrWhiteSpace(_selectedPath);
            bool selectedFileExists = !usesDefault && File.Exists(_selectedPath);
            txtAlertName.Text = _alertName;
            txtSelectedFile.Text = usesDefault
                ? "Default system sound"
                : selectedFileExists
                    ? Path.GetFileName(_selectedPath)
                    : $"{Path.GetFileName(_selectedPath)} (missing)";
            txtSelectedFile.ToolTip = usesDefault
                ? "The built-in Windows sound will be used."
                : _selectedPath;
            btnDefaultSound.IsEnabled = !usesDefault;
        }
    }
}
