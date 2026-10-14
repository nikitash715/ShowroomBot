using ShowroomBot.Core.Scenarios;
using ShowroomBot.Windows;

internal static class RealScenarioWindow
{
    public static Task RunAsync(ScenarioRunner runner, ScenarioDefinition definition)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var cancellation = new CancellationTokenSource();
                using var form = new Form
                {
                    Text = "ShowroomBot — тест", TopMost = true,
                    ClientSize = new Size(320, 130), StartPosition = FormStartPosition.Manual,
                    FormBorderStyle = FormBorderStyle.FixedToolWindow
                };
                // Keep controls outside the RDP screen when a second monitor is available.
                var controlScreen = Screen.AllScreens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen!;
                form.TopMost = !controlScreen.Primary;
                var area = controlScreen.WorkingArea;
                form.Location = new Point(area.Right - form.Width - 12, area.Bottom - form.Height - 12);
                var status = new Label { Dock = DockStyle.Fill, Text = "Запуск через 5 секунд…", Padding = new Padding(8) };
                var stop = new Button { Dock = DockStyle.Bottom, Height = 40, Text = "Остановить (Ctrl+Alt+F12)" };
                form.Controls.Add(status);
                form.Controls.Add(stop);
                void Cancel()
                {
                    _ = cancellation.CancelAsync();
                    ScenarioExecution.CancelCurrent();
                }
                stop.Click += (_, _) => Cancel();
                var finished = false;
                form.FormClosing += (_, e) => { if (!finished) { Cancel(); e.Cancel = true; } };
                using var hotkey = new EmergencyStopHotkey(Cancel);
                using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
                timer.Tick += (_, _) => status.Text = ScenarioExecution.ActiveStatus ?? status.Text;
                timer.Start();
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        await Task.Delay(5000, cancellation.Token);
                        await Task.Run(() => runner.RunAsync(definition, cancellation.Token));
                        completion.TrySetResult();
                    }
                    catch (OperationCanceledException) { completion.TrySetCanceled(); }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { finished = true; form.Close(); }
                };
                Application.Run(form);
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
