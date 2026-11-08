using System.Runtime.InteropServices;

namespace Diego.Telemetry.Host;

/// <summary>
/// SCM adapter for the preshutdown coordinator.
/// .NET ServiceBase still has no OnPreshutdown (dotnet/runtime issue 30386),
/// so this file calls RegisterServiceCtrlHandlerEx directly.
/// SERVICE_CONTROL_STOP only stops telemetry. SERVICE_CONTROL_PRESHUTDOWN
/// runs the coordinator. SERVICE_CONTROL_SHUTDOWN does not start a second run.
/// This type is not executed in the Linux build environment.
/// </summary>
public static class WindowsServiceHost
{
    public const int ServiceControlStop = 0x00000001;
    public const int ServiceControlShutdown = 0x00000005;
    public const int ServiceControlPreshutdown = 0x0000000F;
    public const int ServiceControlInterrogate = 0x00000004;
    public const int ServiceAcceptStop = 0x00000001;
    public const int ServiceAcceptShutdown = 0x00000004;
    public const int ServiceAcceptPreshutdown = 0x00000100;
    public const int ServiceConfigPreshutdownInfo = 7;

    private const int ServiceWin32OwnProcess = 0x00000010;
    private const int ServiceStartPending = 0x00000002;
    private const int ServiceRunning = 0x00000004;
    private const int ServiceStopPending = 0x00000003;
    private const int ServiceStopped = 0x00000001;

    /// <summary>
    /// Entry used by the service command. Fails on any OS that is not Windows
    /// before a native call is made.
    /// </summary>
    public static int Run(Func<CancellationToken, Task> telemetry, Func<Action<ShutdownProgress>, CancellationToken, Task<ShutdownResult>> preshutdown)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new RealHardwareUnavailableException(
                "The Windows service control dispatcher is only available on Windows. " +
                "Preshutdown was not registered. Nothing was installed.");
        }

        return Native.Run(telemetry, preshutdown);
    }

    private static class Native
    {
        private static readonly ManualResetEvent Stopped = new(false);
        private static SERVICE_STATUS Status;
        private static IntPtr StatusHandle;
        private static ServiceControlHandlerEx? HandlerDelegate;
        private static ServiceMainCallback? MainDelegate;
        private static Func<CancellationToken, Task>? Telemetry;
        private static Func<Action<ShutdownProgress>, CancellationToken, Task<ShutdownResult>>? Preshutdown;
        private static CancellationTokenSource RunToken = new();
        private static int PreshutdownStarted;

        public static int Run(Func<CancellationToken, Task> telemetry, Func<Action<ShutdownProgress>, CancellationToken, Task<ShutdownResult>> preshutdown)
        {
            Telemetry = telemetry;
            Preshutdown = preshutdown;
            HandlerDelegate = Handler;
            MainDelegate = ServiceMain;
            var table = new SERVICE_TABLE_ENTRY[2];
            table[0].serviceName = "XeneonTelemetryHost";
            table[0].serviceProc = Marshal.GetFunctionPointerForDelegate(MainDelegate);
            if (!StartServiceCtrlDispatcherW(table))
            {
                return Marshal.GetLastWin32Error();
            }

            return 0;
        }

        private static void ServiceMain(int argc, IntPtr argv)
        {
            _ = argc;
            _ = argv;
            StatusHandle = RegisterServiceCtrlHandlerExW("XeneonTelemetryHost", HandlerDelegate!, IntPtr.Zero);
            Report(ServiceStartPending, 1, 3000);
            _ = Telemetry!(RunToken.Token);
            Report(ServiceRunning, 0, 0);
            Stopped.WaitOne();
        }

        private static int Handler(int control, int eventType, IntPtr eventData, IntPtr context)
        {
            _ = eventType;
            _ = eventData;
            _ = context;
            switch (control)
            {
                case ServiceControlInterrogate:
                    Report(Status.currentState, Status.checkPoint, Status.waitHint);
                    return 0;
                case ServiceControlStop:
                    // Operator stopped or restarted the service. Do not power off the Pi.
                    Report(ServiceStopPending, 1, 3000);
                    RunToken.Cancel();
                    Report(ServiceStopped, 0, 0);
                    Stopped.Set();
                    return 0;
                case ServiceControlPreshutdown:
                    if (Interlocked.Exchange(ref PreshutdownStarted, 1) == 0)
                    {
                        Report(ServiceStopPending, 1, ShutdownLimits.WaitHintMilliseconds);
                        try
                        {
                            Preshutdown!(progress => Report(ServiceStopPending, progress.Checkpoint, progress.WaitHintMilliseconds), RunToken.Token)
                                .GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }

                    RunToken.Cancel();
                    Report(ServiceStopped, 0, 0);
                    Stopped.Set();
                    return 0;
                case ServiceControlShutdown:
                    // Later, shorter notification. Do not start another Pi wait.
                    RunToken.Cancel();
                    Report(ServiceStopped, 0, 0);
                    Stopped.Set();
                    return 0;
                default:
                    return 0;
            }
        }

        private static void Report(int state, int checkpoint, int waitHint)
        {
            Status.serviceType = ServiceWin32OwnProcess;
            Status.currentState = state;
            Status.controlsAccepted = state is ServiceStartPending or ServiceStopPending or ServiceStopped
                ? 0
                : ServiceAcceptStop | ServiceAcceptShutdown | ServiceAcceptPreshutdown;
            Status.win32ExitCode = 0;
            Status.serviceSpecificExitCode = 0;
            Status.checkPoint = checkpoint;
            Status.waitHint = waitHint;
            if (StatusHandle != IntPtr.Zero)
            {
                SetServiceStatus(StatusHandle, ref Status);
            }
        }

        private delegate void ServiceMainCallback(int argc, IntPtr argv);

        private delegate int ServiceControlHandlerEx(int control, int eventType, IntPtr eventData, IntPtr context);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_TABLE_ENTRY
        {
            public string serviceName;
            public IntPtr serviceProc;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS
        {
            public int serviceType;
            public int currentState;
            public int controlsAccepted;
            public int win32ExitCode;
            public int serviceSpecificExitCode;
            public int checkPoint;
            public int waitHint;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool StartServiceCtrlDispatcherW([In] SERVICE_TABLE_ENTRY[] serviceTable);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr RegisterServiceCtrlHandlerExW(string serviceName, ServiceControlHandlerEx handler, IntPtr context);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool SetServiceStatus(IntPtr handle, ref SERVICE_STATUS status);
    }
}
