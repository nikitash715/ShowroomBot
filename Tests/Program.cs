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
await ReadCodeChecks.RunAsync(Check);
await OpenConfigChecks.RunAsync(Check);
var imageArgument = Array.IndexOf(args, "--configurator-image");
if (imageArgument >= 0)
{
    var imagePath = Path.GetFullPath(args[imageArgument + 1]);
    var imageLabels = await new OneCSectionRecognizer().ReadConfiguratorLinesAsync(imagePath, CancellationToken.None);
    using var captured = new Bitmap(imagePath);
    var configurator = OneCConfiguratorRecognizer.Analyze(captured, imageLabels);
    Console.WriteLine($"Configurator: {configurator.IsConfigurator}; tree: {configurator.Tree}; tab: {OneCConfiguratorRecognizer.FindMainConfigurationTab(imageLabels, captured.Size)?.Bounds}");
    if (configurator.Tree == null)
        foreach (var label in imageLabels.Where(l => l.Bounds.Left < captured.Width / 6))
            Console.WriteLine($"OCR: {label.Text} @ {label.Bounds}");
    if (args.Contains("--expect-scrolled-tree"))
    {
        var dockTab = OneCConfiguratorRecognizer.FindMainConfigurationTab(imageLabels, captured.Size);
        Check(configurator.IsConfigurator && configurator.Tree is { } tree && dockTab != null &&
            tree.Bottom < dockTab.Bounds.Top && tree.Bottom < captured.Height * .8 &&
            OneCConfiguratorRecognizer.TreeRows(imageLabels, tree).Count > 0,
            "ReadCode: прокрученное дерево с панелью служебных сообщений распознано и доступно для Home");
        Console.WriteLine($"Tree: {configurator.Tree}; metadata tab: {dockTab?.Bounds}");
    }
    var expectedModuleArgument = Array.IndexOf(args, "--expect-module");
    if (expectedModuleArgument >= 0)
    {
        Console.WriteLine($"Editor: {configurator.Editor}; module: {configurator.ModuleName}");
        Check(configurator.Editor != null && OneCConfiguratorRecognizer.MatchesModuleName(
            configurator.ModuleName, args[expectedModuleArgument + 1]),
            "ReadCode: на снимке сбоя подтверждены редактор и имя открытого модуля");
        Check(OneCConfiguratorRecognizer.FindFoldPluses(captured, configurator.Editor!.Value).Any(),
            "ReadCode: на снимке сбоя значки раскрытия попадают в границы редактора");
    }
    if (args.Contains("--expect-missing-source"))
    {
        Console.WriteLine($"Tree: {configurator.Tree}; editor: {configurator.Editor}; module: {configurator.ModuleName}");
        Check(configurator.Editor != null &&
            OneCConfiguratorRecognizer.MatchesModuleName(configurator.ModuleName, "CRM_MSExchangeСервер") &&
            OneCConfiguratorRecognizer.HasMissingSource(configurator),
            "ReadCode: реальное окно отсутствующего исходного текста подтверждено и пропускается");
    }
    Check(configurator.IsConfigurator && configurator.Tree != null &&
        OneCConfiguratorRecognizer.FindMainConfigurationTab(imageLabels, captured.Size) != null,
        "ReadCode: реальный снимок RDP — конфигуратор, открытое дерево и основная вкладка распознаны");
    if (args.Contains("--expect-common-modules-plus"))
    {
        var node = OneCConfiguratorRecognizer.FindTreeNode(imageLabels, configurator.Tree!.Value, "Общие модули");
        Check(node != null, "ReadCode: на снимке ошибки найдена строка «Общие модули»");
        var toggle = OneCConfiguratorRecognizer.FindTreeToggle(captured, configurator.Tree.Value, node!);
        Check(toggle is { IsExpanded: false } && toggle.Center.X < node!.Bounds.Left,
            $"ReadCode: на снимке ошибки найден + слева от «Общие модули»: {toggle?.Center}");
    }
    if (args.Contains("--common-modules-image"))
    {
        imageLabels = await new OneCSectionRecognizer().ReadTreeLinesAsync(imagePath, configurator.Tree!.Value, CancellationToken.None);
        var modules = OneCConfiguratorRecognizer.FindTreeNode(imageLabels, configurator.Tree!.Value, "Общие модули");
        Check(modules != null, "ReadCode: реальный снимок — видимый узел общих модулей найден без прокрутки");
        var candidates = OneCConfiguratorRecognizer.CommonModuleRows(imageLabels, configurator.Tree.Value,
            modules, modules!.Bounds.Left);
        Check(candidates.Length > 0, "ReadCode: реальный снимок — распознаны дочерние модули для открытия");
        Console.WriteLine($"Модули: {string.Join(", ", candidates.Select(c => c.Text))}");
    }
}
if (args.Contains("--readcode-only"))
{
    Console.WriteLine("Все проверки ReadCode и RDP пройдены.");
    return;
}
await VpnChecks.RunAsync(Check);
await CheckMailChecks.RunAsync(Check);
var linkTitle = new RecognizedText("Переход по ссылке", new Rectangle(10, 10, 200, 20));
var linkGo = new RecognizedText("Перейти", new Rectangle(90, 100, 60, 20));
Check(OpenOneCCommandStep.HasLinkDialog([linkTitle, linkGo]), "Link: recognize blocking dialog before command");
Check(!OpenOneCCommandStep.HasLinkDialog([linkGo]), "Link: ordinary form is not a link dialog");
var saveQuestion = new RecognizedText("Сохранить изменения?", new Rectangle(10, 40, 200, 20));
var discard = new RecognizedText("Не сохранять", new Rectangle(100, 100, 100, 20));
var no = new RecognizedText("Нет", new Rectangle(210, 100, 40, 20));
var ok = new RecognizedText("ОК", new Rectangle(260, 100, 40, 20));
Check(OpenOneCCommandStep.FindDismissButton([saveQuestion, discard, no, ok, discard]) == discard.Bounds,
    "Dismiss: prefer discard over No and OK; deduplicate OCR");
