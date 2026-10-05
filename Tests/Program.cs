using System.Drawing;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Windows;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using ShowroomBot.Configuration;
using ShowroomBot.Rdp;
using ShowroomBot.Core;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    Console.WriteLine($"PASS: {message}");
}

await RdpChecks.RunAsync(Check);
var diagnosticsRoot = Path.Combine(Path.GetTempPath(), $"showroombot-cleanup-{Guid.NewGuid():N}");
try
{
    var nowUtc = DateTime.UtcNow;
    var cutoff = nowUtc.AddDays(-2);
    DiagnosticsCleanup.Clean(diagnosticsRoot, nowUtc);
    Check(!Directory.Exists(diagnosticsRoot), "diagnostics: missing directory is ignored");
    Directory.CreateDirectory(diagnosticsRoot);
    string DiagnosticFile(string relativePath, DateTime timestamp)
    {
        var path = Path.Combine(diagnosticsRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "diagnostic");
        File.SetLastWriteTimeUtc(path, timestamp);
        return path;
    }
    var oldFile = DiagnosticFile("old.log", cutoff.AddSeconds(-1));
    var boundaryFile = DiagnosticFile("boundary.log", cutoff);
    var recentFile = DiagnosticFile("recent.log", nowUtc);
    var nestedOld = DiagnosticFile("old-run/nested/old.png", cutoff.AddDays(-1));
    Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(nestedOld)!, cutoff.AddDays(-1));
    Directory.SetLastWriteTimeUtc(Path.Combine(diagnosticsRoot, "old-run"), cutoff.AddDays(-1));
    var mixedRecent = DiagnosticFile("mixed/recent.log", nowUtc);
    var mixedOld = DiagnosticFile("mixed/old.log", cutoff.AddDays(-1));
    Directory.SetLastWriteTimeUtc(Path.Combine(diagnosticsRoot, "mixed"), cutoff.AddDays(-1));
    var lockedFile = DiagnosticFile("locked.log", cutoff.AddDays(-1));
    using (var locked = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        DiagnosticsCleanup.Clean(diagnosticsRoot, nowUtc);
        Check(File.Exists(lockedFile), "diagnostics: locked file does not interrupt cleanup");
    }
    Check(!File.Exists(oldFile) && !File.Exists(nestedOld) &&
        !Directory.Exists(Path.Combine(diagnosticsRoot, "old-run")), "diagnostics: old files and nested folders removed");
    Check(File.Exists(boundaryFile) && File.Exists(recentFile), "diagnostics: retain last 48 hours including boundary");
    Check(File.Exists(mixedRecent) && !File.Exists(mixedOld), "diagnostics: recent files in old folders preserved");
    DiagnosticsCleanup.Clean(diagnosticsRoot, nowUtc);
    Check(!File.Exists(lockedFile) && Directory.Exists(diagnosticsRoot), "diagnostics: cleanup can be repeated and retains root");
}
finally
{
    // This absolute path was created above under the temporary directory with a unique task prefix.
    if (Directory.Exists(diagnosticsRoot)) Directory.Delete(diagnosticsRoot, recursive: true);
}

var idleClock = new ManualTimeProvider();
var autoStart = new IdleAutoStartTimer(idleClock);
var threshold = TimeSpan.FromMinutes(2);
var longIdle = TimeSpan.FromHours(1);
Check(!autoStart.ShouldStart(false, longIdle, threshold, false, true), "autostart: disabled");
Check(!autoStart.ShouldStart(true, threshold - TimeSpan.FromSeconds(1), threshold, false, true),
    "autostart: wait for idle threshold");
Check(autoStart.ShouldStart(true, threshold, threshold, false, true), "autostart: starts at threshold");
Check(!autoStart.ShouldStart(true, longIdle, threshold, true, true), "autostart: no concurrent scenario");
Check(!autoStart.ShouldStart(true, longIdle, threshold, false, false), "autostart: wait for VPN/RDP");
Check(autoStart.ShouldStart(true, longIdle, threshold, false, true), "autostart: infrastructure recovery permits start");
autoStart.RestartInterval();
var repeatedEarly = false;
for (var second = 0; second < 120; second++)
{
    repeatedEarly |= autoStart.ShouldStart(true, longIdle, threshold, false, true);
    idleClock.Advance(TimeSpan.FromSeconds(1));
}
Check(!repeatedEarly, "autostart: no repeat on any timer tick before new interval expires");
Check(autoStart.ShouldStart(true, longIdle, threshold, false, true), "autostart: repeat after full new interval");
Check(!autoStart.ShouldStart(true, TimeSpan.FromSeconds(1), threshold, false, true),
    "autostart: user activity requires new idle interval");
