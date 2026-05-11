using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Il2Cpp;
using UnityEngine;
using UnityEngine.Audio;

namespace VoiceWarningEditor
{
    // audio playback and missile alarm stuff
    public partial class VoiceWarningEditorMod
    {
        // platform detection
        private bool _useWinmm;
        private GameObject _audioGO;
        private AudioSource _voiceSource;
        private AudioSource _alarmSource;
        private Dictionary<string, AudioClip> _unityClipCache = new Dictionary<string, AudioClip>();

        // mute toggle
        private bool _vwsMuted;
        private bool _mixerGroupLinked;
        private float _lastBackendVolume = -1f;
        private float _nextVolumeSyncTime;
        private const float VOLUME_SYNC_INTERVAL = 0.10f;

        // on-screen notification
        private float _notificationTimer;
        private string _notificationText;
        private GUIStyle _notificationStyle;

        [DllImport("winmm.dll")]
        private static extern int waveOutSetVolume(IntPtr hwo, uint dwVolume);

        // detect if we're running under wine/proton
        private bool DetectWine()
        {
            try
            {
                // wine adds this function to ntdll, native windows doesnt have it
                IntPtr ver = wine_get_version();
                string wineVer = Marshal.PtrToStringAnsi(ver);
                LoggerInstance.Msg($"[Audio] Detected Wine/Proton: {wineVer}");
                return true;
            }
            catch { }
            return false;
        }