Check(OpenOneCCommandStep.FindDismissButton([saveQuestion, no, ok]) == no.Bounds,
    "Dismiss: No rejects saving");
Check(OpenOneCCommandStep.FindDismissButton([saveQuestion, ok]) is null,
    "Dismiss: never accept saving with OK");
Check(OpenOneCCommandStep.FindDismissButton([ok]) == ok.Bounds, "Dismiss: acknowledge ordinary dialog");
Check(OpenOneCCommandStep.FindDismissButton([ok, ok with { Bounds = new Rectangle(320, 100, 40, 20) }]) is null,
    "Dismiss: ambiguous OK blocks click");
Check(OpenOneCCommandStep.HasLinkDialog([linkTitle with { Text = "  ПЕРЕХОД ПО ССЫЛКЕ  " }]),
    "Link: dialog detection ignores case and surrounding whitespace");
Check(OpenOneCCommandStep.FindLinkGoButton([linkTitle, linkGo, linkGo]) == linkGo.Bounds,
    "Link: find Go beneath dialog title and deduplicate OCR line/word");
Check(OpenOneCCommandStep.FindLinkGoButton([linkGo]) is null, "Link: no click without dialog title");
Check(OpenOneCCommandStep.FindLinkGoButton([linkTitle, linkGo, linkGo with { Bounds = new Rectangle(180, 100, 60, 20) }]) is null,
    "Link: ambiguous Go buttons block click");
var diagnosticsRoot = Path.Combine(Path.GetTempPath(), $"showroombot-cleanup-{Guid.NewGuid():N}");
try
{
    var nowUtc = DateTime.UtcNow;
    var cutoff = nowUtc.AddDays(-1);
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
    Check(File.Exists(boundaryFile) && File.Exists(recentFile), "diagnostics: retain last 24 hours including boundary");
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
var workStart = new TimeOnly(9, 0);
var workEnd = new TimeOnly(18, 0);
Check(!IdleAutoStartTimer.IsWithinWindow(new TimeOnly(8, 59), workStart, workEnd), "autostart: before opening");
Check(IdleAutoStartTimer.IsWithinWindow(workStart, workStart, workEnd), "autostart: opening included");
Check(IdleAutoStartTimer.IsWithinWindow(new TimeOnly(17, 59), workStart, workEnd), "autostart: within window");
Check(!IdleAutoStartTimer.IsWithinWindow(workEnd, workStart, workEnd), "autostart: closing excluded");
Check(!IdleAutoStartTimer.IsWithinWindow(new TimeOnly(23, 0), workStart, workEnd), "autostart: after closing");
Check(IdleAutoStartTimer.IsWithinWindow(new TimeOnly(0, 0), new TimeOnly(22, 0), new TimeOnly(6, 0)) &&
    !IdleAutoStartTimer.IsWithinWindow(new TimeOnly(12, 0), new TimeOnly(22, 0), new TimeOnly(6, 0)), "autostart: overnight window");
Check(!IdleAutoStartTimer.IsWithinWindow(workStart, workStart, workStart), "autostart: equal boundaries disable window");
var windowTimer = new IdleAutoStartTimer(idleClock);
Check(!windowTimer.ShouldStart(true, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), false, true,
    workStart, workEnd), "autostart: clock blocks idle start before opening");
