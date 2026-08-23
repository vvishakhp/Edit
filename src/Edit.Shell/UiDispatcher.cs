using Avalonia.Threading;

namespace Edit.Shell;

internal static class UiDispatcher
{
    public static void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Invoke(action);
    }

    public static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
