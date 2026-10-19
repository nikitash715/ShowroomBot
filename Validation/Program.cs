using ShowroomBot.Core.Scenarios;
using ShowroomBot.Windows;

if (args.Length == 3 && args[0] == "telegram-report")
{
    var settings = new ShowroomBot.Configuration.SettingsService(Path.GetFullPath(args[1])).Load();
    using var http = new System.Net.Http.HttpClient();
    var bot = new ShowroomBot.Telegram.TelegramBotService(http, settings.Telegram,
        _ => Task.FromResult("unavailable"), _ => Task.FromResult("unavailable"), _ => "unavailable");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await bot.SendDevelopmentReportAsync(await File.ReadAllTextAsync(args[2]), timeout.Token);
    Console.WriteLine("Telegram development report delivered.");
    return;
}

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

var continuedSeries = ShowroomBot.Configuration.LaunchOptions.Parse(
    ["--scenario", "3", "--run", "--test-series", "--series-count", "9", "--series-start", "2"]);
Check(continuedSeries.SeriesCount == 9 && continuedSeries.SeriesStart == 2 && continuedSeries.TestSeries,
    "Continue authorized series without rerunning first test");
Check(OneCConfiguratorRecognizer.ParseModuleTitle("Общий модуль Налоговый МониторингПереопределяемый. Модуль") ==
    "НалоговыйМониторингПереопределяемый", "OCR-split caption identifier and period instead of colon retain module identity");
Check(OneCConfiguratorRecognizer.ParseModuleTitle("Документ Налоговый Мониторинг. Форма") is null,
    "Relaxed caption punctuation still requires both module markers");
