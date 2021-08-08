﻿using System;
using System.Runtime.InteropServices;

using FFmpeg.AutoGen;

namespace FlyleafLib.MediaFramework.MediaDemuxer
{
    public unsafe class Interrupter
    {
        public int          ForceInterrupt  { get; set; }
        public bool         AllowInterrupts { get; internal set;}

        public Requester    Requester       { get; private set; }
        public long         Requested       { get; private set; }
        public int          Interrupted     { get; private set; }
        public bool         TimedOut        { get; private set; }
        

        public AVIOInterruptCB_callback_func GetCallBackFunc() { return interruptClbk; }
        AVIOInterruptCB_callback_func   interruptClbk = new AVIOInterruptCB_callback_func();     
        AVIOInterruptCB_callback        InterruptClbk = (opaque) =>
        {
            GCHandle demuxerHandle = (GCHandle)((IntPtr)opaque);
            Demuxer demuxer = (Demuxer)demuxerHandle.Target;

            demuxer.Interrupter.TimedOut = false;

            if (demuxer.Status == Status.Stopping)
            {
                #if DEBUG
                demuxer.Log($"{demuxer.Interrupter.Requester} Interrupt (Stopping) !!!");
                #endif
                return demuxer.Interrupter.Interrupted = 1;
            }

            long curTimeout = 0;
            switch (demuxer.Interrupter.Requester)
            {
                case Requester.Close:
                    curTimeout = demuxer.Config.CloseTimeout;
                    break;

                case Requester.Open:
                    curTimeout = demuxer.Config.OpenTimeout;
                    break;

                case Requester.Read:
                    curTimeout = demuxer.Config.ReadTimeout;
                    break;

                case Requester.Seek:
                    curTimeout = demuxer.Config.SeekTimeout;
                    break;
            }

            if (DateTime.UtcNow.Ticks - demuxer.Interrupter.Requested > curTimeout)
            {
                demuxer.Interrupter.TimedOut = true;

                #if DEBUG
                demuxer.Log($"{demuxer.Interrupter.Requester} Timeout !!!! {(DateTime.UtcNow.Ticks - demuxer.Interrupter.Requested) / 10000} ms");
                #endif
                return demuxer.Interrupter.Interrupted = 1;
            }

            if (demuxer.Interrupter.Requester == Requester.Close) return 0;

            if (demuxer.Interrupter.AllowInterrupts)
            {
                if (demuxer.Status == Status.Pausing)
                {
                    #if DEBUG
                    demuxer.Log($"{demuxer.Interrupter.Requester} Interrupt (Pausing) !!!");
                    #endif
                    return demuxer.Interrupter.Interrupted = 1;
                }

                if (demuxer.Interrupter.ForceInterrupt != 0)
                {
                    #if DEBUG
                    demuxer.Log($"{demuxer.Interrupter.Requester} Interrupt !!!");
                    #endif
                    return demuxer.Interrupter.Interrupted = 1;
                }
            }

            return demuxer.Interrupter.Interrupted = 0;
        };

        public Interrupter()
        {
            interruptClbk.Pointer = Marshal.GetFunctionPointerForDelegate(InterruptClbk);
        }

        public void Request(Requester requester)
        {
            Requester = requester;
            Requested = DateTime.UtcNow.Ticks;
        }
    }

    public enum Requester
    {
        Close,
        Open,
        Read,
        Seek
    }
}
