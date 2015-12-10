// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System;
using System.Diagnostics;

namespace OpenLiveWriter.CoreServices
{
    /// <summary>
    /// Easy way to slap a timer around a block of code.  Example:
    /// 
    /// using (new QuickTimer("Long running operation"))
    /// {
    ///	// do long running operation
    /// }
    /// 
    /// When the using block is exited, timing info will be written 
    /// to the Debug output.
    /// </summary>
    public struct QuickTimer : IDisposable
    {


        private readonly string label;
#if DEBUG
        private readonly PerformanceTimer timer;
#endif

        public QuickTimer(string label)
        {

            this.label = label;
#if DEBUG
            this.timer = new PerformanceTimer();
#endif
        }

        public void Dispose()
        {
#if DEBUG
            double time = timer.ElapsedTime();
            Debug.WriteLine("QuickTimer: [" + label + "] " + time + "ms");
#else
			// This is only here to prevent the compiler from warning that label isn't in use
			// The label is only present in release because if we completely empty this class
			// in release, a placeholder is generated by asmmeta, and that placeholder causes
			// asmmeta to vary between release and debug
			Debug.WriteLine("QuickTimer: [" + label + "] - no timing information");
#endif
        }

    }
}