foreach (var invalidSeries in new[] {
    new[] { "--series-count", "9" },
    new[] { "--scenario", "3", "--run", "--test-series", "--series-count", "0" },
    new[] { "--scenario", "3", "--run", "--test-series", "--series-count", "101" },
    new[] { "--scenario", "3", "--run", "--test-series", "--series-start", "bad" } })
{
    var rejected = false;
    try { ShowroomBot.Configuration.LaunchOptions.Parse(invalidSeries); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid series CLI rejected");
}

// A truncated tab and an OCR-omitted Execute label must still allow the
// dedicated toolbar OCR pass to run from the confirmed Text tab.
using (var consoleImage = new Bitmap(1000, 700))
{
    var layout = ToolkitConsoleRecognizer.Analyze(consoleImage, [
        new("Новый: Консоль разработчика (Toolkit)", new Rectangle(200, 76, 348, 18)),
        new("Текст", new Rectangle(400, 149, 31, 10)),
        new("Параметры", new Rectangle(470, 149, 65, 13))]);
    Check(layout.TextTab != null && layout.Execute == null,
        "Missing Execute must not block toolbar OCR for a truncated console tab");
}

if (args.Length == 3 && args[0] == "opening")
{
    var labels = await new OneCSectionRecognizer().ReadConfiguratorLinesAsync(args[1], default);
    using var screenshot = new Bitmap(args[1]);
    var view = OneCConfiguratorRecognizer.Analyze(screenshot, labels);
    Console.WriteLine($"Opening replay: title={view.ModuleName}, editor={view.Editor}, routines={OneCConfiguratorRecognizer.VisibleRoutines(view).Count}");
    Console.WriteLine("Caption OCR: " + string.Join(" | ", labels.Where(l => l.Bounds.Left > 365 && l.Bounds.Top < 180).Select(l => l.Text)));
    Check(OneCConfiguratorRecognizer.ConfirmsModuleOpening(view with { Editor = null, ModuleName = null, Labels = [] },
        view, args[2]), "Incident with a folded extension procedure confirms the selected module opening");
}
else if (args.Length == 3 && args[0] is "selection" or "reselect")
{
    var recognizer = new OneCSectionRecognizer();
    var labels = await recognizer.ReadConfiguratorLinesAsync(args[1], default);
    using var screenshot = new Bitmap(args[1]);
    var view = OneCConfiguratorRecognizer.Analyze(screenshot, labels);
    Check(OneCConfiguratorRecognizer.HasTreeDockPixels(screenshot, new Rectangle(2, 138, 361, 841)),
        "Cached incident dock is independently confirmed by divider and tree pixels");
    Check(view.Tree != null, "Selection incident has metadata tree");
    var treeBounds = view.Tree!.Value;
    var treeLabels = await recognizer.ReadTreeLinesAsync(args[1], treeBounds, default);
    var modules = OneCConfiguratorRecognizer.CommonModuleRows(treeLabels, treeBounds, null, args[0] == "reselect" ? 64 : 43);
    var selected = ReadCodePrefixNavigation.SelectedRow(screenshot, modules);
    Console.WriteLine($"Selection replay: tree={treeBounds}, selected={selected}, candidates={modules.Length}");
    if (args[0] == "selection")
        Check(ReadCodeModulePrefixes.SelectedCandidate(screenshot, modules, args[2]) != null,
            "Incident selected module can be confirmed before opening");
    else
    {
        var targets = modules.Where(r => OneCConfiguratorRecognizer.MatchesModuleName(r.Text, args[2])).ToArray();
        Check(targets.Length == 1, "Incident target remains uniquely available for a fresh-coordinate retry");
        Console.WriteLine($"Reselect target: {targets[0]}");
    }
}
else if (args.Length == 2 && args[0] is "config" or "tree" or "routines")
{
    var labels = await new OneCSectionRecognizer().ReadConfiguratorLinesAsync(args[1], default);
    using var screenshot = new Bitmap(args[1]);
    var view = OneCConfiguratorRecognizer.Analyze(screenshot, labels);
    Check(OneCConfiguratorRecognizer.IsReadyForMetadata(view),
        "Existing Configurator with an open form provides a ready metadata tree");
    Console.WriteLine($"Configurator replay: tree={view.Tree}, editor={view.Editor}");
    if (args[0] == "routines")
    {
        Check(OneCConfiguratorRecognizer.HasRoutineSyntax(view), "Incident editor has readable procedure declarations");
        Check(OneCConfiguratorRecognizer.ConfirmsModuleOpening(view with { Labels = [], ModuleName = null },
            view, "DifferentCandidate"), "Incident can confirm opening despite a garbled title and a pre-existing form surface");
        Console.WriteLine($"Routines replay: declarations={OneCConfiguratorRecognizer.VisibleRoutines(view).Count}, title={view.ModuleName}");
    }
    if (args[0] == "tree")
    {
        var treeBounds = view.Tree!.Value;
        var treeLabels = await new OneCSectionRecognizer().ReadTreeLinesAsync(args[1], treeBounds, default);
        var parent = OneCConfiguratorRecognizer.FindTreeNode(treeLabels, treeBounds, "Общие модули");
        Check(parent != null, "Incident has an expanded common-modules category");
        var modules = OneCConfiguratorRecognizer.CommonModuleRows(treeLabels, treeBounds, parent, parent!.Bounds.Left);
        var focusRows = modules.Where(r => ReadCodeModulePrefixes.IsNavigationRow(r.Text)).ToArray();
        Console.WriteLine($"Tree parent={parent.Bounds}; modules={string.Join(" | ", modules.Select(r => r.Text))}");
        Check(focusRows.Length >= 2, "CRM-heavy incident viewport provides focus anchors for prefix challenge");
        Check(ReadCodeModulePrefixes.Candidates(modules, new HashSet<string>()).All(r =>
            !new[] { "сам", "сям", "crm" }.Contains(r.Text, StringComparer.OrdinalIgnoreCase)),
            "Incident OCR fragments cannot become module opening candidates");
        Console.WriteLine($"Tree replay: navigation anchors={focusRows.Length}, opening candidates={ReadCodeModulePrefixes.Candidates(modules, new HashSet<string>()).Length}");
    }
}
else if (args.Length == 1)
{
    var incident = await new ToolkitConsoleRecognizer(new OneCSectionRecognizer()).RecognizeAsync(args[0], default);
    Check(incident.TextTab != null && incident.Execute != null && incident.Editor != null,
        "Incident screenshot confirms Text, Execute and editor");
    Check(incident.RowCount.HasValue, "Incident screenshot confirms the result row count");
    Console.WriteLine($"Incident layout: {incident}");
}

// Model real MRU promotion, rather than treating the switch index as an
// absolute position. Targets at every position must be found exactly once.
for (var count = 1; count <= 8; count++)
for (var target = 0; target < count; target++)
{
    var windows = Enumerable.Range(0, count).ToList();
    var seen = new List<int>();
    var found = await RemoteWindowSearch.FindAsync(count, _ =>
    {
        var tail = windows[^1];
        windows.RemoveAt(windows.Count - 1);
        windows.Insert(0, tail);
        return Task.CompletedTask;
    }, (_, _) =>
    {
        seen.Add(windows[0]);
        return Task.FromResult(windows[0] == target);
    }, default);
    Check(found && seen.Distinct().Count() == seen.Count, $"MRU target {target}/{count}");
}
var inspected = 0;
var switched = 0;
Check(!await RemoteWindowSearch.FindAsync(5, _ => { switched++; return Task.CompletedTask; },
    (_, _) => { inspected++; return Task.FromResult(false); }, default) && inspected == 5 && switched == 4,
    "Missing target: bounded search checks current window first");
using (var cancellation = new CancellationTokenSource())
{
    try
    {
        await RemoteWindowSearch.FindAsync(5, _ => Task.CompletedTask, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(true);
        }, cancellation.Token);
        throw new Exception("Cancellation ignored");
    }
    catch (OperationCanceledException) { checks++; }
}

