using ShowroomBot.Core.Scenarios;

internal static class OpenConfigChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var options = new ScenarioStepDefinition { Type = "OpenConfig", Executable = "1c.exe", Server = "server",
            Database = "base", User = "reader", Password = "secret", MaxWindowsToCheck = 99 };
        var found = new FakeUi { FoundAt = 4 };
        await new OpenConfigStep(options, found).ExecuteAsync();
        check(found.Prepared && found.Checks == 4 && found.Indices.SequenceEqual([1, 2, 3]) && !found.Launched,
            "OpenConfig: последовательные MRU-индексы; найденный конфигуратор используется без запуска");
        var missing = new FakeUi();
        await new OpenConfigStep(options, missing).ExecuteAsync();
        check(missing.Checks == 15 && missing.Indices.SequenceEqual(Enumerable.Range(1, 14)) &&
            missing.Launch == options && missing.Waited,
            "OpenConfig: жёсткий предел 15 окон; затем запуск со своими параметрами и подтверждение готовности");
        options.MaxWindowsToCheck = 2;
        var bounded = new FakeUi();
        await new OpenConfigStep(options, bounded).ExecuteAsync();
        check(bounded.Checks == 2 && bounded.Launched, "OpenConfig: меньший настроенный лимит соблюдается");
        var first = new FakeUi { FoundAt = 1 };
        await new OpenConfigStep(options, first).ExecuteAsync();
        check(first.Indices.Count == 0 && !first.Launched, "OpenConfig: текущее окно проверяется до Alt+Tab");
        using var cancel = new CancellationTokenSource();
        var cancelled = new FakeUi { OnCheck = cancel.Cancel };
        try { await new OpenConfigStep(options, cancelled).ExecuteAsync(cancel.Token); throw new Exception("Отмена потеряна"); }
        catch (OperationCanceledException)
        { check(!cancelled.Launched && cancelled.Indices.Count == 0, "OpenConfig: отмена останавливает переключения и запуск"); }
        var invalid = new FakeUi();
        try { await new OpenConfigStep(new() { Type = "OpenConfig" }, invalid).ExecuteAsync(); throw new Exception("Параметры приняты"); }
        catch (InvalidOperationException)
        { check(!invalid.Prepared, "OpenConfig: параметры проверяются до ввода в RDP"); }
        check(new ScenarioStepDefinition { Type = "openconfig" }.MaxWindowsToCheck == 15 &&
            options.ExecutionContext == ScenarioExecutionContext.Rdp &&
            new ScenarioStepFactory(null!, null!, null!, null!, null!).Create(options) is OpenConfigStep,
            "OpenConfig: defaults, фабрика и RDP-контекст зарегистрированы");
    }

    private sealed class FakeUi : IOpenConfigUi
    {
        public int FoundAt { get; init; } = -1;
        public Action? OnCheck { get; init; }
        public bool Prepared { get; private set; }
        public int Checks { get; private set; }
        public List<int> Indices { get; } = [];
        public ScenarioStepDefinition? Launch { get; private set; }
        public bool Launched => Launch != null;
        public bool Waited { get; private set; }
        public Task PrepareAsync(CancellationToken token) { Prepared = true; return Task.CompletedTask; }
        public Task<bool> IsConfiguratorAsync(CancellationToken token)
        { Checks++; OnCheck?.Invoke(); return Task.FromResult(Checks == FoundAt); }
        public Task SelectWindowAsync(int index, CancellationToken token) { Indices.Add(index); return Task.CompletedTask; }
        public Task LaunchAsync(ScenarioStepDefinition definition, CancellationToken token) { Launch = definition; return Task.CompletedTask; }
        public Task WaitReadyAsync(CancellationToken token) { Waited = true; return Task.CompletedTask; }
    }
}