autoStart.RestartInterval();
Check(!autoStart.ShouldStart(true, longIdle, threshold, false, true),
    "autostart: completion/cancellation of next or manual scenario resets interval again");
var demoController = new DemoController();
demoController.StartDemo(automatic: true);
Check(demoController.WasStartedAutomatically, "autostart: automatic marker");
demoController.StopDemo();
demoController.StartDemo(automatic: false);
Check(!demoController.WasStartedAutomatically, "manual start: manual marker");
demoController.StopDemo();
Check(demoController.State == AppState.Waiting, "stop: returns to waiting");

// Check sizing on secondary monitors without activating a real RDP session.
var sizingCheck = typeof(RdpController).GetMethod("NeedsMaximize",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
var rectType = sizingCheck.GetParameters()[0].ParameterType;
object Rect(int left, int top, int right, int bottom)
{
    var rect = Activator.CreateInstance(rectType)!;
    foreach (var (name, value) in new[] { ("Left", left), ("Top", top), ("Right", right), ("Bottom", bottom) })
        rectType.GetField(name)!.SetValue(rect, value);
    return rect;
}
bool NeedsMaximize(object window, object work) => (bool)sizingCheck.Invoke(null, [window, work])!;
var workArea = Rect(-2000, -100, 0, 900);
Check(NeedsMaximize(Rect(-2000, -100, -201, 900), workArea), "RDP: narrow window needs maximize");
Check(NeedsMaximize(Rect(-2000, -100, 0, 799), workArea), "RDP: short window needs maximize");
Check(!NeedsMaximize(Rect(-2000, -100, -200, 800), workArea), "RDP: exactly 90% on both axes stays unchanged");
Check(!NeedsMaximize(Rect(-2000, -100, 0, 900), workArea), "RDP: large window stays unchanged");
Check(NeedsMaximize(Rect(211, 1001, 1954, 2017), Rect(0, 0, 1920, 1040)), "RDP: large window mostly outside monitor needs maximize");
Check(NeedsMaximize(Rect(-2201, -100, -201, 900), workArea), "RDP: window clipped on left needs maximize");
Check(!NeedsMaximize(Rect(-2010, -110, 10, 910), workArea), "RDP: maximized border outside work area stays unchanged");
Check(!NeedsMaximize(Rect(0, 0, 100, 100), Rect(0, 0, 0, 0)), "RDP: empty work area is ignored");

var typingSettings = new TypingSettings();
var tempo = new TypingTempo(typingSettings, new Random(42));
var delays = Enumerable.Range(0, 10000).Select(_ => tempo.NextDelay()).ToArray();
Check(delays.Min() >= 80 && delays.Distinct().Count() > 100 && delays.Any(d => d >= 880),
    "темп меняется, не превышает прежнюю скорость и содержит редкие паузы");
foreach (var target in new[] { new Point(1000, 600), new Point(-500, -300), new Point(0, 0) })
{
    var path = new MouseTrajectory(Point.Empty, target, new MouseSettings(), new Random(42));
    Check(path.At(0) == Point.Empty && path.At(1) == target, "траектория точно достигает цели, включая отрицательные координаты");
}
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await new RdpController("unused.example").OpenOrActivateAsync(cancelled.Token); throw new Exception("Отмена открытия RDP потеряна"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: отмена до открытия или активации RDP"); }
    try { await new KeyboardInputSender().SendTextAsync("не отправлять", cancelled.Token); throw new Exception("Отмена текста потеряна"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: отмена до отправки текста"); }
    try { await new MouseInputSender().MoveToAsync(new Point(10, 10), cancelled.Token); throw new Exception("Отмена мыши потеряна"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: отмена до перемещения мыши"); }
}
var configPath = Path.GetTempFileName();
try
{
    var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "configexample.yaml"));
    File.WriteAllText(configPath, source);
    var service = new SettingsService(configPath);
    var settings = service.Load();
    service.Save(settings);
    Check(File.ReadAllText(configPath) == source, "сохранение без изменений сохраняет конфиг побайтно с комментариями");
    settings.IdleMinutes++;
    service.Save(settings);
    Check(service.Load().IdleMinutes == settings.IdleMinutes && File.ReadAllText(configPath).Contains("# Общие настройки"),
        "изменение настройки сохраняет комментарии и корректный YAML");
    File.WriteAllText(configPath, "# заголовок\nidleMinutes: 17 # inline\nautomation:\n  mouse:\n    movementDurationMilliseconds: 410 # длительность\n    stepDelayMilliseconds: 12\nunknown: 'keep' # сохранить\n");
    var migrated = service.Load();
    Check(!migrated.Telegram.Enabled && migrated.Telegram.AllowedUserId == 0 &&
        migrated.Telegram.BotToken == "", "Telegram: старый конфиг оставляет интеграцию выключенной");
    service.Save(migrated);
    var saved = File.ReadAllText(configPath);
    Check(service.Load().Automation.Typing.MinimumDelayMilliseconds == 80 && saved.Contains("# inline") &&
        saved.Contains("unknown: 'keep' # сохранить") && service.Load().Automation.Mouse.MovementDurationMilliseconds == 410,
        "добавление отсутствующих параметров сохраняет комментарии, неизвестные ключи и значения");
    File.WriteAllText(configPath, "vpn:\n  connectionNames:\n    - 'one' # первая\n    # заметка\n    - 'two' # вторая\nidleMinutes: 17 # конец\n");
    var names = service.Load();
    names.Vpn.ConnectionNames = ["changed, with comma", "two # name", "three"];
    service.Save(names);
    Check(service.Load().Vpn.ConnectionNames.SequenceEqual(names.Vpn.ConnectionNames) &&
        new[] { "первая", "заметка", "вторая", "конец" }.All(c => File.ReadAllText(configPath).Contains(c)),
        "изменение размера списка сохраняет все комментарии и значения");
    File.WriteAllText(configPath, "invalid: [\n");
    try { service.Load(); throw new Exception("Повреждённый YAML принят"); }
    catch (InvalidDataException) { Check(File.ReadAllText(configPath) == "invalid: [\n", "ошибка чтения не перезаписывает настройки"); }
}
finally { File.Delete(configPath); }
using (var execution = new ScenarioExecution(CancellationToken.None))
{
    ScenarioExecution.CancelCurrent();
    try { await new KeyboardInputSender().SendTextAsync("не отправлять"); throw new Exception("Аварийная остановка текста потеряна"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: аварийная остановка общего набора"); }
    try { await new MouseInputSender().MoveToAsync(new Point(1, 1)); throw new Exception("Аварийная остановка мыши потеряна"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: аварийная остановка общего движения мыши"); }
}

// Inspect packets without sending any keys to the active window.
var keyFactory = typeof(KeyboardInputSender).GetMethod("CreateVirtualKeyInput",
    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
foreach (ushort key in new ushort[] { 0x21, 0x24, 0x23, 0x2E }) // PageUp, Home, End, Delete
{
    foreach (var keyUp in new[] { false, true })
    {
        var packet = keyFactory.Invoke(null, new object[] { key, keyUp })!;
        var data = packet.GetType().GetField("data")!.GetValue(packet)!;
        var input = data.GetType().GetField("keyboardInput")!.GetValue(data)!;
        var flags = (uint)input.GetType().GetField("dwFlags")!.GetValue(input)!;
        var scan = (ushort)input.GetType().GetField("wScan")!.GetValue(input)!;
        var expectedScan = key switch { 0x21 => 0x49, 0x24 => 0x47, 0x23 => 0x4F, _ => 0x53 };
        Check(flags == (keyUp ? 11u : 9u) && scan == expectedScan,
            $"RDP navigation key {key:X2}, keyUp={keyUp}: extended flag preserves navigation with NumLock");
    }
}

Check(!OneCBaseRecognizer.ClassifyWindow(true, "1С: Конфигуратор").IsTargetClient, "конфигуратор исключён при совпадении цвета");
Check(OneCBaseRecognizer.ClassifyWindow(true, "1С: Предприятие").IsTargetClient,
    "пользовательский клиент принимается при совпадении цвета");
var noPanelPath = Path.Combine(Path.GetTempPath(), $"open1c-no-panel-{Guid.NewGuid():N}.png");
try
{
    // Too wide for Windows OCR: a negative colour check must return before invoking it.
    using var image = new Bitmap(20000, 40);
    using var graphics = Graphics.FromImage(image);
    graphics.Clear(Color.White);
    image.Save(noPanelPath);
    var recognition = await OneCBaseRecognizer.RecognizeWindowAsync(
        noPanelPath, Color.FromArgb(192, 220, 192), 0, CancellationToken.None);
    Check(!recognition.IsTargetClient &&
        !File.Exists(Path.ChangeExtension(noPanelPath, ".window-header.png")) &&
        !File.Exists(Path.ChangeExtension(noPanelPath, ".window-header.txt")),
        "окно без цвета панели пропускается без подготовки заголовка и OCR");
    using var panelBrush = new SolidBrush(Color.FromArgb(192, 220, 192));
    graphics.FillRectangle(panelBrush, 0, 0, 80, 28);
    Check(!OneCBaseRecognizer.HasPanelColor(image, Color.FromArgb(192, 220, 192), 0),
        "семь строк совпадающего цвета недостаточны для панели");
    graphics.FillRectangle(panelBrush, 0, 28, 80, 4);
    Check(OneCBaseRecognizer.HasPanelColor(image, Color.FromArgb(192, 220, 192), 0),
        "восемь строк совпадающего цвета подтверждают панель");
}
finally
{
    File.Delete(noPanelPath);
    File.Delete(Path.ChangeExtension(noPanelPath, ".window-header.png"));
    File.Delete(Path.ChangeExtension(noPanelPath, ".window-header.txt"));
}
foreach (var (scale, offset) in new[] { (1, 0), (2, 40) })
{
    using var image = new Bitmap(1000 * scale + offset, 800 * scale + offset);
    using var graphics = Graphics.FromImage(image);
    graphics.Clear(Color.White);
    Rectangle R(int x, int y, int w, int h) => new(x * scale + offset, y * scale + offset, w * scale, h * scale);
    graphics.DrawRectangle(Pens.Gray, R(300, 150, 650, 220));
    graphics.DrawRectangle(Pens.Gray, R(150, 450, 800, 300));
    var labels = new[]
    {
        new RecognizedText("Новый: Консоль разработчика (Toolkit)", R(150, 20, 400, 16)),
        new RecognizedText("Новый: Консоль разработчика (Toolkit)", R(150, 50, 400, 16)),
        new RecognizedText("Выполнить", R(200, 90, 80, 16)),
        new RecognizedText("Текст", R(305, 125, 40, 16)),
        new RecognizedText("Проверить", R(850, 390, 80, 16)),
        new RecognizedText("Результат (Таблица, 100 строк)", R(165, 410, 230, 16))
    };
    var layout = ToolkitConsoleRecognizer.Analyze(image, labels);
    Check(layout.Editor.HasValue && layout.Result.HasValue && layout.RowCount == 100,
        $"границы редактора и результата при масштабе {scale} и смещении {offset}");
    Check(layout.Execute == labels[2].Bounds, "верхняя кнопка Выполнить отделена от Проверить");
    var consoleWordBounds = R(450, 20, 70, 16);
    var mergedTabs = labels.Select((l, index) => index == 0 ? l with
        { Text = "Начальная страница Другая вкладка Новый: Консоль разработчика (Toolkit)",
          Bounds = R(20, 20, 700, 16) } : l)
        .Append(new RecognizedText("Консоль", consoleWordBounds)).ToArray();
    Check(ToolkitConsoleRecognizer.Analyze(image, mergedTabs).ConsoleTab == consoleWordBounds,
        "объединённая OCR строка вкладок направляет клик на слово Консоль");
    var missingSuffix = labels.Select((l, index) => index == 1
        ? l with { Text = "Новый: Консоль разработчика" } : l).ToArray();
    var missingSuffixLayout = ToolkitConsoleRecognizer.Analyze(image, missingSuffix);
    Check(missingSuffixLayout.Editor == layout.Editor && missingSuffixLayout.Execute == layout.Execute,
        "пропуск Toolkit в заголовке формы не теряет редактор и Выполнить");
    var missingTabSuffix = labels.Select((l, index) => index == 0
        ? l with { Text = "Новый: Консоль разработчика" } : l).ToArray();
    Check(ToolkitConsoleRecognizer.Analyze(image, missingTabSuffix).Editor == layout.Editor,
        "пропуск Toolkit во вкладке не теряет активную форму");
    Check(ToolkitConsoleRecognizer.Analyze(image, labels.Select(l => l with
        { Text = l.Text.Replace("(Toolkit)", "") }).ToArray()).Editor == null,
        "консоль без подтверждения Toolkit не принимается");
    Check(ToolkitConsoleRecognizer.Analyze(image, labels.Where((_, index) => index != 1).ToArray()).Editor == null,
        "вкладка неактивной консоли не принимается за активную форму");
    graphics.DrawRectangle(Pens.Red, R(300, 150, 650, 220));
    var errorLabels = labels.Select(l => l with { Text = l.Text.Replace("Toolkit", "Tolkit") }).Append(
        new RecognizedText("(1, 1) Ожидается ВЫБРАТЬ", R(330, 390, 220, 16))).ToArray();
    var errorLayout = ToolkitConsoleRecognizer.Analyze(image, errorLabels);
    Check(errorLayout.Editor.HasValue && errorLayout.Execute == labels[2].Bounds && errorLayout.Error != null,
        "красная рамка, ошибка синтаксиса и OCR Tolkit не теряют кнопку Выполнить");
    foreach (var message in new[] { "(2, 5) Таблица не найдена РаспределениеЗапасов", "(2_ 5) Таблица не найдена РаспределениеЗапасов", "Поле не найдено Номенклатура", "Параметр не найден Склад" })
    {
        var missingTable = ToolkitConsoleRecognizer.Analyze(image, labels.Append(
            new RecognizedText(message, R(330, 390, 500, 16))).ToArray());
        Check(missingTable.Error == message, $"ошибка 1С распознана: {message}");
        var queryText = ToolkitConsoleRecognizer.Analyze(image, labels.Append(
            new RecognizedText(message, R(330, 200, 500, 16))).ToArray());
        Check(queryText.Error == null, "текст внутри запроса не принимается за ошибку");
    }
}
var frame = new Rectangle(10, 10, 100, 100);
Check(RdpClipboardQueryInput.NormalizeNewlines("ВЫБРАТЬ\r\n    Количество\r\nИЗ") == "ВЫБРАТЬ\n    Количество\nИЗ",
    "сравнение вставки сохраняет отступы и границу строки перед ИЗ");
var expectedQuery = "ВЫБРАТЬ\n    Количество\nИЗ";
RdpClipboardQueryInput.ValidateCopiedText(expectedQuery, expectedQuery.Replace("\n", "\r\n"));
foreach (var corrupted in new[] { "ф" + expectedQuery, expectedQuery.Replace("Количество\nИЗ", "КоличествоИЗ"),
    expectedQuery.Replace("    Количество", "        Количество") })
{
    try { RdpClipboardQueryInput.ValidateCopiedText(expectedQuery, corrupted); throw new Exception("Повреждённый запрос принят"); }
    catch (InvalidOperationException) { Console.WriteLine("PASS: повреждённый ввод блокирует запуск запроса"); }
}
var old = new ToolkitConsoleLayout(frame, frame, frame, frame, frame, 100, false, null, "7 мс");
var progress = new ToolkitExecutionProgress(old, "old", 3);
Check(!Enumerable.Range(0, 10).Any(_ => progress.Observe(old, "old")), "старый стабильный результат не завершает новый запуск");
Check(!progress.Observe(old with { Result = null, RowCount = null }, null) &&
    !Enumerable.Range(0, 5).Any(_ => progress.Observe(old, "old")), "сбой распознавания не подтверждает новый запуск");
Check(!progress.Observe(old with { Busy = true }, "old"), "выполнение не считается завершённым");
Check(!progress.Observe(old, "old") && !progress.Observe(old, "old") && progress.Observe(old, "old"),
    "идентичный результат принят после наблюдаемого выполнения и стабилизации");
progress = new ToolkitExecutionProgress(old, "old", 2);
var empty = old with { RowCount = 0, Result = null };
Check(!progress.Observe(empty, null) && progress.Observe(empty, null), "пустой новый результат успешно завершает шаг");
progress = new ToolkitExecutionProgress(old, "old", 2);
Check(!progress.Observe(old with { Error = "Ошибка запроса", ExecutionStamp = "8 мс" }, "new"), "ошибка не принимается за успех");
var staleError = old with { Error = "Ошибка запроса" };
progress = new ToolkitExecutionProgress(staleError, "old", 2);
Check(!Enumerable.Range(0, 10).Any(_ => progress.Observe(staleError, "old") || progress.HasConfirmedError),
    "старая ошибка не подтверждается по истечении времени");
Check(!progress.Observe(staleError, "new") && !progress.HasConfirmedError &&
    progress.Observe(staleError, "new") && !progress.HasConfirmedError,
    "новый стабильный результат имеет приоритет над сообщением об ошибке");
progress = new ToolkitExecutionProgress(old, "old", 2);
Check(!progress.Observe(staleError, "old") && !progress.HasConfirmedError,
    "одного распознавания ошибки недостаточно для fallback");
Check(!progress.Observe(old, "old") && !progress.HasConfirmedError &&
    !progress.Observe(staleError, "old") && !progress.HasConfirmedError &&
    !progress.Observe(staleError, "old") && progress.HasConfirmedError,
    "новая ошибка подтверждается только последовательными наблюдениями");
progress = new ToolkitExecutionProgress(staleError, "old", 2);
Check(!progress.Observe(old with { Busy = true }, "old") &&
    !progress.Observe(staleError, "old") && !progress.HasConfirmedError &&
    !progress.Observe(staleError, "old") && progress.HasConfirmedError,
    "повтор той же ошибки подтверждается после её исчезновения в текущем запуске");
progress = new ToolkitExecutionProgress(old, "old", 2);
Check(!progress.Observe(staleError with { Busy = true }, "old") && !progress.HasConfirmedError &&
    !progress.Observe(staleError, "old") && !progress.HasConfirmedError,
    "сообщение во время выполнения не учитывается для подтверждения ошибки");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    using var image = new Bitmap(800, 600);
    try
    {
        ToolkitConsoleRecognizer.Analyze(image, new[] {
            new RecognizedText("Результат (Таблица, 1 строка)", new Rectangle(100, 100, 240, 16)) }, cancellation.Token);
        throw new Exception("Отмена не сработала");
    }
    catch (OperationCanceledException) { Console.WriteLine("PASS: отмена распознавания геометрии"); }
}
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var yaml = File.ReadAllText(Path.Combine(root, "Examples/ScenarioReference.example.yaml"));
var definition = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()
    .Deserialize<ScenarioDefinition>(yaml);
Check(definition.Steps.Count == 5 && definition.Steps[0].Type == "Open1C" &&
    definition.Steps[3].Type == "ExecuteToolkitQuery" && definition.Steps[3].ScrollNotches == 2,
    "пример YAML десериализуется с параметрами шага");
Check(definition.Steps[3].QueryInputMode == "typing", "справочник использует набор по умолчанию");
Check(!string.IsNullOrWhiteSpace(definition.Steps[0].Executable) &&
    !string.IsNullOrWhiteSpace(definition.Steps[0].Database) &&
    string.IsNullOrEmpty(definition.Steps[3].Executable) &&
    string.IsNullOrEmpty(definition.Steps[3].Server) &&
    string.IsNullOrEmpty(definition.Steps[3].Database) &&
    string.IsNullOrEmpty(definition.Steps[3].User) &&
    string.IsNullOrEmpty(definition.Steps[3].Password),
    "параметры запуска базы задаются только в отдельном Open1C");
var scenarioDeserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
var minimalStep = scenarioDeserializer.Deserialize<ScenarioStepDefinition>("type: Open1C\n");
Check(minimalStep.AfterActivationDelayMs == 1000 && minimalStep.AfterRunDialogDelayMs == 500 &&
    minimalStep.WindowSwitchDelayMs == 700 && minimalStep.PollIntervalMs == 30000 && minimalStep.ScrollPauseMs == 700 &&
    minimalStep.ReadyTimeoutSeconds == 420 && minimalStep.SectionOpenTimeoutSeconds == 5 && minimalStep.CommandTimeoutSeconds == 15 &&
    minimalStep.ConsoleTimeoutSeconds == 30 && minimalStep.QueryInputTimeoutSeconds == 20 &&
    minimalStep.QueryTimeoutSeconds == 120 && minimalStep.ScrollTimeoutSeconds == 300,
    "YAML без параметров времени получает задержки и таймауты из кода");
Check(new[] { "Open1CCommand", "ExecuteToolkitQuery" }.All(type =>
    scenarioDeserializer.Deserialize<ScenarioStepDefinition>($"type: {type}\n").PollIntervalMs == 500),
    "остальные шаги сохраняют интервал проверки 500 мс");
Check(scenarioDeserializer.Deserialize<ScenarioStepDefinition>(
    "pollIntervalMs: 250\nreadyTimeoutSeconds: 90\ntype: open1c\n").PollIntervalMs == 250 &&
    scenarioDeserializer.Deserialize<ScenarioStepDefinition>("type: Open1C\nreadyTimeoutSeconds: 90\n").ReadyTimeoutSeconds == 90,
    "явные настройки Open1C переопределяют defaults независимо от порядка полей YAML");
Check(definition.Steps[0].ReadyTimeoutSeconds == 420 && definition.Steps[0].PollIntervalMs == 30000,
    "пример Open1C ожидает запуск 7 минут с проверкой каждые 30 секунд");
var overriddenStep = scenarioDeserializer.Deserialize<ScenarioStepDefinition>(
    "type: Open1C\nafterActivationDelayMs: 0\nafterRunDialogDelayMs: 123\npollIntervalMs: 250\nscrollTimeoutSeconds: 180\n");
Check(overriddenStep.AfterActivationDelayMs == 0 && overriddenStep.AfterRunDialogDelayMs == 123 &&
    overriddenStep.PollIntervalMs == 250 && overriddenStep.ScrollTimeoutSeconds == 180,
    "явные параметры времени переопределяют значения по умолчанию, включая нулевую паузу");
Check(definition.Steps.Select(step => step.Type).SequenceEqual(
    new[] { "Open1C", "Open1CSection", "Open1CCommand", "ExecuteToolkitQuery", "Wait" }), "справочник содержит все типы шагов");
var stepFactory = new ScenarioStepFactory(null!, null!, null!, null!, null!);
Check(definition.Steps[4].Seconds == 5 && stepFactory.Create(definition.Steps[4]) is WaitStep,
    "пример Wait десериализуется и создаётся фабрикой");
var waitDefinition = scenarioDeserializer.Deserialize<ScenarioStepDefinition>("type: wait\nseconds: 1\n");
var waitStep = stepFactory.Create(waitDefinition);
Check(waitStep is WaitStep, "фабрика распознаёт Wait без учёта регистра");
var waitTimer = System.Diagnostics.Stopwatch.StartNew();
var waitTask = waitStep.ExecuteAsync();
Check(!waitTask.IsCompleted, "Wait ожидает асинхронно");
await waitTask;
Check(waitTimer.Elapsed >= TimeSpan.FromSeconds(1), "Wait ожидает указанное число секунд");
foreach (var invalidSeconds in new[] { 0, -1 })
{
    try
    {
        await stepFactory.Create(new ScenarioStepDefinition { Type = "Wait", Seconds = invalidSeconds }).ExecuteAsync();
        throw new Exception("Неположительное время ожидания принято");
    }
    catch (InvalidOperationException error) when (error.Message.Contains("seconds"))
    { Console.WriteLine("PASS: неположительное или отсутствующее seconds отклонено"); }
}
using (var waitCancellation = new CancellationTokenSource())
{
    var pendingWait = stepFactory.Create(new ScenarioStepDefinition { Type = "Wait", Seconds = 60 })
        .ExecuteAsync(waitCancellation.Token);
    waitCancellation.Cancel();
    try { await pendingWait; throw new Exception("Отмена Wait потеряна"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: ожидание Wait прерывается отменой"); }
    try { await waitStep.ExecuteAsync(waitCancellation.Token); throw new Exception("Предварительная отмена Wait потеряна"); }
    catch (OperationCanceledException) { Console.WriteLine("PASS: Wait поддерживает отмену до начала ожидания"); }
}
Check(new ScenarioStepDefinition().QueryInputMode == "typing", "старые сценарии сохраняют клавиатурный ввод");
var invalidInputMode = new ExecuteToolkitQueryStep(new ScenarioStepDefinition
    { QueryFile = definition.Steps[3].QueryFile, QueryInputMode = "invalid" },
    null!, null!, null!, null!, null!);
try { await invalidInputMode.ExecuteAsync(); throw new Exception("Неизвестный способ ввода принят"); }
catch (InvalidOperationException error) when (error.Message.Contains("queryInputMode"))
{ Console.WriteLine("PASS: неизвестный способ ввода отклонён до действий в RDP"); }
Check(File.Exists(Path.Combine(Path.GetDirectoryName(typeof(ScenarioDefinition).Assembly.Location)!,
    definition.Steps[3].QueryFile)), "запрос скопирован в выходной каталог");
var missing = new ExecuteToolkitQueryStep(new ScenarioStepDefinition { QueryFile = "missing-query.txt" },
    null!, null!, null!, null!, null!);
try { await missing.ExecuteAsync(); throw new Exception("Отсутствующий файл принят"); }
catch (FileNotFoundException) { Console.WriteLine("PASS: отсутствующий файл отклонён до обращения к RDP"); }
var emptyFile = Path.GetTempFileName();
try
{
    var step = new ExecuteToolkitQueryStep(new ScenarioStepDefinition { QueryFile = emptyFile },
        null!, null!, null!, null!, null!);
    try { await step.ExecuteAsync(); throw new Exception("Пустой файл принят"); }
    catch (InvalidDataException) { Console.WriteLine("PASS: пустой файл отклонён до обращения к RDP"); }
}
finally { File.Delete(emptyFile); }
var bitmapPath = Path.Combine(Path.GetTempPath(), $"toolkit-check-{Guid.NewGuid():N}.png");
try
{
    using var image = new Bitmap(600, 400);
    using var graphics = Graphics.FromImage(image);
    graphics.Clear(Color.White);
    var region = new Rectangle(20, 20, 560, 360);
    var pointer = new Point(350, 200);
    graphics.FillRectangle(Brushes.DarkGray, 569, 70, 5, 60);
    image.Save(bitmapPath);
    Check(!ToolkitConsoleRecognizer.ScrollbarAtBottom(bitmapPath, region), "ползунок вверху не означает конец");
    var first = ToolkitConsoleRecognizer.Fingerprint(bitmapPath, region, pointer);
    graphics.FillRectangle(Brushes.Black, pointer.X, pointer.Y, 10, 10);
    image.Save(bitmapPath);
    Check(first == ToolkitConsoleRecognizer.Fingerprint(bitmapPath, region, pointer), "курсор исключён из сравнения результата");
    graphics.FillRectangle(Brushes.White, 569, 70, 5, 60);
    graphics.FillRectangle(Brushes.DarkGray, 569, 300, 5, 65);
    image.Save(bitmapPath);
    Check(ToolkitConsoleRecognizer.ScrollbarAtBottom(bitmapPath, region), "нижнее положение ползунка распознаётся");
}
finally { File.Delete(bitmapPath); }
foreach (var screenshot in args.Where(arg => arg != "--expect-query-error"))
{
    var actual = await new ToolkitConsoleRecognizer(new OneCSectionRecognizer()).RecognizeAsync(screenshot, CancellationToken.None);
    Check(actual.Editor != null && actual.Execute != null,
        $"реальный снимок: редактор {actual.Editor}, Выполнить {actual.Execute}");
    if (args.Contains("--expect-query-error"))
        Check(actual.Error != null, $"реальный снимок: ошибка запроса распознана: {actual.Error}");
    if (actual.RowCount > 0)
        Check(actual.Result != null, $"реальный снимок: видимая область результата {actual.Result}");
}
await TelegramChecks.RunAsync(Check);
Console.WriteLine("Все проверки пройдены.");

sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _timestamp;
    public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
}