var tree = new Rectangle(0, 0, 150, 200);
var bounds = new Rectangle(20, 30, 800, 600);
var first = new TreeFocusSnapshot(true, false, tree, bounds, "ОбменДанными", "stable");
var down = first with { SelectedModule = "ОбщиеНастройки" };
async Task<bool> Jump(TreeFocusSnapshot[] frames, bool cancelBeforeTyping = false, bool finalGate = true)
{
    var typed = 0;
    var keys = new List<ushort>();
    var queue = new Queue<TreeFocusSnapshot>(frames);
    using var cancellation = new CancellationTokenSource();
    try
    {
        var result = await ReadCodePrefixNavigation.TryJumpAsync("Об", _ =>
        {
            var frame = queue.Dequeue();
            if (cancelBeforeTyping && queue.Count == 0) cancellation.Cancel();
            return Task.FromResult(frame);
        }, (key, _) => { keys.Add(key); return Task.CompletedTask; }, (prefix, _) =>
        {
            if (finalGate) typed++;
            return Task.FromResult(finalGate);
        }, cancellation.Token);
        Check(typed == (result ? 1 : 0), "No text unless all focus gates pass");
        if (result) Check(keys.SequenceEqual(new ushort[] { 0x28, 0x26 }), "Down/Up focus challenge");
        return result;
    }
    catch (OperationCanceledException)
    {
        Check(cancelBeforeTyping && typed == 0, "Cancelled prefix must not type");
        return false;
    }
}
Check(await Jump([first, down, first]), "Verified tree permits prefix");
Check(await Jump([first with { HasEditor = true }, down with { HasEditor = true }, first with { HasEditor = true }]),
    "Existing unchanged form/editor permits verified tree navigation");
Check(await Jump([first with { SelectedModule = "CRM_First" }, down with { SelectedModule = "CRM_Second" },
    first with { SelectedModule = "CRM_First" }]), "CRM rows can confirm tree focus without becoming opening candidates");
Check(!await Jump([first with { IsConfigurator = false }]), "Wrong app");
Check(!await Jump([first with { SelectedModule = null }]), "No active selection");
Check(!await Jump([first, first]), "Click did not move keyboard focus");
Check(!await Jump([first, down with { OutsideTreeHash = "editor changed" }]), "Editor or dialog responds to Down");
Check(!await Jump([first, down with { ClientBounds = bounds with { X = 40 } }]), "RDP moved");
Check(!await Jump([first, down, down]), "Up failed to restore selection");
Check(!await Jump([first, down, first with { HasEditor = true }]), "Editor appeared before typing");
Check(!await Jump([first, down, first], true), "Cancellation before text input");
Check(!await Jump([first, down, first], finalGate: false), "Last pixel gate rejects changed focus");

Check(ReadCodeModulePrefixes.Parse([" Об ", "об", "", "# comment", "А", "Длинный", "12", "За"])
    .SequenceEqual(new[] { "Об", "За" }), "Only unique two-letter prefixes");
Check(ReadCodeModulePrefixes.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())).Length == 0,
    "Missing file preserves fallback");
Check(ReadCodeModulePrefixes.Load(AppContext.BaseDirectory).Contains("Об"), "Resource copied and loaded");
foreach (var name in new[] { "CRM_Test", "crm_Test", "СRМ_Test", "СЯМ_Test", "СРМ_Test", "Обмен_", "сам", "сям", "CRM", "самКлассификаторыВызовСервера", "CRMлокализация" })
    Check(!ReadCodeModulePrefixes.IsAllowedModule(name), $"Exclude {name}");
