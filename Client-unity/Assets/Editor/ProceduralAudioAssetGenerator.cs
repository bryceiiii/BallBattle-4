using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class ProceduralAudioAssetGenerator
{
    private const int SampleRate = 44100;
    private const string AudioFolder = "Assets/Resources/Audio";

    [MenuItem("Tools/Game Audio/Regenerate Arena Audio")]
    public static void GenerateAll()
    {
        string fullFolder = Path.Combine(Application.dataPath, "Resources/Audio");
        Directory.CreateDirectory(fullFolder);

        WriteWav(Path.Combine(fullFolder, "BackgroundMusic.wav"), BuildBackgroundMusic());
        WriteWav(Path.Combine(fullFolder, "SFX_Shoot.wav"), BuildShoot());
        WriteWav(Path.Combine(fullFolder, "SFX_Hit.wav"), BuildHit());
        WriteWav(Path.Combine(fullFolder, "SFX_Empty.wav"), BuildEmpty());
        WriteWav(Path.Combine(fullFolder, "SFX_Split.wav"), BuildSplit());
        WriteWav(Path.Combine(fullFolder, "SFX_Pickup.wav"), BuildPickup());
        WriteWav(Path.Combine(fullFolder, "SFX_UI_Click.wav"), BuildUiClick());

        AssetDatabase.Refresh();

        ConfigureMusic(AudioFolder + "/BackgroundMusic.wav");
        ConfigureSfx(AudioFolder + "/SFX_Shoot.wav", AudioCompressionFormat.ADPCM);
        ConfigureSfx(AudioFolder + "/SFX_Hit.wav", AudioCompressionFormat.ADPCM);
        ConfigureSfx(AudioFolder + "/SFX_Empty.wav", AudioCompressionFormat.ADPCM);
        ConfigureSfx(AudioFolder + "/SFX_Split.wav", AudioCompressionFormat.ADPCM);
        ConfigureSfx(AudioFolder + "/SFX_Pickup.wav", AudioCompressionFormat.PCM);
        ConfigureSfx(AudioFolder + "/SFX_UI_Click.wav", AudioCompressionFormat.PCM);

        AssetDatabase.SaveAssets();
        Debug.Log("[ProceduralAudio] Generated arena music and sound effects in " + AudioFolder);
    }

    private static void ConfigureMusic(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;

        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = 0.72f;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = false;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = false;
        importer.loadInBackground = true;
        importer.SaveAndReimport();
    }

    private static void ConfigureSfx(string path, AudioCompressionFormat format)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) return;

        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = format;
        settings.quality = 0.9f;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = true;
        importer.loadInBackground = false;
        importer.SaveAndReimport();
    }

    private static float[] BuildBackgroundMusic()
    {
        const int bars = 16;
        const float bpm = 120f;
        const float beatSeconds = 60f / bpm;
        const float barSeconds = beatSeconds * 4f;
        int totalSamples = Mathf.RoundToInt(SampleRate * barSeconds * bars);
        float[] left = new float[totalSamples];
        float[] right = new float[totalSamples];

        int[] roots = { 45, 41, 48, 43 };
        bool[] minor = { true, false, false, false };
        int[] arpPattern = { 0, 1, 2, 3, 2, 1, 0, 2 };

        for (int bar = 0; bar < bars; bar++)
        {
            int chord = bar % 4;
            float barStart = bar * barSeconds;
            int root = roots[chord];

            AddPad(left, right, barStart, barSeconds, root, minor[chord]);
            AddBass(left, right, barStart, beatSeconds, root, bar);

            int arpCount = 16;
            float stepSeconds = barSeconds / arpCount;
            for (int step = 0; step < arpCount; step++)
            {
                int patternIndex = step % arpPattern.Length;
                int interval = arpPattern[patternIndex];
                int note = root + 24 + ChordInterval(interval, minor[chord]);
                AddPluck(left, right, barStart + step * stepSeconds, stepSeconds, note, step);
            }

            for (int beat = 0; beat < 4; beat++)
            {
                float beatStart = barStart + beat * beatSeconds;
                AddKick(left, right, beatStart);
                AddHat(left, right, beatStart + beatSeconds * 0.5f, 0.75f);
                AddHat(left, right, beatStart + beatSeconds * 0.25f, 0.28f);
                AddHat(left, right, beatStart + beatSeconds * 0.75f, 0.22f);

                if (beat == 1 || beat == 3)
                    AddSnare(left, right, beatStart);
            }

            float riserStart = barStart + barSeconds * 0.72f;
            if (bar % 4 == 3)
                AddRiser(left, right, riserStart, barSeconds * 0.28f);
        }

        float[] result = new float[totalSamples * 2];
        float peak = 0.001f;
        for (int i = 0; i < totalSamples; i++)
        {
            float l = SoftClip(left[i] * 1.05f);
            float r = SoftClip(right[i] * 1.05f);
            peak = Mathf.Max(peak, Mathf.Abs(l), Mathf.Abs(r));
            result[i * 2] = l;
            result[i * 2 + 1] = r;
        }

        float gain = 0.88f / peak;
        int fadeSamples = SampleRate / 80;
        for (int i = 0; i < totalSamples; i++)
        {
            float edgeFade = 1f;
            if (i < fadeSamples) edgeFade = i / (float)fadeSamples;
            else if (i >= totalSamples - fadeSamples) edgeFade = (totalSamples - 1 - i) / (float)fadeSamples;

            result[i * 2] *= gain * edgeFade;
            result[i * 2 + 1] *= gain * edgeFade;
        }
        return result;
    }

    private static float[] BuildShoot()
    {
        float duration = 0.22f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count * 2];
        var rng = new System.Random(4142);
        float phase = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float freq = Mathf.Lerp(1650f, 190f, Mathf.Pow(p, 0.45f));
            phase += freq / SampleRate;
            float saw = 2f * (phase - Mathf.Floor(phase + 0.5f));
            float sine = Mathf.Sin(phase * Mathf.PI * 2f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float snap = noise * Mathf.Exp(-t * 95f) * 0.55f;
            float body = (saw * 0.62f + sine * 0.38f) * Mathf.Exp(-t * 15f);
            float sample = (body + snap) * 0.65f;
            data[i * 2] = sample;
            data[i * 2 + 1] = sample * 0.97f;
        }
        return Normalize(data, 0.92f);
    }

    private static float[] BuildHit()
    {
        float duration = 0.24f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count * 2];
        var rng = new System.Random(7853);
        float phase = 0f;
        float lowNoise = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float freq = Mathf.Lerp(145f, 42f, Mathf.Pow(p, 0.5f));
            phase += freq / SampleRate;
            float thump = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 17f);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lowNoise = Mathf.Lerp(lowNoise, noise, 0.12f);
            float crack = (noise - lowNoise) * Mathf.Exp(-t * 55f);
            float metalPhase = 2f * Mathf.PI * 760f * t;
            float metal = Mathf.Sin(metalPhase) * Mathf.Exp(-t * 34f) * 0.18f;
            float sample = thump * 0.75f + crack * 0.44f + metal;
            data[i * 2] = sample;
            data[i * 2 + 1] = sample;
        }
        return Normalize(data, 0.9f);
    }

    private static float[] BuildEmpty()
    {
        float duration = 0.1f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count * 2];
        var rng = new System.Random(9201);

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float click = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 180f);
            float ping = Mathf.Sin(2f * Mathf.PI * 1850f * t) * Mathf.Exp(-t * 44f);
            float low = Mathf.Sin(2f * Mathf.PI * 820f * t) * Mathf.Exp(-t * 60f);
            float sample = click * 0.45f + ping * 0.45f + low * 0.2f;
            data[i * 2] = sample;
            data[i * 2 + 1] = sample;
        }
        return Normalize(data, 0.72f);
    }

    private static float[] BuildSplit()
    {
        float duration = 0.32f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count * 2];
        var rng = new System.Random(3077);
        float phase = 0f;
        float filtered = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / duration;
            float freq = Mathf.Lerp(180f, 980f, p * p);
            phase += freq / SampleRate;
            float chirp = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Sin(p * Mathf.PI);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            filtered = Mathf.Lerp(filtered, noise, 0.06f + p * 0.22f);
            float whoosh = (noise - filtered) * Mathf.Sin(p * Mathf.PI) * 0.5f;
            float sample = chirp * 0.5f + whoosh;
            data[i * 2] = sample * (0.94f + p * 0.06f);
            data[i * 2 + 1] = sample * (1.06f - p * 0.06f);
        }
        return Normalize(data, 0.84f);
    }

    private static float[] BuildPickup()
    {
        float duration = 0.42f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] left = new float[count];
        float[] right = new float[count];
        int[] midi = { 84, 88, 91, 96 };
        float[] starts = { 0f, 0.055f, 0.11f, 0.17f };

        for (int note = 0; note < midi.Length; note++)
        {
            float freq = MidiToFrequency(midi[note]);
            int start = Mathf.RoundToInt(starts[note] * SampleRate);
            int end = Mathf.Min(count, start + Mathf.RoundToInt(0.22f * SampleRate));
            for (int i = start; i < end; i++)
            {
                float t = (i - start) / (float)SampleRate;
                float env = Mathf.Exp(-t * 13f) * Mathf.Clamp01(t * 180f);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.7f;
                tone += Mathf.Sin(2f * Mathf.PI * freq * 2.01f * t) * 0.2f;
                float pan = note / (float)(midi.Length - 1);
                left[i] += tone * env * (1f - pan * 0.45f);
                right[i] += tone * env * (0.55f + pan * 0.45f);
            }
        }

        var rng = new System.Random(6602);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            if (i % 7 == 0)
            {
                float sparkleNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float sparkle = sparkleNoise * Mathf.Exp(-t * 7f) * 0.12f;
                left[i] += sparkle;
                right[i] += sparkle * 0.82f;
            }
        }

        float[] data = new float[count * 2];
        for (int i = 0; i < count; i++)
        {
            data[i * 2] = left[i];
            data[i * 2 + 1] = right[i];
        }
        return Normalize(data, 0.82f);
    }

    private static float[] BuildUiClick()
    {
        float duration = 0.09f;
        int count = Mathf.RoundToInt(SampleRate * duration);
        float[] data = new float[count * 2];
        var rng = new System.Random(5531);

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float glass = Mathf.Sin(2f * Mathf.PI * 2100f * t) * 0.34f;
            glass += Mathf.Sin(2f * Mathf.PI * 3150f * t) * 0.18f;
            float sample = (glass + noise * 0.18f) * Mathf.Exp(-t * 58f);
            data[i * 2] = sample;
            data[i * 2 + 1] = sample;
        }
        return Normalize(data, 0.7f);
    }

    private static void AddPad(float[] left, float[] right, float start, float duration, int root, bool minor)
    {
        int startSample = Mathf.RoundToInt(start * SampleRate);
        int endSample = Mathf.Min(left.Length, startSample + Mathf.RoundToInt(duration * SampleRate));
        int[] intervals = minor ? new[] { 0, 3, 7 } : new[] { 0, 4, 7 };

        for (int note = 0; note < intervals.Length; note++)
        {
            float freq = MidiToFrequency(root + 12 + intervals[note]);
            float pan = (note - 1) * 0.38f;
            float leftGain = Mathf.Lerp(0.55f, 1f, (1f - pan) * 0.5f);
            float rightGain = Mathf.Lerp(1f, 0.55f, (1f + pan) * 0.5f);

            for (int i = startSample; i < endSample; i++)
            {
                float local = (i - startSample) / (float)SampleRate;
                float p = local / duration;
                float env = Mathf.Min(1f, local / 0.18f) * Mathf.Pow(Mathf.Clamp01(1f - p), 0.7f);
                float phaseA = local * freq;
                float phaseB = local * freq * 1.006f;
                float sawA = 2f * (phaseA - Mathf.Floor(phaseA + 0.5f));
                float sawB = 2f * (phaseB - Mathf.Floor(phaseB + 0.5f));
                float sample = (sawA + sawB) * env * 0.055f;
                left[i] += sample * leftGain;
                right[i] += sample * rightGain;
            }
        }
    }

    private static void AddBass(float[] left, float[] right, float start, float beatSeconds, int root, int bar)
    {
        for (int step = 0; step < 8; step++)
        {
            float noteStart = start + step * beatSeconds * 0.5f;
            float freq = MidiToFrequency(root + (step == 3 || step == 7 ? 7 : 0));
            AddTone(left, right, noteStart, beatSeconds * 0.42f, freq, 0.16f, 0.8f, 0.5f, 0.5f);
        }
    }

    private static void AddPluck(float[] left, float[] right, float start, float stepSeconds, int midi, int step)
    {
        float freq = MidiToFrequency(midi);
        float pan = step % 2 == 0 ? 0.24f : -0.24f;
        AddTone(left, right, start, stepSeconds * 0.82f, freq, 0.13f, 0.65f, 0.5f - pan, 0.5f + pan);
    }

    private static void AddTone(
        float[] left,
        float[] right,
        float start,
        float duration,
        float freq,
        float gain,
        float decay,
        float leftGain,
        float rightGain)
    {
        int startSample = Mathf.Max(0, Mathf.RoundToInt(start * SampleRate));
        int endSample = Mathf.Min(left.Length, startSample + Mathf.RoundToInt(duration * SampleRate));
        float phase = 0f;
        for (int i = startSample; i < endSample; i++)
        {
            float t = (i - startSample) / (float)SampleRate;
            float p = t / duration;
            phase += freq / SampleRate;
            float square = phase % 1f < 0.5f ? 1f : -1f;
            float sine = Mathf.Sin(phase * Mathf.PI * 2f);
            float env = Mathf.Clamp01(t * 180f) * Mathf.Exp(-t * decay) * Mathf.Clamp01(1f - p);
            float sample = (square * 0.35f + sine * 0.65f) * env * gain;
            left[i] += sample * leftGain;
            right[i] += sample * rightGain;
        }
    }

    private static void AddKick(float[] left, float[] right, float start)
    {
        int startSample = Mathf.RoundToInt(start * SampleRate);
        int endSample = Mathf.Min(left.Length, startSample + Mathf.RoundToInt(0.2f * SampleRate));
        float phase = 0f;
        for (int i = startSample; i < endSample; i++)
        {
            float t = (i - startSample) / (float)SampleRate;
            float freq = Mathf.Lerp(125f, 43f, Mathf.Clamp01(t / 0.16f));
            phase += freq / SampleRate;
            float env = Mathf.Exp(-t * 17f);
            float sample = Mathf.Sin(phase * Mathf.PI * 2f) * env * 0.44f;
            left[i] += sample;
            right[i] += sample;
        }
    }

    private static void AddSnare(float[] left, float[] right, float start)
    {
        var rng = new System.Random(1000 + Mathf.RoundToInt(start * 10f));
        int startSample = Mathf.RoundToInt(start * SampleRate);
        int endSample = Mathf.Min(left.Length, startSample + Mathf.RoundToInt(0.18f * SampleRate));
        float previous = 0f;
        for (int i = startSample; i < endSample; i++)
        {
            float t = (i - startSample) / (float)SampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float high = noise - previous * 0.92f;
            previous = noise;
            float sample = high * Mathf.Exp(-t * 22f) * 0.24f;
            left[i] += sample;
            right[i] += sample;
        }
    }

    private static void AddHat(float[] left, float[] right, float start, float volume)
    {
        var rng = new System.Random(2000 + Mathf.RoundToInt(start * 31f));
        int startSample = Mathf.RoundToInt(start * SampleRate);
        int endSample = Mathf.Min(left.Length, startSample + Mathf.RoundToInt(0.055f * SampleRate));
        float previous = 0f;
        for (int i = startSample; i < endSample; i++)
        {
            float t = (i - startSample) / (float)SampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float high = noise - previous * 0.96f;
            previous = noise;
            float sample = high * Mathf.Exp(-t * 72f) * 0.09f * volume;
            left[i] += sample;
            right[i] += sample;
        }
    }

    private static void AddRiser(float[] left, float[] right, float start, float duration)
    {
        int startSample = Mathf.RoundToInt(start * SampleRate);
        int endSample = Mathf.Min(left.Length, startSample + Mathf.RoundToInt(duration * SampleRate));
        var rng = new System.Random(7744);
        float filtered = 0f;
        for (int i = startSample; i < endSample; i++)
        {
            float t = (i - startSample) / (float)SampleRate;
            float p = t / duration;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            filtered = Mathf.Lerp(filtered, noise, 0.02f + p * 0.3f);
            float sample = (noise - filtered) * p * p * 0.16f;
            left[i] += sample;
            right[i] += sample;
        }
    }

    private static int ChordInterval(int index, bool minor)
    {
        int[] intervals = minor ? new[] { 0, 3, 7, 12 } : new[] { 0, 4, 7, 12 };
        return intervals[index % intervals.Length];
    }

    private static float MidiToFrequency(int midi)
    {
        return 440f * Mathf.Pow(2f, (midi - 69) / 12f);
    }

    private static float SoftClip(float value)
    {
        return (float)Math.Tanh(value);
    }

    private static float[] Normalize(float[] data, float targetPeak)
    {
        float peak = 0.0001f;
        for (int i = 0; i < data.Length; i++)
            peak = Mathf.Max(peak, Mathf.Abs(data[i]));

        float gain = targetPeak / peak;
        for (int i = 0; i < data.Length; i++)
            data[i] *= gain;
        return data;
    }

    private static void WriteWav(string path, float[] interleavedStereo)
    {
        int sampleCount = interleavedStereo.Length;
        int dataSize = sampleCount * 2;
        int frameCount = sampleCount / 2;

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataSize);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)2);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2 * 2);
            writer.Write((short)4);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)Mathf.Clamp(Mathf.RoundToInt(interleavedStereo[i] * 32767f), -32768, 32767);
                writer.Write(sample);
            }
        }
    }
}
