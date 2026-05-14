using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace AudioSteganography.Logic
{
    public static class AudioStegoService
    {
        private const int BytesPer16BitSample = 2;
        private const int BitsPerByte = 8;
        private const byte SetLsbMask = 1;      // 00000001
        private const byte ClearLsbMask = 254;  // 11111110
        private const char EndOfMessageMarker = '\0';

        public static long CalculateCapacityInBytes(string inputFilePath)
        {
            using var reader = new WaveFileReader(inputFilePath);
            long totalSamples = reader.Length / BytesPer16BitSample;

            return totalSamples / BitsPerByte;
        }

        public static void EmbedData(string inputFilePath, string outputFilePath, string secretMessage, string secretKey = "")
        {
            using var reader = new WaveFileReader(inputFilePath);
            ValidateAudioFormat(reader);

            List<bool> messageBits = ConvertMessageToBits(secretMessage + EndOfMessageMarker);
            long totalSamples = reader.Length / BytesPer16BitSample;

            if (messageBits.Count > totalSamples)
            {
                throw new InvalidOperationException($"Файл замалий. Потрібно семплів: {messageBits.Count}, є: {totalSamples}");
            }

            byte[] audioData = ReadAudioData(reader);

            using var indexEnumerator = GetSampleIndexSequence(totalSamples, secretKey).GetEnumerator();

            foreach (bool bitValue in messageBits)
            {
                indexEnumerator.MoveNext();
                int sampleIndex = indexEnumerator.Current;

                int byteIndex = sampleIndex * BytesPer16BitSample;

                SetLeastSignificantBit(ref audioData[byteIndex], bitValue);
            }

            SaveAudioData(outputFilePath, reader.WaveFormat, audioData);
        }

        public static string ExtractData(string inputFilePath, string secretKey = "")
        {
            using var reader = new WaveFileReader(inputFilePath);
            ValidateAudioFormat(reader);

            long totalSamples = reader.Length / BytesPer16BitSample;
            byte[] audioData = ReadAudioData(reader);

            var extractedBytes = new List<byte>();
            int currentByte = 0;
            int collectedBitsCount = 0;

            using var indexEnumerator = GetSampleIndexSequence(totalSamples, secretKey).GetEnumerator();

            for (long i = 0; i < totalSamples; i++)
            {
                indexEnumerator.MoveNext();
                int sampleIndex = indexEnumerator.Current;
                int byteIndex = sampleIndex * BytesPer16BitSample;

                int extractedBit = GetLeastSignificantBit(audioData[byteIndex]);

                currentByte = (currentByte << 1) | extractedBit;
                collectedBitsCount++;

                if (collectedBitsCount == BitsPerByte)
                {
                    if (currentByte == EndOfMessageMarker)
                    {
                        break;
                    }

                    extractedBytes.Add((byte)currentByte);
                    currentByte = 0;
                    collectedBitsCount = 0;
                }
            }

            return Encoding.UTF8.GetString(extractedBytes.ToArray());
        }

        private static void ValidateAudioFormat(WaveFileReader reader)
        {
            if (reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm || reader.WaveFormat.BitsPerSample != 16)
            {
                throw new FormatException("Підтримуються тільки 16-бітні PCM WAV файли (стандарт).");
            }
        }

        private static byte[] ReadAudioData(WaveFileReader reader)
        {
            byte[] audioData = new byte[reader.Length];
            reader.Read(audioData, 0, audioData.Length);
            return audioData;
        }

        private static void SaveAudioData(string outputFilePath, WaveFormat format, byte[] audioData)
        {
            using var writer = new WaveFileWriter(outputFilePath, format);
            writer.Write(audioData, 0, audioData.Length);
        }

        private static List<bool> ConvertMessageToBits(string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            var bits = new List<bool>(bytes.Length * BitsPerByte);

            foreach (byte b in bytes)
            {
                for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
                {
                    bool bitValue = ((b >> bitIndex) & 1) == 1;
                    bits.Add(bitValue);
                }
            }

            return bits;
        }

        private static void SetLeastSignificantBit(ref byte targetByte, bool bitValue)
        {
            if (bitValue)
            {
                targetByte |= SetLsbMask;
            }
            else
            {
                targetByte &= ClearLsbMask;
            }
        }

        private static int GetLeastSignificantBit(byte sourceByte)
        {
            return sourceByte & 1;
        }

        private static IEnumerable<int> GetSampleIndexSequence(long maxIndex, string secretKey)
        {
            if (string.IsNullOrEmpty(secretKey))
            {
                return GenerateSequentialIndices(maxIndex);
            }

            return GenerateRandomIndices(maxIndex, secretKey);
        }

        private static IEnumerable<int> GenerateSequentialIndices(long maxIndex)
        {
            for (int i = 0; i < maxIndex; i++)
            {
                yield return i;
            }
        }

        private static IEnumerable<int> GenerateRandomIndices(long maxIndex, string secretKey)
        {
            int randomSeed = GenerateDeterministicSeed(secretKey);
            Random randomGenerator = new Random(randomSeed);
            HashSet<int> usedIndices = new HashSet<int>();

            for (long i = 0; i < maxIndex; i++)
            {
                int randomIndex;

                do
                {
                    randomIndex = randomGenerator.Next(0, (int)maxIndex);
                }
                while (!usedIndices.Add(randomIndex));

                yield return randomIndex;
            }
        }

        private static int GenerateDeterministicSeed(string secretKey)
        {
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(secretKey));

            return BitConverter.ToInt32(hashBytes, 0);
        }
    }
}