Check(ReadCodeModulePrefixes.IsAllowedModule("ОбменCRM_Данными"), "CRM inside name is allowed");
var splitRows = OneCConfiguratorRecognizer.CommonModuleRows([
    new("CRM_", new Rectangle(83, 200, 25, 12)),
    new("БазаЗнанийСервер", new Rectangle(112, 200, 160, 12))],
    new Rectangle(2, 138, 361, 841), null, 26);
Check(splitRows.Length == 1 && splitRows[0].Text == "CRM_БазаЗнанийСервер",
    "Module row assembly preserves name fragments beyond the indentation threshold");
var iconParent = new RecognizedText("Э Общие модули", new Rectangle(26, 200, 115, 12));
var hierarchyLabels = new RecognizedText[] { iconParent,
    new("Общие", new Rectangle(64, 200, 35, 12)),
    new("ОбменДанными", new Rectangle(84, 220, 120, 12)),
    new("Подписки на события", new Rectangle(65, 240, 140, 12)),
    new("Регламентные задания", new Rectangle(65, 260, 140, 12)) };
var textIndent = OneCConfiguratorRecognizer.CommonModulesIndent(hierarchyLabels, iconParent);
var hierarchyModules = OneCConfiguratorRecognizer.CommonModuleRows(hierarchyLabels,
    new Rectangle(2, 138, 361, 841), iconParent, textIndent);
Check(textIndent == 64 && hierarchyModules.Length == 1 && hierarchyModules[0].Text == "ОбменДанными",
    "Expansion icon cannot shift parent text indent or turn sibling metadata categories into modules");
var iconHierarchy = hierarchyLabels.Concat(new RecognizedText[] {
    new("Е ОбменДанными", new Rectangle(64, 220, 140, 12)),
    new("Е", new Rectangle(64, 220, 9, 12)) }).ToArray();
var iconModules = OneCConfiguratorRecognizer.CommonModuleRows(iconHierarchy,
    new Rectangle(2, 138, 361, 841), iconParent, textIndent);
Check(iconModules.Length == 1 && iconModules[0].Text == "ОбменДанными" && iconModules[0].Bounds.Left == 84,
    "OCR document icon cannot truncate the module list or shift a caption click to the icon column");
var moduleRows = new[] { "CRM_Test", "СRМ_Test", "ОбменДанными", "ОбщиеНастройки", "Заказы", "Общий_" }
    .Select(name => new RecognizedText(name, new Rectangle(10, 10, 100, 16))).ToArray();
var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ОбменДанными" };
Check(ReadCodeModulePrefixes.Candidates(moduleRows, visited, "об").Select(r => r.Text)
    .SequenceEqual(new[] { "ОбщиеНастройки" }), "Prefix candidates exclude CRM, visited and truncated rows");
Check(ReadCodeModulePrefixes.Candidates(moduleRows, visited).Select(r => r.Text)
    .SequenceEqual(new[] { "ОбщиеНастройки", "Заказы" }), "Fallback candidates still exclude CRM");

var rows = new[] { new RecognizedText("ОбменДанными", new Rectangle(10, 40, 120, 16)),
    new RecognizedText("ОбщиеНастройки", new Rectangle(10, 65, 120, 16)) };
using (var image = new Bitmap(300, 200))
using (var graphics = Graphics.FromImage(image))
{
    graphics.Clear(Color.White);
    graphics.FillRectangle(Brushes.Gray, rows[0].Bounds);
    Check(ReadCodePrefixNavigation.SelectedRow(image, rows) == null, "Inactive grey selection rejected");
    graphics.FillRectangle(Brushes.RoyalBlue, rows[0].Bounds);
    Check(ReadCodePrefixNavigation.SelectedRow(image, rows)?.Text == rows[0].Text, "Active blue selection");
    var hash = ReadCodePrefixNavigation.OutsideTreeHash(image, tree);
    graphics.FillRectangle(Brushes.Gold, rows[1].Bounds);
    Check(ReadCodePrefixNavigation.SelectedRow(image, rows) == null, "Ambiguous selections rejected");
    Check(ReadCodePrefixNavigation.OutsideTreeHash(image, tree) == hash, "Tree movement excluded from outside hash");
    graphics.FillRectangle(Brushes.Black, 180, 90, 10, 10);
    Check(ReadCodePrefixNavigation.OutsideTreeHash(image, tree) != hash, "Outside-tree change detected");
}

