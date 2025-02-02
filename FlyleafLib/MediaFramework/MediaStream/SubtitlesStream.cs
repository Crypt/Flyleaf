﻿using System;

using FFmpeg.AutoGen;

using FlyleafLib.MediaFramework.MediaDemuxer;

namespace FlyleafLib.MediaFramework.MediaStream
{
    public unsafe class SubtitlesStream : StreamBase
    {
        public override string GetDump() { return $"[{Type}  #{StreamIndex}{(Language == null || Language == Language.Get("und") ? "" : "-" + Language.IdSubLanguage)}] {Codec} | [BR: {BitRate}] | {Utils.TicksToTime((long)(AVStream->start_time * Timebase))}/{Utils.TicksToTime((long)(AVStream->duration * Timebase))} | {Utils.TicksToTime(StartTime)}/{Utils.TicksToTime(Duration)}"; }

        public SubtitlesStream() { }
        public SubtitlesStream(Demuxer demuxer, AVStream* st) : base(demuxer, st)
        {
            base.Refresh();
        }
    }
}
