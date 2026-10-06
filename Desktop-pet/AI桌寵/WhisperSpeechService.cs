using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Whisper.net;

namespace SmartDesktopPet
{
    public class WhisperSpeechService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _waveWriter;
        private readonly string _tempAudioFile;
        private WhisperFactory? _whisperFactory;

        public WhisperSpeechService(string modelPath)
        {
            _tempAudioFile = Path.Combine(Path.GetTempPath(), "pet_voice_input.wav");

            // 🌸 初始化 Whisper 模型（這步可能耗時幾秒，建議放在背景執行緒）
            _whisperFactory = WhisperFactory.FromPath(modelPath);
        }

        /// <summary>
        /// 開始錄音
        /// </summary>
        public void StartRecording()
        {
            // 🌸 設定為 16kHz, 單聲道, 16-bit，這是 Whisper 模型要求的格式
            _waveIn = new WaveInEvent
            {
                DeviceNumber = 0, // 預設麥克風
                WaveFormat = new WaveFormat(16000, 1), // 16000Hz, Mono
                BufferMilliseconds = 100
            };

            _waveWriter = new WaveFileWriter(_tempAudioFile, _waveIn.WaveFormat);

            _waveIn.DataAvailable += (s, e) =>
            {
                _waveWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            };

            _waveIn.StartRecording();
            Debug.WriteLine("[Whisper] 開始錄音...");
        }

        /// <summary>
        /// 停止錄音並辨識，回傳辨識出的文字
        /// </summary>
        public async Task<string> StopRecordingAndTranscribeAsync()
        {
            if (_waveIn == null) return string.Empty;

            _waveIn.StopRecording();
            _waveIn.Dispose();
            _waveWriter?.Dispose();
            _waveIn = null;
            _waveWriter = null;

            Debug.WriteLine("[Whisper] 錄音結束，開始辨識...");

            if (_whisperFactory == null || !File.Exists(_tempAudioFile))
                return string.Empty;

            // 🌸 使用 using 確保 processor 被正確釋放
            using var processor = _whisperFactory.CreateBuilder()
                .WithLanguage("zh") // 🌸 指定中文，大幅提升準確率
                .Build();

            var result = new System.Text.StringBuilder();

            using var fileStream = File.OpenRead(_tempAudioFile);
            await foreach (var segment in processor.ProcessAsync(fileStream))
            {
                result.Append(segment.Text);
            }

            // 刪除臨時檔案
            try { File.Delete(_tempAudioFile); } catch { }

            string text = result.ToString().Trim();
            Debug.WriteLine($"[Whisper] 辨識結果: {text}");
            return text;
        }

        public void Dispose()
        {
            _waveIn?.Dispose();
            _waveWriter?.Dispose();
            _whisperFactory?.Dispose();
        }
    }
}