// A click on a bottom tree row can scroll that row before the double click.
using (var image = new Bitmap(300, 200))
using (var graphics = Graphics.FromImage(image))
{
    var oldRow = new RecognizedText("ОбменДанными", new Rectangle(20, 170, 140, 14));
    var movedRow = oldRow with { Bounds = oldRow.Bounds with { Y = 145 } };
    graphics.Clear(Color.White);
    graphics.FillRectangle(Brushes.RoyalBlue, movedRow.Bounds);
    Check(ReadCodeModulePrefixes.SelectedCandidate(image, [movedRow], oldRow.Text)?.Bounds == movedRow.Bounds,
        "Opening uses freshly recognized selected row after auto-scroll");
    Check(ReadCodeModulePrefixes.SelectedCandidate(image, [oldRow], oldRow.Text) == null,
        "Stale coordinates cannot confirm selection");
    Check(ReadCodeModulePrefixes.SelectedCandidate(image, [movedRow], "Заказы") == null,
        "A different selected module cannot be opened");
    Check(ReadCodeModulePrefixes.SelectedCandidate(image, [movedRow with { Text = "CRM_Test" }], "CRM_Test") == null,
        "CRM remains excluded after selection");
    graphics.FillRectangle(Brushes.Gray, movedRow.Bounds);
    Check(ReadCodeModulePrefixes.SelectedCandidate(image, [movedRow], oldRow.Text) == null,
        "Inactive tree selection cannot confirm opening");
}
var previousEditor = new ConfiguratorView(true, tree, new Rectangle(180, 0, 100, 180), "ДругойМодуль", []);
var splitSyntax = new ConfiguratorView(true, tree, new Rectangle(180, 0, 600, 180), null, [
    new("Процедура", new Rectangle(200, 40, 70, 12)),
    new("Пересчитать (Форма) Экспорт", new Rectangle(280, 40, 250, 12))]);
Check(OneCConfiguratorRecognizer.VisibleRoutines(splitSyntax).Count == 1 &&
    OneCConfiguratorRecognizer.ConfirmsModuleOpening(previousEditor with { Editor = null }, splitSyntax, "РезервыПоОплатеТрудаКлиент"),
    "Split syntax colours still confirm a newly opened editor through a procedure declaration");
Check(!OneCConfiguratorRecognizer.HasRoutineSyntax(splitSyntax with { Labels = [splitSyntax.Labels[1]] }),
    "An isolated function call without declaration keyword cannot confirm module opening");
Check(OneCConfiguratorRecognizer.HasRoutineSyntax(splitSyntax with { Labels = [
    new("Процедура клв ПередВыполнениемКомандыПечати (Форма) Экспорт", new Rectangle(200, 40, 550, 12))] }),
    "OCR whitespace inside a procedure identifier does not hide an extension declaration");
var initialDock = new Rectangle(2, 153, 361, 826);
var afterClosingDock = new Rectangle(2, 138, 361, 841);
Check(OneCConfiguratorRecognizer.SameTreeDock(initialDock, afterClosingDock),
    "Closing a module can change OCR caption height without moving the metadata dock");
Check(!OneCConfiguratorRecognizer.SameTreeDock(initialDock, afterClosingDock with { Width = 400 }) &&
    !OneCConfiguratorRecognizer.SameTreeDock(initialDock, afterClosingDock with { Y = 70 }),
    "Real dock resize and large displacement cannot be normalized");
Check(!OneCConfiguratorRecognizer.ConfirmsModuleOpening(previousEditor, previousEditor, "ОбменДанными"),
    "Unchanged old editor is not successful module opening");
Check(OneCConfiguratorRecognizer.IsReadyForMetadata(previousEditor), "Open editor does not block metadata readiness");
Check(!OneCConfiguratorRecognizer.IsReadyForMetadata(previousEditor with { Tree = null }),
    "Configurator title alone cannot complete OpenConfig");
Check(!OneCConfiguratorRecognizer.IsReadyForMetadata(previousEditor with { Labels = [
    new("Соединение с хранилищем расширения", new Rectangle(300, 300, 250, 20))] }),
    "Repository modal blocks handoff even when the tree is visible behind it");
var resultLayout = new ToolkitConsoleLayout(null, null, null, null, new Rectangle(0, 100, 200, 200),
    100, false, null, "0,64 с");
var progress = new ToolkitExecutionProgress(resultLayout, "same-result", 2);
Check(!progress.Observe(resultLayout, "same-result") && !progress.Observe(resultLayout, "same-result"),
    "Unchanged previous result must not complete a fresh execution");
