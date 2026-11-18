using System.Diagnostics;
using PCL.Core.App.Localization;

namespace PCL;

public static partial class GameWatcher
{
    public partial class Watcher
    {
        private void LogReceived(object sender, DataReceivedEventArgs e)
        {
            var line = e.Data;
            lock (_waitingLogLock)
            {
                if (line is null)
                {
                    _closedStreams++;
                    return;
                }

                waitingLog.Add(line);
            }

            if (!RealTimeLog)
                return;

            LogRealTime(line, ref _lastLevel);
            fullLog.Add(line);
        }

        /// <summary>
        ///     触发日志改变事件，并统计日志行数。
        /// </summary>
        private void LogRealTime(string? line, ref GameLogLevel level)
        {
            if (line is null)
                return;

            if (!IsLogContinuationLine(line))
                level = GetLevel(line, level);

            IncrementLogLevelCount(level);
            LogOutput?.Invoke(this, new LogOutputEventArgs(line, level));
        }

        private static bool IsLogContinuationLine(string line)
        {
            return line.StartsWithF("\tat ")
                || line.StartsWithF("Caused by: ")
                || line.StartsWithF("\t... ");
        }

        private void IncrementLogLevelCount(GameLogLevel level)
        {
            switch (level)
            {
                case GameLogLevel.Debug:
                    countDebug = (uint)(countDebug + 1L);
                    break;
                case GameLogLevel.Info:
                    countInfo = (uint)(countInfo + 1L);
                    break;
                case GameLogLevel.Warn:
                    countWarn = (uint)(countWarn + 1L);
                    break;
                case GameLogLevel.Error:
                    countError = (uint)(countError + 1L);
                    break;
                case GameLogLevel.Fatal:
                    countFatal = (uint)(countFatal + 1L);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(level), level, null);
            }
        }

        /// <summary>
        ///     有新的日志输出，日志计数器发生改变时触发。
        /// </summary>
        public event LogOutputEventHandler? LogOutput;

        private void TimerLog()
        {
            try
            {
                ProcessPendingLogs();

                if (!gameProcess.HasExited)
                    return;

                // 退出检测独立于日志队列，否则最后几行日志处理完后将永远不会被发现
                // 进程退出后异步输出可能仍在送达，先等两条输出流结束（最多 2 秒）再处理退出，避免漏掉崩溃日志
                var deadline = Environment.TickCount64 + 2000;
                while (!IsOutputStreamClosed && Environment.TickCount64 < deadline)
                {
                    Thread.Sleep(10);
                    ProcessPendingLogs();
                }

                ProcessPendingLogs();
                HandleMinecraftProcessExit();
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(
                    ex,
                    "输出 Minecraft 日志失败",
                    LauncherLogLevel.Feedback,
                    userSummary: Lang.Text("Minecraft.Launch.Error.WatcherOperationFailed"));
            }
        }

        private void ProcessPendingLogs()
        {
            var pendingLogs = TakePendingLogs();
            if (pendingLogs is null)
                return;

            foreach (var line in pendingLogs)
                GameLog(line);

            if (State == MinecraftState.Loading)
                ProgressUpdate();
        }

        private bool IsOutputStreamClosed
        {
            get
            {
                lock (_waitingLogLock)
                    return _closedStreams >= 2;
            }
        }

        private List<string>? TakePendingLogs()
        {
            lock (_waitingLogLock)
            {
                if (waitingLog.Count == 0)
                    return null;

                var pendingLogs = waitingLog;
                waitingLog = new List<string>(1000);
                return pendingLogs;
            }
        }

        private void HandleMinecraftProcessExit()
        {
            WatcherLog(Lang.Text("Watcher.ProcessExited", gameProcess.ExitCode));

            if (RealTimeLog)
            {
                var level = GameLogLevel.Info;
                LogRealTime(
                    Lang.Text("Watcher.ProcessExited", gameProcess.ExitCode),
                    ref level);
            }

            GameExit?.Invoke();

            if (State == MinecraftState.Loading)
            {
                WatcherLog(Lang.Text("Watcher.Crash.Suspected"));
                Crashed();
            }
            else if (gameProcess.ExitCode != 0
                     && State == MinecraftState.Running
                     && version.releaseTime.Year >= 2012)
            {
                WatcherLog(Lang.Text("Watcher.Crash.AbnormalExit"));
                Crashed();
            }
            else if (State != MinecraftState.Crashed)
            {
                State = MinecraftState.Ended;
            }
        }

        private void GameLog(string? text)
        {
            if (text is null)
                return;

            text = NormalizeLogLineEndings(text);
            latestLog.Enqueue(text);

            if (latestLog.Count >= 501)
                latestLog.Dequeue();

            UpdateLogProgress(text);
            HandleMinecraftStateFromLog(text);
        }

        private static string NormalizeLogLineEndings(string text)
        {
            return text
                .Replace("\r\n", "\r")
                .Replace("\n", "\r")
                .Replace("\r", "\r\n");
        }

        private void UpdateLogProgress(string text)
        {
            switch (_logProgress)
            {
                case < 1:
                    WatcherLog(Lang.Text("Watcher.Progress.LogAppeared"));
                    _logProgress = 1;
                    break;

                case < 2 when text.Contains("Setting user:"):
                    WatcherLog(Lang.Text("Watcher.Progress.UserSet"));
                    _logProgress = 2;
                    break;

                case < 3 when text.ContainsF("lwjgl version", true):
                    WatcherLog(Lang.Text("Watcher.Progress.LwjglConfirmed"));
                    _logProgress = 3;
                    break;

                case < 4 when text.Contains("OpenAL initialized")
                    || text.Contains("Starting up SoundSystem"):
                    WatcherLog(Lang.Text("Watcher.Progress.OpenAlLoaded"));
                    _logProgress = 4;
                    break;

                case < 5 when (text.Contains("Created")
                        && text.Contains("textures")
                        && text.Contains("-atlas"))
                    || text.Contains("Found animation info"):
                    WatcherLog(Lang.Text("Watcher.Progress.TexturesLoaded"));
                    _logProgress = 5;
                    break;
            }
        }

        private void HandleMinecraftStateFromLog(string text)
        {
            if (text.Contains("[CHAT]"))
                return;

            if (text.Contains("Someone is closing me!")
                || text.Contains("Restarting Minecraft with command"))
            {
                WatcherLog(Lang.Text("Watcher.Log.CloseDetected", text));
                State = MinecraftState.Ended;
                return;
            }

            if (text.Contains("Crash report saved to")
                || text.Contains("This crash report has been saved to:")
                || text.Contains("Could not save crash report to")
                || text.Contains("/ERROR]: Unable to launch")
                || text.Contains(
                    "An exception was thrown, the game will display an error screen and halt."))
            {
                WatcherLog(Lang.Text("Watcher.Log.CrashDetected", text));
                Crashed();
            }
        }

        private void WatcherLog(string text)
        {
            LauncherCommon.Log($"[{_pid}] {text}");
        }
    }
}