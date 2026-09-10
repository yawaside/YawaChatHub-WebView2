namespace YawaChatHub;

/// <summary>
/// Озвучка через SAPI (COM «SAPI.SpVoice») — то, что в Electron-версии делал
/// модуль desktop/electron/tts.js. Interop без дополнительных NuGet-пакетов.
/// </summary>
public sealed class SapiTts : IDisposable
{
    private readonly object _gate = new();
    private dynamic? _voice;
    private Thread? _thread;
    private bool _cancelRequested;

    private dynamic Voice
    {
        get
        {
            _voice ??= Activator.CreateInstance(
                Type.GetTypeFromProgID("SAPI.SpVoice", throwOnError: true)!)
                ?? throw new InvalidOperationException("SAPI недоступен");
            return _voice;
        }
    }

    /// Озвучить текст и завершить таск по окончании фразы (синхронный Speak в воркере).
    public Task SpeakAsync(string text, string voiceName, int rate, int volume)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate) _cancelRequested = false;
        _thread = new Thread(() =>
        {
            try
            {
                var v = Voice;
                try
                {
                    var tokens = v.GetVoices();
                    for (int i = 0; i < tokens.Count; i++)
                    {
                        var t = tokens.Item(i);
                        string desc = t.GetDescription();
                        if (!string.IsNullOrEmpty(voiceName) &&
                            desc.Contains(voiceName, StringComparison.OrdinalIgnoreCase))
                        {
                            v.Voice = t;
                            break;
                        }
                    }
                }
                catch { /* голос по умолчанию */ }

                v.Rate = Math.Clamp(rate, -10, 10);
                v.Volume = Math.Clamp(volume, 0, 100);
                // SVSFlagsAsync=1 — не блокируем COM-поток, ждём флаг окончания
                v.Speak(text, 1);
                while ((int)v.WaitUntilDone(100) == 0)
                {
                    lock (_gate) if (_cancelRequested) break;
                }
            }
            catch { /* голос мог быть сброшен Clear() */ }
            tcs.TrySetResult();
        }) { IsBackground = true, Name = "yawa-tts" };
        _thread.Start();
        return tcs.Task;
    }

    public void Pause() { try { Voice.Pause(); } catch { } }
    public void Resume() { try { Voice.Resume(); } catch { } }

    /// Сбросить очередь: пропустить всё озвученное и стоящее в очереди SAPI.
    public void Clear()
    {
        lock (_gate) _cancelRequested = true;
        try { Voice.Skip("Sentence", int.MaxValue); } catch { }
    }

    public void Dispose()
    {
        Clear();
        try { _thread?.Join(300); } catch { }
        if (_voice != null)
        {
            try { System.Runtime.InteropServices.Marshal.ReleaseComObject(_voice); } catch { }
            _voice = null;
        }
    }
}