Check(!progress.Observe(resultLayout with { ExecutionStamp = "0,65 с" }, "same-result") &&
    progress.Observe(resultLayout with { ExecutionStamp = "0,65 с" }, "same-result"),
    "New execution time can confirm repeated identical data after stable observations");
Check(ToolkitConsoleRecognizer.IsExecutionTime("0,64 с") && ToolkitConsoleRecognizer.IsExecutionTime("39 мс") &&
    !ToolkitConsoleRecognizer.IsExecutionTime("100 строк"), "Execution time accepts Cyrillic seconds but excludes row counts");

// Verify OpenConfig uses the common bounded search and launches only after exhaustion.
var options = new ScenarioStepDefinition { Executable = "1cv8.exe", Server = "server", Database = "base",
    MaxWindowsToCheck = 4 };
var ui = new FakeConfigUi(3);
await new OpenConfigStep(options, ui).ExecuteAsync();
Check(ui.Inspections == 4 && ui.Switches == 3 && !ui.Launched && ui.Ready, "OpenConfig finds fourth window and validates readiness");
ui = new FakeConfigUi(-1);
await new OpenConfigStep(options, ui).ExecuteAsync();
Check(ui.Inspections == 4 && ui.Switches == 3 && ui.Launched && ui.Ready, "OpenConfig launches after bound");
var mailScenario = new ScenarioCatalog().Load().Single(s => Path.GetFileName(s.FilePath) == "Scenario3.yaml");
Check(mailScenario.Name == "Тест" && mailScenario.Definition.Steps.Count > 0 &&
    mailScenario.Definition.Steps[^1].Type == "ReadCode", "Editable Scenario3 remains available with a final reading step");
var testRead = mailScenario.Definition.Steps[^1];
Check(testRead.FastTest && testRead.ReadModuleCount > 0, "Scenario3 uses bounded fast reading passes");
var normalRead = new ScenarioStepDefinition { Type = "ReadCode", ReadModuleCount = 1 };
Check(!normalRead.FastTest && normalRead.ReadDurationSeconds == 43 && normalRead.ReadLinePauseMs == 900,
    "Normal demo timing is preserved");
Check(testRead.ReadyTimeoutSeconds == normalRead.ReadyTimeoutSeconds && testRead.PollIntervalMs == normalRead.PollIntervalMs &&
    testRead.ScrollTimeoutSeconds == normalRead.ScrollTimeoutSeconds, "Fast test preserves readiness and navigation budgets");
using (var cancelledMail = new CancellationTokenSource())
{
    cancelledMail.Cancel();
    try
    {
        await new ShowroomBot.Mail.LocalOutlookInboxReader().ReadUnreadInboxAsync(cancelledMail.Token);
        throw new Exception("Cancelled CheckMail started Outlook");
    }
    catch (OperationCanceledException) { checks++; }
}
var catalog = new ScenarioCatalog().Load();
Check(ShowroomBot.Configuration.LaunchOptions.Parse([]) == new ShowroomBot.Configuration.LaunchOptions(null, false), "Default manual launch");
Check(ShowroomBot.Configuration.LaunchOptions.Parse(["--scenario", "3", "--run"]).Select(catalog).FilePath == mailScenario.FilePath,
    "Number identifies Scenario3.yaml");
Check(ShowroomBot.Configuration.LaunchOptions.Parse(["--scenario", "Тест"]).Select(catalog).Name == "Тест", "Select by name without running");
foreach (var invalid in new[] { new[] { "--run" }, new[] { "--scenario" }, new[] { "--unknown" },
    new[] { "--scenario", "3", "--run", "--run" } })
{
    try { ShowroomBot.Configuration.LaunchOptions.Parse(invalid); throw new Exception("Invalid CLI accepted"); }
    catch (ArgumentException) { checks++; }
}
try
{
    ShowroomBot.Configuration.LaunchOptions.Parse(["--scenario", "missing"]).Select(catalog);
    throw new Exception("Missing scenario accepted");
}
catch (ArgumentException) { checks++; }
Console.WriteLine($"Navigation checks passed: {checks}");

