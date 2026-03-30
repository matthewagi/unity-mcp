using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Claude.UnityMCP.Communication
{
    public static class MainThreadDispatcher
    {
        private class WorkItem
        {
            public Action Action;
            public AutoResetEvent Done;
            public Exception Error;
        }

        private static readonly ConcurrentQueue<WorkItem> _queue = new ConcurrentQueue<WorkItem>();

        public static void RunOnMainThreadBlocking(Action action)
        {
            using (var evt = new AutoResetEvent(false))
            {
                var item = new WorkItem { Action = action, Done = evt };
                _queue.Enqueue(item);
                if (!evt.WaitOne(30000))
                    throw new TimeoutException("Main thread timeout");
                if (item.Error != null)
                    throw item.Error;
            }
        }

        public static void ProcessPending()
        {
            while (_queue.TryDequeue(out var item))
            {
                try { item.Action?.Invoke(); }
                catch (Exception ex) { item.Error = ex; }
                finally { try { item.Done?.Set(); } catch { } }
            }
        }

        public static void DrainAndCancel()
        {
            while (_queue.TryDequeue(out var item))
            {
                item.Error = new OperationCanceledException("MCP stopped");
                try { item.Done?.Set(); } catch { }
            }
        }
    }
}
