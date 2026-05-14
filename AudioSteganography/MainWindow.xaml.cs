using Microsoft.Win32;
using NAudio.Wave;
using AudioSteganography.Logic;
using System;
using System.Windows;
using System.Windows.Media;
using ScottPlot.WPF;

namespace AudioSteganography
{
    public partial class MainWindow : Window
    {
        private string _originalWavPath;
        private string _stegoWavPath;

        private WaveOutEvent _waveOut;
        private AudioFileReader _audioReader;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAudio();
            base.OnClosed(e);
        }


        private void BrowseInputWav_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "WAV Аудіо (*.wav)|*.wav" };
            if (dlg.ShowDialog() == true)
            {
                InputWavTxt.Text = dlg.FileName;
                _originalWavPath = dlg.FileName;

                long capacity = AudioStegoService.CalculateCapacityInBytes(_originalWavPath);
                EmbedStatusLbl.Text = $"Максимальна місткість файлу: {capacity} символів.";
                EmbedStatusLbl.Foreground = Brushes.Blue;
            }
        }

        private void Embed_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(InputWavTxt.Text) || string.IsNullOrEmpty(SecretMessageTxt.Text))
            {
                ShowStatus("Оберіть файл та введіть повідомлення!", true);
                return;
            }

            var saveDlg = new SaveFileDialog
            {
                Filter = "WAV Аудіо (*.wav)|*.wav",
                FileName = "stego_output.wav"
            };

            if (saveDlg.ShowDialog() == true)
            {
                try
                {
                    _stegoWavPath = saveDlg.FileName;
                    string password = EmbedPasswordTxt.Text;
                    string secretText = SecretMessageTxt.Text;

                    AudioStegoService.EmbedData(InputWavTxt.Text, _stegoWavPath, secretText, password);

                    int usedBytes = System.Text.Encoding.UTF8.GetByteCount(secretText + '\0');
                    int usedBits = usedBytes * 8;

                    long capacityBytes = AudioStegoService.CalculateCapacityInBytes(InputWavTxt.Text);
                    long capacityBits = capacityBytes * 8;

                    double percentUsed = Math.Round((double)usedBytes / capacityBytes * 100, 4);

                    EmbedStatusLbl.Text = $"✅ Успішно збережено в: {System.IO.Path.GetFileName(_stegoWavPath)}\n" +
                                          $"📊 Використано: {usedBits} біт ({usedBytes} байт)\n" +
                                          $"💾 Загальна місткість: {capacityBits} біт ({capacityBytes} байт)\n" +
                                          $"📈 Файл заповнено на: {percentUsed}%";
                    EmbedStatusLbl.Foreground = Brushes.Green;

                    ExtractWavTxt.Text = _stegoWavPath; 
                }
                catch (Exception ex)
                {
                    ShowStatus($"Помилка: {ex.Message}", true);
                }
            }
        }

        private void ShowStatus(string message, bool isError)
        {
            EmbedStatusLbl.Text = message;
            EmbedStatusLbl.Foreground = isError ? Brushes.Red : Brushes.Green;
        }

        private void BrowseExtractWav_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "WAV Аудіо (*.wav)|*.wav" };
            if (dlg.ShowDialog() == true)
            {
                ExtractWavTxt.Text = dlg.FileName;
            }
        }

        private void Extract_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ExtractWavTxt.Text))
            {
                MessageBox.Show("Оберіть WAV файл для вилучення!", "Помилка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string password = ExtractPasswordTxt.Text;
                string secret = AudioStegoService.ExtractData(ExtractWavTxt.Text, password);

                ExtractedMessageTxt.Text = secret;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка вилучення: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PlotWaveforms_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_originalWavPath) || string.IsNullOrEmpty(_stegoWavPath))
            {
                MessageBox.Show("Для порівняння потрібно спочатку приховати повідомлення (щоб програма знала обидва файли).", "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                DrawWaveform(PlotOriginal, _originalWavPath, "Оригінальний сигнал", System.Drawing.Color.Blue);

                DrawWaveform(PlotStego, _stegoWavPath, "Стего-сигнал", System.Drawing.Color.Teal);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка при побудові графіка: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DrawWaveform(WpfPlot plotControl, string wavPath, string title, System.Drawing.Color color)
        {
            plotControl.Plot.Clear();

            using var reader = new AudioFileReader(wavPath);
            int sampleCount = (int)(reader.Length / 4);
            float[] floatSamples = new float[sampleCount];
            reader.Read(floatSamples, 0, sampleCount);

            double maxAmplitude = 0;
            double[] doubleSamples = new double[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                doubleSamples[i] = floatSamples[i]; 

                double absValue = Math.Abs(floatSamples[i]);
                if (absValue > maxAmplitude)
                {
                    maxAmplitude = absValue;
                }
            }

            var sig = plotControl.Plot.Add.Signal(doubleSamples);
            double sampleRate = reader.WaveFormat.SampleRate;
            sig.Data.Period = 1.0 / sampleRate;
            sig.Color = new ScottPlot.Color(color.R, color.G, color.B);

            double durationInSeconds = sampleCount / sampleRate;
            plotControl.Plot.Axes.SetLimitsX(0, durationInSeconds);

            double yLimit = maxAmplitude > 0 ? maxAmplitude * 1.05 : 1;
            plotControl.Plot.Axes.SetLimitsY(-yLimit, yLimit);

            plotControl.Plot.Axes.Title.Label.Text = title;
            plotControl.Plot.Axes.Bottom.Label.Text = "Час (секунди)";
            plotControl.Plot.Axes.Left.Label.Text = "Амплітуда";

            plotControl.Refresh();
        }

        private readonly SolidColorBrush _colorPlay = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#28A745")); // Зелений
        private readonly SolidColorBrush _colorStop = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC3545")); // Червоний

        private void BtnPlayOriginal_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayback(_originalWavPath, BtnPlayOriginal);
        }

        private void BtnPlayStego_Click(object sender, RoutedEventArgs e)
        {
            TogglePlayback(_stegoWavPath, BtnPlayStego);
        }

        private void TogglePlayback(string filePath, System.Windows.Controls.Button btn)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("Файл не знайдено! Спочатку побудуйте графіки.", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
            {
                StopAudio();
                return;
            }

            StopAudio();
            try
            {
                _audioReader = new AudioFileReader(filePath);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioReader);

                _waveOut.PlaybackStopped += (s, args) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ResetPlayButtons();
                        DisposeAudio();
                    });
                };

                _waveOut.Play();

                btn.Content = "⏹";
                btn.Background = _colorStop;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Помилка відтворення: " + ex.Message, "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetPlayButtons()
        {
            if (BtnPlayOriginal != null)
            {
                BtnPlayOriginal.Content = "▶";
                BtnPlayOriginal.Background = _colorPlay;
            }
            if (BtnPlayStego != null)
            {
                BtnPlayStego.Content = "▶";
                BtnPlayStego.Background = _colorPlay;
            }
        }

        private void StopAudio()
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
                DisposeAudio();
            }
            ResetPlayButtons();
        }

        private void DisposeAudio()
        {
            if (_waveOut != null) { _waveOut.Dispose(); _waveOut = null; }
            if (_audioReader != null) { _audioReader.Dispose(); _audioReader = null; }
        }
    }
}