foreach (var outcome in new[] { "success", "error", "cancelled" })
{
    var lifecycle = new List<string>();
    using var cancellation = new CancellationTokenSource();
    var runner = new ScenarioRunner(new FakeScenarioFactory(async token =>
    {
        lifecycle.Add("step");
        await Task.Yield();
        if (outcome == "error") throw new InvalidOperationException("expected failure");
        if (outcome == "cancelled") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
    }), _ => { lifecycle.Add("screenshot"); return "fake.png"; },
        () => throw new Exception("Unexpected local preparation"), () => lifecycle.Add("minimize"));
    runner.ScenarioChanged += note => { if (note.Outcome != ScenarioOutcome.Started) lifecycle.Add("notification"); };
    try
    {
        await runner.RunAsync(new ScenarioDefinition { Name = "RDP cleanup check", Steps = [new() { Type = "ReadCode" }] },
            cancellation.Token);
        Check(outcome == "success", "Only successful lifecycle completes normally");
    }
    catch (OperationCanceledException) { Check(outcome == "cancelled", "Cancellation is preserved through cleanup"); }
    catch (InvalidOperationException) { Check(outcome == "error", "Original failure is preserved through cleanup"); }
    Check(lifecycle.SequenceEqual(new[] { "step", "screenshot", "minimize", "notification" }),
        $"RDP minimizes after diagnostic screenshot and before notification: {outcome}");
}
Console.WriteLine($"All checks passed: {checks}");

Check(!ShowroomBot.Configuration.LaunchOptions.Parse(["--scenario", "3", "--run"]).TestSeries &&
    ShowroomBot.Configuration.LaunchOptions.Parse(["--scenario", "3", "--run", "--test-series"]).TestSeries,
    "Only an explicit CLI test series enables RDP minimization");
try
{
    ShowroomBot.Configuration.LaunchOptions.Parse(["--scenario", "3", "--test-series"]);
    throw new Exception("Test series accepted without run");
}
catch (ArgumentException) { checks++; }
var normalLifecycle = new List<string>();
var normalRunner = new ScenarioRunner(new FakeScenarioFactory(_ => Task.CompletedTask), _ => "fake.png",
    () => throw new Exception("Unexpected local preparation"));
normalRunner.ScenarioChanged += note => { if (note.Outcome == ScenarioOutcome.Completed) normalLifecycle.Add("completed"); };
await normalRunner.RunAsync(new ScenarioDefinition { Name = "Normal mode check", Steps = [new() { Type = "ReadCode" }] });
Check(normalLifecycle.SequenceEqual(new[] { "completed" }), "Normal scenario completes without RDP cleanup");

