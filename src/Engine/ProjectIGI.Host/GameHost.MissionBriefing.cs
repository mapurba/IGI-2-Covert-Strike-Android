using System.Diagnostics;
using System.Globalization;
using ProjectIGI.Audio;
using ProjectIGI.Core.Diagnostics;
using ProjectIGI.Core.Graphics;
using ProjectIGI.Engine.Audio;
using ProjectIGI.Presentation.Text;
using ProjectIGI.Rendering;
using ProjectIGI.UI;
using ProjectIGI.UI.Layout;

namespace ProjectIGI.Host;

/// <summary>A custom mission's spoken briefing: its voice-over, played once the level is shown, and its captions.</summary>
/// <remarks>
/// <para>
/// Retail missions brief through their scripts and the bank's speech; a mission made outside the original tools has no
/// way to add to that bank. It may instead ship <c>briefing.wav</c> (mono or stereo 8/16-bit PCM) beside its scripts,
/// with an optional <c>briefing_captions.txt</c> of <c>start_ms|end_ms|speaker|text</c> lines. Both are read through
/// <c>MISSION:</c>, so a retail mission, which ships neither, is untouched.
/// </para>
/// <para>
/// The voice starts on the first frame the player can see the level and is held whenever the simulation is: the pause
/// menu, the failure menu, the warm-up and a cut-scene. The sound system cannot seek a playing voice, so a hold stops
/// it and the resume plays the remainder of the PCM from the held position, keeping the captions in step.
/// </para>
/// </remarks>
public sealed partial class GameHost
{
    private const string MissionBriefingVoiceFile = "MISSION:briefing.wav";
    private const string MissionBriefingCaptionFile = "MISSION:briefing_captions.txt";
    private const string MissionBriefingSoundName = "mission_briefing";

    /// <summary>How long a caption lingers after its line, so the eye can finish it.</summary>
    private const double MissionBriefingCaptionLingerSeconds = 0.6;

    private MissionBriefing? _briefing;

    /// <summary>Reads the mission's briefing, if it ships one, ready to start when the level is shown.</summary>
    private void PrepareMissionBriefing()
    {
        StopMissionBriefing();
        if (_audioOut.Sounds is null || !TryReadMissionBytes(MissionBriefingVoiceFile, out byte[]? wav))
        {
            return;
        }

        if (!WavPcm.TryRead(wav, out AudioFormat format, out byte[] pcm) || !format.IsValid || format.BlockAlign == 0)
        {
            _logger.Warning("Audio", "The mission's briefing.wav is not uncompressed 8/16-bit PCM; it is not played.");
            return;
        }

        List<BriefingCaption> captions = TryReadMissionBytes(MissionBriefingCaptionFile, out byte[]? captionBytes)
            ? ParseBriefingCaptions(System.Text.Encoding.UTF8.GetString(captionBytes))
            : [];

        _briefing = new MissionBriefing(format, pcm, captions);
        _logger.Information(
            "Audio",
            FormattableString.Invariant(
                $"The mission ships a spoken briefing: {_briefing.DurationSeconds:F1} s, {captions.Count} captions."));
    }

    /// <summary>Silences and forgets the briefing, as the mission it belongs to goes.</summary>
    private void StopMissionBriefing()
    {
        if (_briefing is { } briefing && briefing.Voice.IsValidHandle)
        {
            _audioOut.Sounds?.Stop(briefing.Voice);
        }

        _briefing = null;
    }

    /// <summary>Starts, holds, resumes and retires the briefing, once a frame.</summary>
    private void UpdateMissionBriefing()
    {
        if (_briefing is not { } briefing || _audioOut.Sounds is not { } sounds)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (briefing.Playing)
        {
            briefing.PositionSeconds += Stopwatch.GetElapsedTime(briefing.Timestamp, now).TotalSeconds;
        }

        briefing.Timestamp = now;

        bool held = _session.Mission is null
            || _session.FailedMenu
            || _menuFlow.PauseMenu
            || PresentationHoldsLevel
            || _mission.Cutscenes?.IsPlaying == true;

        if (briefing.PositionSeconds >= briefing.DurationSeconds)
        {
            StopMissionBriefing();
            return;
        }

        if (held)
        {
            if (briefing.Playing)
            {
                sounds.Stop(briefing.Voice);
                briefing.Voice = AudioVoiceHandle.None;
                briefing.Playing = false;
            }

            return;
        }

        if (briefing.Playing)
        {
            return;
        }

        long frame = (long)(briefing.PositionSeconds * briefing.Format.SampleRate);
        long offset = Math.Clamp(frame * briefing.Format.BlockAlign, 0, briefing.Pcm.Length);
        byte[] remainder = offset == 0 ? briefing.Pcm : briefing.Pcm[(int)offset..];
        if (remainder.Length < briefing.Format.BlockAlign)
        {
            StopMissionBriefing();
            return;
        }

        sounds.RegisterExternal(MissionBriefingSoundName, briefing.Format, remainder);
        AudioVoiceHandle voice = sounds.PlayExternal(MissionBriefingSoundName, null, 1f, 400);
        if (voice.IsValidHandle)
        {
            sounds.SetVoiceParameters(
                voice,
                VoiceParameters.Default with { Category = SoundCategory.Speech, Gain = 1f, Priority = 400 });
        }

        briefing.Voice = voice;
        briefing.Playing = true;
    }