idleClock.Advance(TimeSpan.FromHours(9));
Check(windowTimer.ShouldStart(true, TimeSpan.FromHours(1), TimeSpan.FromMinutes(1), false, true,
    workStart, workEnd), "autostart: clock permits idle start at opening");
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
    Check(migrated.AutoStartStartTime == "09:00" && migrated.AutoStartEndTime == "18:00", "autostart: old config defaults");
    migrated.AutoStartStartTime = "22:30";
    migrated.AutoStartEndTime = "06:15";
    Check(!migrated.Telegram.Enabled && migrated.Telegram.AllowedUserId == 0 &&
        migrated.Telegram.BotToken == "", "Telegram: старый конфиг оставляет интеграцию выключенной");
    service.Save(migrated);
    Check(service.Load().AutoStartStartTime == "22:30" && service.Load().AutoStartEndTime == "06:15", "autostart: working hours persist");
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
Check(definition.Steps.Count == 8 && definition.Steps[0].Type == "Open1C" &&
    definition.Steps[5].Type == "CheckMail" && definition.Steps[5].ExecutionContext == ScenarioExecutionContext.Local &&
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
foreach (var speed in Enumerable.Range(1, 10))
{
    var scrolling = scenarioDeserializer.Deserialize<ScenarioStepDefinition>($"scrollSpeed: {speed}\n");
    Check(scrolling.ScrollSpeed == speed && scrolling.ScrollNotches is >= 1 and <= 10 &&
        scrolling.ScrollPauseMs is >= 100 and <= 1500 && scrolling.ScrollUnchangedAttempts == 4 &&
        scrolling.MaxScrollAttempts == 300 && scrolling.ScrollTimeoutSeconds == 660,
        $"скорость {speed}: темп меняется независимо от защитных лимитов");
    if (speed > 1)
    {
        var previous = new ScenarioStepDefinition { ScrollSpeed = speed - 1 };
        Check(scrolling.ScrollNotches >= previous.ScrollNotches && scrolling.ScrollPauseMs < previous.ScrollPauseMs,
            $"скорость {speed} быстрее предыдущей");
    }
}
var ignored = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties().Build().Deserialize<ScenarioStepDefinition>(
        "scrollSpeed: 10\nscrollNotches: 3\nscrollPauseMs: 900\nscrollTimeoutSeconds: 1\nmaxScrollAttempts: 1\nscrollUnchangedAttempts: 2\n");
Check(ignored.ScrollNotches == 10 && ignored.ScrollPauseMs == 100 && ignored.MaxScrollAttempts == 1 &&
    ignored.ScrollTimeoutSeconds == 1 && ignored.ScrollUnchangedAttempts == 2,
    "YAML настраивает защитные лимиты независимо от скорости; размер колеса и пауза определяются скоростью");
foreach (var speed in new[] { 1, 10 })
{
    var configured = scenarioDeserializer.Deserialize<ScenarioStepDefinition>(
        $"maxScrollAttempts: 120\nscrollTimeoutSeconds: 900\nscrollUnchangedAttempts: 6\nscrollSpeed: {speed}\n");
    Check(configured.MaxScrollAttempts == 120 && configured.ScrollTimeoutSeconds == 900 && configured.ScrollUnchangedAttempts == 6,
        "явные лимиты YAML сохраняются для медленной и быстрой прокрутки");
}
foreach (var speed in new[] { 0, 11 })
{
    var invalid = new ExecuteToolkitQueryStep(new ScenarioStepDefinition
        { QueryFile = definition.Steps[3].QueryFile, ScrollSpeed = speed }, null!, null!, null!, null!, null!);
    try { await invalid.ExecuteAsync(); throw new Exception("Недопустимая скорость принята"); }
    catch (InvalidOperationException error) when (error.Message.Contains("scrollSpeed"))
    { Console.WriteLine("PASS: скорость вне диапазона отклонена до действий в RDP"); }
}
var minimalStep = scenarioDeserializer.Deserialize<ScenarioStepDefinition>("type: Open1C\n");
Check(minimalStep.AfterActivationDelayMs == 1000 && minimalStep.AfterRunDialogDelayMs == 500 &&
    minimalStep.WindowSwitchDelayMs == 700 && minimalStep.PollIntervalMs == 30000 && minimalStep.ScrollPauseMs == 700 &&
    minimalStep.ReadyTimeoutSeconds == 420 && minimalStep.SectionOpenTimeoutSeconds == 5 && minimalStep.CommandTimeoutSeconds == 15 &&
    minimalStep.ConsoleTimeoutSeconds == 30 && minimalStep.QueryInputTimeoutSeconds == 20 &&
    minimalStep.QueryTimeoutSeconds == 120 && minimalStep.ScrollTimeoutSeconds == 660,
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
var minimalConfigStep = scenarioDeserializer.Deserialize<ScenarioStepDefinition>("type: OpenConfig\n");
Check(minimalConfigStep.WindowSwitchDelayMs == 700 && minimalConfigStep.AfterActivationDelayMs == 1000 &&
    minimalConfigStep.AfterRunDialogDelayMs == 500 && minimalConfigStep.ReadyTimeoutSeconds == 420 &&
    minimalConfigStep.PollIntervalMs == 500,
    "OpenConfig без параметров времени использует значения по умолчанию");
var overriddenStep = scenarioDeserializer.Deserialize<ScenarioStepDefinition>(
    "type: Open1C\nafterActivationDelayMs: 0\nafterRunDialogDelayMs: 123\npollIntervalMs: 250\n");
Check(overriddenStep.AfterActivationDelayMs == 0 && overriddenStep.AfterRunDialogDelayMs == 123 &&
    overriddenStep.PollIntervalMs == 250,
    "явные параметры времени переопределяют значения по умолчанию, включая нулевую паузу");
Check(definition.Steps.Select(step => step.Type).SequenceEqual(
    new[] { "Open1C", "Open1CSection", "Open1CCommand", "ExecuteToolkitQuery", "Wait", "CheckMail", "OpenConfig", "ReadCode" }), "справочник содержит все типы шагов");
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
foreach (var input in args.Where(arg => arg.StartsWith("--section-screenshot=")))
{
    var source = input["--section-screenshot=".Length..];
    var scratch = Path.Combine(Path.GetTempPath(), "section-regression-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratch);
    try
    {
        var screenshot = Path.Combine(scratch, "section.png");
        File.Copy(source, screenshot);
        var recognizer = new OneCSectionRecognizer();
        var shortName = await recognizer.RecognizeAsync(screenshot, "Закупки", CancellationToken.None);
        var fullName = await recognizer.RecognizeAsync(screenshot, "Корпоративные закупки", CancellationToken.None);
        Check(shortName.TextBounds is null && fullName.TextBounds is not null,
            "Section: actual failure screenshot rejects substring and recognizes complete label");
    }
    finally { Directory.Delete(scratch, true); }
}
foreach (var input in args.Where(arg => arg.StartsWith("--command-screenshot=")))
{
    var scratch = Path.Combine(Path.GetTempPath(), "command-regression-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratch);
    try
    {
        var screenshot = Path.Combine(scratch, "command.png");
        File.Copy(input["--command-screenshot=".Length..], screenshot);
        var recognizer = new OneCSectionRecognizer();
        var actual = await recognizer.RecognizeCommandAsync(screenshot, "Сформировать заказы поставщикам", CancellationToken.None);
        Check(actual.Command is Rectangle bounds && actual.Workspace.Contains(bounds) &&
            bounds.Left >= 270 && bounds.Right <= 510 && bounds.Top >= 280 && bounds.Bottom <= 310,
            $"Command: actual failure screenshot recognizes link at original coordinates: {actual.Command}");
        var absent = await recognizer.RecognizeCommandAsync(screenshot, "Сформировать заказы покупателям", CancellationToken.None);
        Check(absent.Command is null, "Command: different command is not accepted");
    }
    finally { Directory.Delete(scratch, true); }
}
foreach (var screenshot in args.Where(arg => arg != "--expect-query-error" && !arg.StartsWith("--section-screenshot=") && !arg.StartsWith("--command-screenshot=")))
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
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(_timestamp);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public void Advance(TimeSpan elapsed) => _timestamp += elapsed.Ticks;
}