var drainDirectory = Path.Combine(AppContext.BaseDirectory, "telegram-drain-check");
Directory.CreateDirectory(drainDirectory);
var drainScreenshot = Path.Combine(drainDirectory, "fake.png");
await File.WriteAllBytesAsync(drainScreenshot, [137, 80, 78, 71]); // Mock transport never sends this file to a server.
using (var transport = new TelegramDrainHandler())
using (var client = new System.Net.Http.HttpClient(transport))
using (var stopPolling = new CancellationTokenSource())
{
    var service = new ShowroomBot.Telegram.TelegramBotService(client,
        new ShowroomBot.Configuration.TelegramSettings { Enabled = true, NotifyDemoEvents = true,
            BotToken = "123456:unit_test", AllowedUserId = 42 },
        _ => Task.FromResult("unused"), _ => Task.FromResult("unused"), _ => throw new Exception("Unexpected capture"));
    service.QueueNotification(new("Queue drain test", ScenarioOutcome.Started));
    var serviceTask = service.RunAsync(stopPolling.Token);
    await transport.PollStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    service.QueueNotification(new("Queue drain test", ScenarioOutcome.Completed, ScreenshotPath: drainScreenshot));
    await transport.FinalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    stopPolling.Cancel();
    await Task.Delay(30);
    Check(!serviceTask.IsCompleted, "Shutdown waits for the in-flight final Telegram outcome");
    transport.AllowFinal.TrySetResult();
    await serviceTask.WaitAsync(TimeSpan.FromSeconds(5));
    Check(transport.Delivered.SequenceEqual(new[] { "started", "completed", "document" }),
        "Final text and screenshot drain in order after polling cancellation");
}
{
    var directory = Path.Combine(AppContext.BaseDirectory, "development-check-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var mailbox = new ShowroomBot.Telegram.DevelopmentMailbox(directory);
    var pending = Path.Combine(directory, "report.pending.txt");
    File.WriteAllText(pending, "Series: 10");
    try
    {
        await mailbox.DeliverPendingReportAsync((_, _) => Task.FromException(new IOException("offline")), default);
    }
    catch (IOException) { }
    Check(File.Exists(pending), "Failed development report remains pending");
    string? delivered = null;
    await mailbox.DeliverPendingReportAsync((text, _) => { delivered = text; return Task.CompletedTask; }, default);
    Check(delivered == "Series: 10" && mailbox.ReadReport() == delivered && !File.Exists(pending),
        "Development report acknowledged only after delivery");
    using var http = new System.Net.Http.HttpClient(new TelegramDrainHandler());
    var bot = new ShowroomBot.Telegram.TelegramBotService(http,
        new ShowroomBot.Configuration.TelegramSettings { Enabled = true, AllowedUserId = 1234, BotToken = "123:abc" },
        _ => Task.FromResult("start"), _ => Task.FromResult("stop"), _ => "unused",
        developmentDirectory: directory);
    var handle = typeof(ShowroomBot.Telegram.TelegramBotService).GetMethod("HandleMessageAsync",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    async Task Message(long sender, string chatType, long chatId, string text)
    {
        using var json = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(new
        { from = new { id = sender }, chat = new { id = chatId, type = chatType }, message_id = 7, text }));
        await (Task)handle.Invoke(bot, new object[] { json.RootElement, CancellationToken.None })!;
    }
    await Message(99, "private", 1234, "/codex spoof");
    await Message(1234, "group", 1234, "/codex group");
    await Message(1234, "private", 99, "/codex wrongchat");
    var inbox = Path.Combine(directory, "instructions.jsonl");
    Check(!File.Exists(inbox), "Development inbox rejects unauthorized sender, group and wrong chat");
    await Message(1234, "private", 1234, "/codex");
    Check(!File.Exists(inbox), "Empty development instruction is not queued");
    await Message(1234, "private", 1234, "/codex Проверь скорость");
    using var instruction = System.Text.Json.JsonDocument.Parse(File.ReadAllText(inbox));
    Check(instruction.RootElement.GetProperty("text").GetString() == "Проверь скорость" &&
        instruction.RootElement.GetProperty("userId").GetInt64() == 1234,
        "Authorized development instruction preserves text and provenance");
    await Message(1234, "private", 1234, "/codex Проверь скорость");
    Check(File.ReadAllLines(inbox).Length == 1, "Replayed Telegram instruction is not queued twice after restart");
}
Console.WriteLine($"Final checks passed: {checks}");

sealed class TelegramDrainHandler : System.Net.Http.HttpMessageHandler
{
    public TaskCompletionSource PollStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource FinalStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource AllowFinal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public System.Collections.Concurrent.ConcurrentQueue<string> Delivered = new();
    protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(
        System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.RequestUri!.Segments[^1];
        if (method == "getUpdates")
        {
            PollStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        if (method == "sendMessage")
        {
            using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var final = body.RootElement.GetProperty("text").GetString()!.Contains("завершён");
            if (final)
            {
                FinalStarted.TrySetResult();
                await AllowFinal.Task.WaitAsync(cancellationToken);
            }
            Delivered.Enqueue(final ? "completed" : "started");
        }
        if (method == "sendDocument") Delivered.Enqueue("document");
        return new(System.Net.HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent("{\"ok\":true,\"result\":[]}")
        };
    }
}

sealed class FakeScenarioFactory(Func<CancellationToken, Task> execute) : IScenarioStepFactory
{
    public IScenarioStep Create(ScenarioStepDefinition definition) => new FakeScenarioStep(execute);
}

sealed class FakeScenarioStep(Func<CancellationToken, Task> execute) : IScenarioStep
{
    public string Name => "Lifecycle check";
    public Task ExecuteAsync(CancellationToken cancellationToken = default) => execute(cancellationToken);
}

sealed class FakeConfigUi(int target) : IOpenConfigUi
{
    public int Inspections;
    public int Switches;
    public bool Launched;
    public bool Ready;
    public Task PrepareAsync(CancellationToken token) => Task.CompletedTask;
    public Task<bool> IsConfiguratorAsync(CancellationToken token) => Task.FromResult(Inspections++ == target);
    public Task SelectNextWindowAsync(CancellationToken token) { Switches++; return Task.CompletedTask; }
    public Task LaunchAsync(ScenarioStepDefinition definition, CancellationToken token) { Launched = true; return Task.CompletedTask; }
    public Task WaitReadyAsync(CancellationToken token) { Ready = true; return Task.CompletedTask; }
}
