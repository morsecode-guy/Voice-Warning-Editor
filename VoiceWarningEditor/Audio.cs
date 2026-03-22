using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VoiceWarningEditor
{
    // audio playback and missile alarm stuff
    public partial class VoiceWarningEditorMod
    {
        // parse wav header to get duration :3
        private float GetWavDuration(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var br = new BinaryReader(fs);
                if (fs.Length < 44) return 1.0f;

                br.ReadBytes(22);
                int channels = br.ReadInt16();
                int sampleRate = br.ReadInt32();
                br.ReadBytes(6);
                int bitsPerSample = br.ReadInt16();

                fs.Position = 12;
                while (fs.Position + 8 < fs.Length)
                {
                    string chunkId = new string(br.ReadChars(4));
                    int chunkSize = br.ReadInt32();
                    if (chunkId == "data")
                    {
                        int bytesPerSample = bitsPerSample / 8;
                        int totalSamples = chunkSize / bytesPerSample;
                        int samplesPerChannel = totalSamples / Math.Max(channels, 1);
                        return (float)samplesPerChannel / Math.Max(sampleRate, 1);
                    }
                    fs.Position += chunkSize;
                    if (fs.Position % 2 != 0) fs.Position++;
                }
            }
            catch { }
            return 1.0f;
        }

        // find all wav files in the data folder
        internal void IndexAudioClips()
        {
            _clipPaths.Clear();
            _clipDurations.Clear();
            _clipsReady = false;

            if (!Directory.Exists(_dataFolderPath))
            {
                LoggerInstance.Warning($"Sound folder not found: {_dataFolderPath}");
                _clipsReady = true;
                return;
            }

            string[] wavFiles = Directory.GetFiles(_dataFolderPath, "*.wav");
            LoggerInstance.Msg($"Found {wavFiles.Length} .wav files in {_dataFolderPath}");

            foreach (string filePath in wavFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string fullPath = Path.GetFullPath(filePath);
                float duration = GetWavDuration(fullPath);

                _clipPaths[fileName] = fullPath;
                _clipDurations[fileName] = duration;
                LoggerInstance.Msg($"  Indexed: {fileName} ({duration:F2}s) -> {fullPath}");
            }

            _clipsReady = true;
            LoggerInstance.Msg($"[Audio] {_clipPaths.Count} clips indexed and ready");
        }

        // play a wav through winmm, per-craft overrides first
        internal void PlayWarningSound(string clipName)
        {
            string filePath = GetSoundPath(clipName);
            if (filePath == null) return;

            float duration = _clipDurations.ContainsKey(clipName) ? _clipDurations[clipName] : 1.0f;
            if (_craftOverrides.ContainsKey(clipName))
                duration = GetWavDuration(filePath);

            try
            {
                bool result = PlaySound(filePath, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
                _lastPlayTime = Time.realtimeSinceStartup;
                _currentClipLength = duration;

                string source = _craftOverrides.ContainsKey(clipName) ? "override" : "default";
                LoggerInstance.Msg($"[Play] '{clipName}' ({duration:F2}s) [{source}] result={result}");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Play] Failed '{clipName}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        // stop current winmm sound
        private void StopCurrentAudio()
        {
            try { PlaySound(null, IntPtr.Zero, SND_PURGE); } catch { }
        }

        // clean up everything
        private void Cleanup()
        {
            StopCurrentAudio();
            StopMissileAlarm();
            _ourSignature = null;
            _warningCooldowns.Clear();
            _warningQueue.Clear();
        }

        // check if something is still playing (winmm has no status api lol)
        internal bool IsAudioPlaying()
        {
            return Time.realtimeSinceStartup - _lastPlayTime < _currentClipLength;
        }

        // fire a warning with cooldown
        internal void TriggerWarning(string clipName, float cooldown)
        {
            if (_warningCooldowns.ContainsKey(clipName)) return;
            if (GetSoundPath(clipName) == null) return;
            if (!_clipsReady) return;

            LoggerInstance.Msg($"[Trigger] Warning: '{clipName}' (cooldown: {cooldown}s)");

            if (!IsAudioPlaying())
                PlayWarningSound(clipName);
            else if (!_warningQueue.Contains(clipName))
            {
                _warningQueue.Enqueue(clipName);
                LoggerInstance.Msg($"[Trigger] Queued '{clipName}' (queue: {_warningQueue.Count})");
            }

            _warningCooldowns[clipName] = cooldown;
        }

        // save it for later
        private void QueueWarning(string clipName, float cooldown)
        {
            if (_warningCooldowns.ContainsKey(clipName)) return;
            if (GetSoundPath(clipName) == null) return;
            if (!_warningQueue.Contains(clipName))
                _warningQueue.Enqueue(clipName);
            _warningCooldowns[clipName] = cooldown;
        }

        // gapless missile alarm via waveout hardware loop >:(
        internal void StartMissileAlarm()
        {
            if (_missileAlarmPlaying) return;

            string filePath = GetSoundPath("missile_lauch_f18");
            if (filePath == null) return;

            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                if (fileData.Length < 44)
                {
                    LoggerInstance.Warning("[waveOut] Missile alarm WAV too small");
                    return;
                }

                int channels = BitConverter.ToInt16(fileData, 22);
                int sampleRate = BitConverter.ToInt32(fileData, 24);
                int bitsPerSample = BitConverter.ToInt16(fileData, 34);

                // find the pcm data chunk
                int dataOffset = 12;
                int dataSize = 0;
                while (dataOffset + 8 < fileData.Length)
                {
                    string chunkId = System.Text.Encoding.ASCII.GetString(fileData, dataOffset, 4);
                    int chunkSize = BitConverter.ToInt32(fileData, dataOffset + 4);
                    if (chunkId == "data")
                    {
                        dataOffset += 8;
                        dataSize = Math.Min(chunkSize, fileData.Length - dataOffset);
                        break;
                    }
                    dataOffset += 8 + chunkSize;
                    if (dataOffset % 2 != 0) dataOffset++;
                }

                if (dataSize <= 0)
                {
                    LoggerInstance.Warning("[waveOut] No data chunk in missile alarm");
                    return;
                }

                var fmt = new WAVEFORMATEX
                {
                    wFormatTag = 1,
                    nChannels = (ushort)channels,
                    nSamplesPerSec = (uint)sampleRate,
                    wBitsPerSample = (ushort)bitsPerSample,
                    nBlockAlign = (ushort)(channels * bitsPerSample / 8),
                    nAvgBytesPerSec = (uint)(sampleRate * channels * bitsPerSample / 8),
                    cbSize = 0,
                };

                int err = waveOutOpen(out _waveOutHandle, WAVE_MAPPER, ref fmt, IntPtr.Zero, IntPtr.Zero, CALLBACK_NULL);
                if (err != 0)
                {
                    LoggerInstance.Warning($"[waveOut] waveOutOpen failed (err={err})");
                    return;
                }

                _pcmDataPtr = Marshal.AllocHGlobal(dataSize);
                Marshal.Copy(fileData, dataOffset, _pcmDataPtr, dataSize);

                int hdrSize = Marshal.SizeOf<WAVEHDR>();
                _waveHdrPtr = Marshal.AllocHGlobal(hdrSize);

                var hdr = new WAVEHDR
                {
                    lpData = _pcmDataPtr,
                    dwBufferLength = (uint)dataSize,
                    dwFlags = WHDR_BEGINLOOP | WHDR_ENDLOOP,
                    dwLoops = 0x7FFFFFFF,
                };
                Marshal.StructureToPtr(hdr, _waveHdrPtr, false);

                err = waveOutPrepareHeader(_waveOutHandle, _waveHdrPtr, hdrSize);
                if (err != 0)
                {
                    LoggerInstance.Warning($"[waveOut] PrepareHeader failed (err={err})");
                    CleanupWaveOut();
                    return;
                }

                err = waveOutWrite(_waveOutHandle, _waveHdrPtr, hdrSize);
                if (err != 0)
                {
                    LoggerInstance.Warning($"[waveOut] Write failed (err={err})");
                    waveOutUnprepareHeader(_waveOutHandle, _waveHdrPtr, hdrSize);
                    CleanupWaveOut();
                    return;
                }

                _missileAlarmPlaying = true;
                LoggerInstance.Msg($"[waveOut] Missile alarm started ({channels}ch {sampleRate}Hz {bitsPerSample}bit, {dataSize}B)");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[waveOut] Missile alarm error: {ex.Message}");
                CleanupWaveOut();
            }
        }

        // stop the alarm and free waveout
        internal void StopMissileAlarm()
        {
            if (!_missileAlarmPlaying) return;
            try
            {
                waveOutReset(_waveOutHandle);
                int hdrSize = Marshal.SizeOf<WAVEHDR>();
                waveOutUnprepareHeader(_waveOutHandle, _waveHdrPtr, hdrSize);
                CleanupWaveOut();
                _missileAlarmPlaying = false;
                LoggerInstance.Msg("[waveOut] Missile alarm stopped");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[waveOut] Stop error: {ex.Message}");
                _missileAlarmPlaying = false;
            }
        }

        // free unmanaged memory
        private void CleanupWaveOut()
        {
            try
            {
                if (_waveOutHandle != IntPtr.Zero) { waveOutClose(_waveOutHandle); _waveOutHandle = IntPtr.Zero; }
                if (_pcmDataPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_pcmDataPtr); _pcmDataPtr = IntPtr.Zero; }
                if (_waveHdrPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_waveHdrPtr); _waveHdrPtr = IntPtr.Zero; }
            }
            catch { }
        }
    }
}
