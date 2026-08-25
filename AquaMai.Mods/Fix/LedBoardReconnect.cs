using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using AquaMai.Config.Attributes;
using Comio;
using HarmonyLib;
using MelonLoader;

namespace AquaMai.Mods.Fix;

[ConfigSection(
    name: "LED 控制板断线重连",
    en: "Stop the LED board's failed serial worker after a physical disconnect and reconnect it when the port returns.",
    zh: "LED 控制板物理断开后停止异常串口线程，并在端口恢复后自动重连。",
    exampleHidden: true,
    defaultOn: true)]
public static class LedBoardReconnect
{
    private const long RetryIntervalTicks = TimeSpan.TicksPerSecond;

    private static readonly ConditionalWeakTable<Host, RecoveryState> RecoveryStates = new();
    private static readonly FieldInfo ThreadExitField = AccessTools.Field(typeof(Host), "_threadExit");
    private static readonly FieldInfo InitializedField = AccessTools.Field(typeof(Host), "_initialized");
    private static readonly FieldInfo ThreadField = AccessTools.Field(typeof(Host), "_thread");
    private static readonly FieldInfo PortField = AccessTools.Field(typeof(Host), "_port");
    private static readonly FieldInfo BoardMapField = AccessTools.Field(typeof(Host), "_boardMap");
    private static readonly FieldInfo InitParamField = AccessTools.Field(typeof(Host), "_initParam");

    [ThreadStatic]
    private static Host currentHost;

    private sealed class RecoveryState
    {
        public readonly object Sync = new();
        public int Pending;
        public int RetryCount;
        public long NextRetryTicks;
        public BoardBase[] Boards;
        public string PortName;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Host), "_recv")]
    private static void BeforeReceive(Host __instance)
    {
        currentHost = __instance;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Host), "_recv")]
    private static Exception FinalizeReceive(Exception __exception)
    {
        currentHost = null;
        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Host), "_send")]
    private static void BeforeSend(Host __instance)
    {
        currentHost = __instance;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Host), "_send")]
    private static Exception FinalizeSend(Exception __exception)
    {
        currentHost = null;
        return __exception;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(SerialPort), nameof(SerialPort.Read), typeof(byte[]), typeof(int), typeof(int))]
    private static Exception DetectReadDisconnect(SerialPort __instance, Exception __exception)
    {
        DetectDisconnect(__instance, __exception, "read");
        return __exception;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(SerialPort), nameof(SerialPort.Write), typeof(byte[]), typeof(int), typeof(int))]
    private static Exception DetectWriteDisconnect(SerialPort __instance, Exception __exception)
    {
        DetectDisconnect(__instance, __exception, "write");
        return __exception;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Host), nameof(Host.Execute))]
    private static bool RecoverDisconnectedHost(Host __instance)
    {
        if (!RecoveryStates.TryGetValue(__instance, out var state) ||
            Volatile.Read(ref state.Pending) == 0)
        {
            return true;
        }

        lock (state.Sync)
        {
            if (Volatile.Read(ref state.Pending) == 0)
            {
                return true;
            }

            var worker = (Thread)ThreadField.GetValue(__instance);
            if (worker?.IsAlive == true)
            {
                return false;
            }

            var now = DateTime.UtcNow.Ticks;
            if (now < state.NextRetryTicks)
            {
                return false;
            }
            state.NextRetryTicks = now + RetryIntervalTicks;

            if (state.Boards == null)
            {
                var boardMap = (Dictionary<uint, BoardBase>)BoardMapField.GetValue(__instance);
                state.Boards = new BoardBase[boardMap.Count];
                boardMap.Values.CopyTo(state.Boards, 0);
                state.PortName = ((Host.InitParam)InitParamField.GetValue(__instance)).PortName;
            }

            DisposePort(__instance);
            if (!IsPortPresent(state.PortName))
            {
                state.RetryCount++;
                if (state.RetryCount == 1 || state.RetryCount % 5 == 0)
                {
                    MelonLogger.Msg($"[LedBoardReconnect] 等待 LED 串口恢复：{state.PortName}，第 {state.RetryCount} 次重试");
                }
                return false;
            }

            ThreadField.SetValue(__instance, null);
            if (!__instance.Initialize())
            {
                state.RetryCount++;
                MelonLogger.Warning($"[LedBoardReconnect] LED 串口重新初始化失败：{state.PortName}，第 {state.RetryCount} 次重试");
                return false;
            }

            var registered = 0;
            foreach (var board in state.Boards)
            {
                if (__instance.RegisterBoard(board))
                {
                    registered++;
                }
            }

            // Initialize 会保留已停止的 Thread，置空后原状态机才能创建新线程。
            ThreadField.SetValue(__instance, null);
            Volatile.Write(ref state.Pending, 0);
            MelonLogger.Msg($"[LedBoardReconnect] LED 串口已重连：{state.PortName}，控制板 {registered}/{state.Boards.Length}");
            return true;
        }
    }

    private static void DetectDisconnect(SerialPort port, Exception exception, string operation)
    {
        var host = currentHost;
        if (host == null || exception == null || !IsDisconnectException(exception))
        {
            return;
        }

        var state = RecoveryStates.GetValue(host, CreateRecoveryState);
        lock (state.Sync)
        {
            if (Volatile.Read(ref state.Pending) != 0)
            {
                return;
            }

            // 原实现会吞掉异常并无退避循环，必须先让当前工作线程退出以阻断分配风暴。
            ThreadExitField.SetValue(host, true);
            InitializedField.SetValue(host, false);
            state.RetryCount = 0;
            state.NextRetryTicks = 0;
            state.Boards = null;
            state.PortName = null;
            Volatile.Write(ref state.Pending, 1);
            MelonLogger.Warning(
                $"[LedBoardReconnect] LED 串口已断开：{DescribePort(port)} ({operation})，已停止异常线程；" +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static RecoveryState CreateRecoveryState(Host _)
    {
        return new RecoveryState();
    }

    private static bool IsDisconnectException(Exception exception)
    {
        return exception is IOException ||
               exception is UnauthorizedAccessException ||
               exception is InvalidOperationException ||
               exception is ObjectDisposedException ||
               exception is Win32Exception;
    }

    private static bool IsPortPresent(string portName)
    {
        if (string.IsNullOrEmpty(portName))
        {
            return false;
        }

        try
        {
            foreach (var candidate in SerialPort.GetPortNames())
            {
                if (string.Equals(candidate, portName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[LedBoardReconnect] 无法扫描串口 {portName}：{e.Message}");
        }

        return false;
    }

    private static void DisposePort(Host host)
    {
        var port = (SerialPort)PortField.GetValue(host);
        if (port == null)
        {
            return;
        }

        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
            port.Dispose();
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[LedBoardReconnect] 无法释放旧串口 {DescribePort(port)}：{e.Message}");
        }
    }

    private static string DescribePort(SerialPort port)
    {
        if (port == null)
        {
            return "(null)";
        }

        try
        {
            return port.PortName;
        }
        catch
        {
            return "(unknown)";
        }
    }
}