    /// <summary>Draws the line being spoken, low and centred between the thumbs.</summary>
    private void DrawMissionBriefingCaption(FrameLoopState state)
    {
        if (_briefing is not { Playing: true } briefing || _computerOpen)
        {
            return;
        }

        _shellType ??= UiFontLibrary.Typography(_device);
        UI.Kit.IgiTypography type = _shellType;

        BriefingCaption? spoken = null;
        foreach (BriefingCaption caption in briefing.Captions)
        {
            if (briefing.PositionSeconds >= caption.Start
                && briefing.PositionSeconds < caption.End + MissionBriefingCaptionLingerSeconds)
            {
                spoken = caption;
            }
        }

        if (spoken is not { } line)
        {
            return;
        }

        float width = SafeWindowWidth;
        float height = SafeWindowHeight;
        float scale = Math.Clamp(height / 480f, 1f, 3f);
        float textSize = 12.5f * scale;
        float labelSize = 9.5f * scale;
        float panelWidth = Math.Min(width * 0.54f, 760f * scale);
        float padding = 9f * scale;
        List<string> lines = WrapLines(type.Body, line.Text, textSize, panelWidth - (padding * 2f), maxLines: 4);
        float lineHeight = type.Body.LineHeight(textSize) * 1.08f;
        float labelHeight = type.Mono.LineHeight(labelSize);
        float panelHeight = (padding * 2f) + labelHeight + (3f * scale) + (lines.Count * lineHeight);
        float panelX = (width - panelWidth) * 0.5f;
        float panelY = height - panelHeight - (18f * scale);

        bool comms = string.Equals(line.Speaker, "ANYA", StringComparison.OrdinalIgnoreCase);
        Bgra32 accent = comms ? Bgra32.FromRgb(255, 184, 64) : Bgra32.FromRgb(120, 200, 255);
        string label = comms ? $"{line.Speaker} (COMMS)" : $"{line.Speaker} (FIELD)";

        _device.Set2DProjection(SafeWindowWidth, SafeWindowHeight);
        state.Batch.Begin();
        UiPainter painter = UiPainter.Create(state.Batch, new LayoutContext(SafeWindowWidth, SafeWindowHeight));
        painter.FillRect(new UiRect(panelX, panelY, panelWidth, panelHeight), Bgra32.FromRgba(6, 12, 10, 190));
        painter.FillRect(new UiRect(panelX, panelY, 3f * scale, panelHeight), accent);
        painter.DrawText(type.Mono, label, new UiVec2(panelX + padding, panelY + padding), labelSize, accent);

        float y = panelY + padding + labelHeight + (3f * scale);
        foreach (string text in lines)
        {
            painter.DrawTextShadowed(
                type.Body,
                text,
                new UiVec2(panelX + padding, y),
                textSize,
                Bgra32.FromRgb(236, 240, 232),
                Bgra32.FromRgba(0, 0, 0, 200),
                new UiVec2(1f * scale, 1f * scale));
            y += lineHeight;
        }

        state.Batch.End();
    }

    private bool TryReadMissionBytes(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? bytes)
    {
        bytes = null;
        try
        {
            if (!_fileSystem.TryOpenRead(name, out Stream? stream))
            {
                return false;
            }

            using (stream)
            using (MemoryStream buffer = new())
            {
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
                return true;
            }
        }
        catch (IOException ex)
        {
            _logger.Warning("Audio", $"Could not read {name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Reads <c>start_ms|end_ms|speaker|text</c> lines; blank lines and <c>#</c> comments are skipped.</summary>
    internal static List<BriefingCaption> ParseBriefingCaptions(string text)
    {
        List<BriefingCaption> captions = [];
        foreach (string raw in text.Split('\n'))
        {
            string row = raw.Trim();
            if (row.Length == 0 || row[0] == '#')
            {
                continue;
            }

            string[] fields = row.Split('|', 4);
            if (fields.Length == 4
                && int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int start)
                && int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int end)
                && end > start)
            {
                captions.Add(new BriefingCaption(start / 1000.0, end / 1000.0, fields[2].Trim(), fields[3].Trim()));
            }
        }

        return captions;
    }

    /// <summary>One caption: when it is spoken, by whom, and what is said.</summary>
    internal readonly record struct BriefingCaption(double Start, double End, string Speaker, string Text);

    /// <summary>A briefing being played: its PCM, how far it has got, and the voice saying it.</summary>
    private sealed class MissionBriefing(AudioFormat format, byte[] pcm, List<BriefingCaption> captions)
    {
        public AudioFormat Format { get; } = format;

        public byte[] Pcm { get; } = pcm;

        public List<BriefingCaption> Captions { get; } = captions;

        public double DurationSeconds { get; } = (double)format.FrameCount(pcm.Length) / format.SampleRate;

        public double PositionSeconds { get; set; }

        public long Timestamp { get; set; }

        public bool Playing { get; set; }

        public AudioVoiceHandle Voice { get; set; } = AudioVoiceHandle.None;
    }
}
