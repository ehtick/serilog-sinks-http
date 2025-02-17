﻿// Copyright 2015-2025 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Serilog.Sinks.Http.Private.Time;

public class PortableTimer : IDisposable
{
    private readonly object syncRoot = new();
    private readonly Func<Task> onTick;
    private readonly Timer timer;

    private bool running;
    private bool disposed;

    public PortableTimer(Func<Task> onTick)
    {
        this.onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));

        timer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start(TimeSpan interval)
    {
        if (interval < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));

        lock (syncRoot)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PortableTimer));
            }

            timer.Change(interval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            while (running)
            {
                Monitor.Wait(syncRoot);
            }

            timer.Dispose();

            disposed = true;
        }
    }

    private async void OnTick()
    {
        try
        {
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                // There's a little bit of raciness here, but it's needed to support the
                // current API, which allows the tick handler to reenter and set the next interval.

                if (running)
                {
                    Monitor.Wait(syncRoot);

                    if (disposed)
                    {
                        return;
                    }
                }

                running = true;
            }

            await onTick();
        }
        finally
        {
            lock (syncRoot)
            {
                running = false;
                Monitor.PulseAll(syncRoot);
            }
        }
    }
}