        [DllImport("ntdll.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr wine_get_version();

        // set up audio backend
        private void InitAudioBackend()
        {
            _useWinmm = DetectWine();
            if (_useWinmm)
            {
                LoggerInstance.Msg("[Audio] Wine/Proton detected: using Unity mixer-first playback (winmm fallback)");
                try
                {
                    PlaySound(null, IntPtr.Zero, SND_PURGE);
                    LoggerInstance.Msg("[Audio] winmm.dll available");
                }
                catch (Exception ex)
                {
                    LoggerInstance.Error($"[Audio] winmm.dll not available: {ex.Message}");
                }

                // Always create Unity sources so Master Volume/mixer routing works on Wine/Proton too.
                SetupUnityAudio();
            }
            else
            {
                LoggerInstance.Msg("[Audio] Using Unity AudioSource (Windows) for playback");
                SetupUnityAudio();
            }
        }

        // create hidden audio sources for unity playback
        private void SetupUnityAudio()
        {
            try
            {
                _audioGO = new GameObject("VWS_Audio");
                UnityEngine.Object.DontDestroyOnLoad(_audioGO);
                _audioGO.hideFlags = HideFlags.HideAndDontSave;

                _voiceSource = _audioGO.AddComponent<AudioSource>();
                _voiceSource.playOnAwake = false;
                _voiceSource.spatialBlend = 0f;
                _voiceSource.volume = 1f;

                _alarmSource = _audioGO.AddComponent<AudioSource>();
                _alarmSource.playOnAwake = false;
                _alarmSource.spatialBlend = 0f;
                _alarmSource.volume = 1f;
                _alarmSource.loop = true;

                LoggerInstance.Msg("[Audio] Unity AudioSources created");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Audio] Failed to create AudioSources: {ex.Message}");
            }
        }

        // link our audio sources to the game's mixer so they follow the volume slider
        private void TryLinkMixerGroup()
        {
            if (_mixerGroupLinked) return;
            try
            {
                var mgr = AudioManager.instance;
                if (mgr == null) return;
                var group = mgr.masterGroup;
                if (group == null) return;

                if (_voiceSource != null) _voiceSource.outputAudioMixerGroup = group;
                if (_alarmSource != null) _alarmSource.outputAudioMixerGroup = group;
                _mixerGroupLinked = true;
                LoggerInstance.Msg("[Audio] Linked VWS audio to game mixer group");
            }
            catch { }
        }

        // keep backend output scaled to game master volume
        private void UpdateBackendVolume()
        {
            if (Time.realtimeSinceStartup < _nextVolumeSyncTime) return;
            _nextVolumeSyncTime = Time.realtimeSinceStartup + VOLUME_SYNC_INTERVAL;

            float target = GetGameMasterVolume();
            if (_vwsMuted) target = 0f;
            target = Mathf.Clamp01(target);

            if (Mathf.Abs(target - _lastBackendVolume) < 0.01f)
                return;

            _lastBackendVolume = target;

            try
            {
                if (_voiceSource != null) _voiceSource.volume = target;
                if (_alarmSource != null) _alarmSource.volume = target;

                if (_useWinmm)
                {
                    uint perChannel = (uint)Mathf.Clamp(Mathf.RoundToInt(target * 65535f), 0, 65535);
                    uint packed = perChannel | (perChannel << 16);
                    waveOutSetVolume(_waveOutHandle != IntPtr.Zero ? _waveOutHandle : IntPtr.Zero, packed);
                }
            }
            catch { }
        }

        // resolve master volume with soft reflection to stay compatible across game updates
        private float GetGameMasterVolume()
        {
            try
            {
                var mgr = AudioManager.instance;
                if (mgr == null) return 1f;

                var t = mgr.GetType();
                string[] names = { "masterVolume", "MasterVolume", "Volume", "volume", "sfxVolume", "musicVolume" };

                for (int i = 0; i < names.Length; i++)
                {
                    string name = names[i];

                    try
                    {
                        var prop = t.GetProperty(name);
                        if (prop != null)
                        {
                            object val = prop.GetValue(mgr, null);
                            if (val is float f) return f;
                            if (val is double d) return (float)d;
                        }
                    }
                    catch { }

                    try
                    {
                        var field = t.GetField(name);
                        if (field != null)
                        {
                            object val = field.GetValue(mgr);
                            if (val is float f) return f;
                            if (val is double d) return (float)d;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return 1f;
        }

        // toggle mute on/off
        private void ToggleMute()
        {
            _vwsMuted = !_vwsMuted;

            if (_vwsMuted)
            {
                // stop everything immediately
                StopCurrentAudio();
                StopMissileAlarm();
                _warningQueue.Clear();
            }

            ShowNotification(_vwsMuted ? "VWS: Muted" : "VWS: Unmuted");
            LoggerInstance.Msg($"[Audio] VWS {(_vwsMuted ? "muted" : "unmuted")}");
        }

        // show a temporary on-screen notification
        private void ShowNotification(string text)
        {
            _notificationText = text;
            _notificationTimer = 2.0f;
        }

        // draw notification (called from OnGUI)
        private void DrawNotification()
        {
            if (_notificationTimer <= 0f) return;

            if (_notificationStyle == null)
            {
                _notificationStyle = new GUIStyle(GUI.skin.box);
                _notificationStyle.fontSize = 22;
                _notificationStyle.fontStyle = FontStyle.Bold;
                _notificationStyle.alignment = TextAnchor.MiddleCenter;
                _notificationStyle.normal.textColor = Color.white;
            }

            // fade out in last 0.5s
            float alpha = Mathf.Clamp01(_notificationTimer / 0.5f);
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            float w = 280f;
            float h = 50f;
            float x = (Screen.width - w) / 2f;
            float y = Screen.height * 0.15f;

            GUI.Box(new Rect(x, y, w, h), _notificationText, _notificationStyle);
            GUI.color = prevColor;
        }

        // load a wav file into a unity AudioClip from raw pcm
        private AudioClip LoadWavAsClip(string filePath, string clipName)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(filePath);
                if (fileData.Length < 44) return null;

                int channels = BitConverter.ToInt16(fileData, 22);
                int sampleRate = BitConverter.ToInt32(fileData, 24);
                int bitsPerSample = BitConverter.ToInt16(fileData, 34);

                // find the data chunk
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

                if (dataSize <= 0) return null;

                // convert pcm bytes to float samples
                int bytesPerSample = bitsPerSample / 8;
                int totalSamples = dataSize / bytesPerSample;
                int samplesPerChannel = totalSamples / Math.Max(channels, 1);

                float[] floatData = new float[totalSamples];
                for (int i = 0; i < totalSamples; i++)
                {
                    int offset = dataOffset + i * bytesPerSample;
                    if (offset + bytesPerSample > fileData.Length) break;

                    if (bitsPerSample == 16)
                        floatData[i] = BitConverter.ToInt16(fileData, offset) / 32768f;
                    else if (bitsPerSample == 8)
                        floatData[i] = (fileData[offset] - 128) / 128f;
                    else if (bitsPerSample == 24)
                    {
                        int val = fileData[offset] | (fileData[offset + 1] << 8) | (fileData[offset + 2] << 16);
                        if (val >= 0x800000) val -= 0x1000000;
                        floatData[i] = val / 8388608f;
                    }
                    else if (bitsPerSample == 32)
                        floatData[i] = BitConverter.ToInt32(fileData, offset) / 2147483648f;
                }

                var clip = AudioClip.Create(clipName, samplesPerChannel, channels, sampleRate, false);
                clip.SetData(floatData, 0);
                return clip;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Audio] Failed to load clip '{clipName}': {ex.Message}");
                return null;
            }
        }

        // get or cache a unity AudioClip
        private AudioClip GetUnityClip(string clipName, string filePath)
        {
            if (_unityClipCache.TryGetValue(filePath, out var cached) && cached != null)
                return cached;

            var clip = LoadWavAsClip(filePath, clipName);
            if (clip != null)
                _unityClipCache[filePath] = clip;
            return clip;
        }

        // evict a cached clip so the next play re-reads from disk
        internal void InvalidateClipCache(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            if (_unityClipCache.Remove(filePath))
                LoggerInstance.Msg($"[Audio] Invalidated cached clip for '{Path.GetFileName(filePath)}'");
        }

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

        // play a wav, picks the right backend
        internal void PlayWarningSound(string clipName)
        {
            if (_vwsMuted) return;

            string filePath = GetSoundPath(clipName);
            if (filePath == null) return;

            float duration = _clipDurations.ContainsKey(clipName) ? _clipDurations[clipName] : 1.0f;
            if (_craftOverrides.ContainsKey(clipName))
                duration = GetWavDuration(filePath);

            string source = _craftOverrides.ContainsKey(clipName) ? "override" : "default";

            if (_useWinmm)
            {
                // Prefer Unity path when available so mixer/master volume controls this mod.
                try
                {
                    if (_voiceSource != null)
                    {
                        var clip = GetUnityClip(clipName, filePath);
                        if (clip != null)
                        {
                            _voiceSource.clip = clip;
                            _voiceSource.Play();
                            _lastPlayTime = Time.realtimeSinceStartup;
                            _currentClipLength = duration;
                            LoggerInstance.Msg($"[Play/Unity] '{clipName}' ({duration:F2}s) [{source}]");
                            return;
                        }
                    }

                    bool result = PlaySound(filePath, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
                    _lastPlayTime = Time.realtimeSinceStartup;
                    _currentClipLength = duration;
                    LoggerInstance.Msg($"[Play/winmm-fallback] '{clipName}' ({duration:F2}s) [{source}] result={result}");
                }
                catch (Exception ex)
                {
                    LoggerInstance.Error($"[Play] Failed '{clipName}': {ex.GetType().Name}: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    var clip = GetUnityClip(clipName, filePath);
                    if (clip != null && _voiceSource != null)
                    {
                        _voiceSource.clip = clip;
                        _voiceSource.Play();
                        _lastPlayTime = Time.realtimeSinceStartup;
                        _currentClipLength = duration;
                        LoggerInstance.Msg($"[Play/Unity] '{clipName}' ({duration:F2}s) [{source}]");
                    }
                    else
                    {
                        LoggerInstance.Warning($"[Play/Unity] No clip or source for '{clipName}'");
                    }
                }
                catch (Exception ex)
                {
                    LoggerInstance.Error($"[Play/Unity] Failed '{clipName}': {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        // stop current sound
        private void StopCurrentAudio()
        {
            try { if (_voiceSource != null) _voiceSource.Stop(); } catch { }

            if (_useWinmm)
            {
                try { PlaySound(null, IntPtr.Zero, SND_PURGE); } catch { }
            }
        }

        // clean up everything
        private void Cleanup()
        {
            StopCurrentAudio();
            StopMissileAlarm();
            _ourSignature = null;
            _warningCooldowns.Clear();
            _warningQueue.Clear();
            _unityClipCache.Clear();
        }

        // check if something is still playing
        internal bool IsAudioPlaying()
        {
            if (_voiceSource != null)
                return _voiceSource.isPlaying;
            return Time.realtimeSinceStartup - _lastPlayTime < _currentClipLength;
        }

        // fire a warning with cooldown
        internal void TriggerWarning(string clipName, float cooldown)
        {
            if (_vwsMuted) return;
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

        // gapless threat loop alarm, different backends per platform
        internal void StartThreatAlarm(string clipName)
        {
            if (_vwsMuted) return;
            string filePath = GetSoundPath(clipName);
            if (filePath == null) return;

            // if already playing same loop, keep it running
            if (_missileAlarmPlaying && string.Equals(_activeThreatLoopClip, clipName, StringComparison.Ordinal))
                return;

            // switch loops cleanly when threat priority changes
            if (_missileAlarmPlaying)
                StopMissileAlarm();

            if (_alarmSource != null)
                StartThreatAlarmUnity(filePath, clipName);
            else if (_useWinmm)
                StartThreatAlarmWaveOut(filePath, clipName);
        }

        internal void StartMissileAlarm()
        {
            StartThreatAlarm("missile_lauch_f18");
        }

        // unity looping alarm for windows
        private void StartThreatAlarmUnity(string filePath, string clipName)
        {
            try
            {
                var clip = GetUnityClip(clipName, filePath);
                if (clip != null && _alarmSource != null)
                {
                    _alarmSource.clip = clip;
                    _alarmSource.loop = true;
                    _alarmSource.Play();
                    _missileAlarmPlaying = true;
                    _activeThreatLoopClip = clipName;
                    LoggerInstance.Msg($"[Audio/Unity] Threat loop started: '{clipName}'");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[Audio/Unity] Threat loop error: {ex.Message}");
            }
        }

        // waveout hardware loop for linux/wine >:(
        private void StartThreatAlarmWaveOut(string filePath, string clipName)
        {
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
                _activeThreatLoopClip = clipName;
                LoggerInstance.Msg($"[waveOut] Threat loop started: '{clipName}' ({channels}ch {sampleRate}Hz {bitsPerSample}bit, {dataSize}B)");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[waveOut] Threat loop error: {ex.Message}");
                CleanupWaveOut();
            }
        }

        // stop the alarm
        internal void StopMissileAlarm()
        {
            if (!_missileAlarmPlaying) return;

            if (_useWinmm)
            {
                try
                {
                    waveOutReset(_waveOutHandle);
                    int hdrSize = Marshal.SizeOf<WAVEHDR>();
                    waveOutUnprepareHeader(_waveOutHandle, _waveHdrPtr, hdrSize);
                    CleanupWaveOut();
                    LoggerInstance.Msg("[waveOut] Missile alarm stopped");
                }
                catch (Exception ex)
                {
                    LoggerInstance.Error($"[waveOut] Stop error: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    if (_alarmSource != null) _alarmSource.Stop();
                    LoggerInstance.Msg("[Audio/Unity] Missile alarm stopped");
                }
                catch { }
            }
            _missileAlarmPlaying = false;
            _activeThreatLoopClip = null;
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
