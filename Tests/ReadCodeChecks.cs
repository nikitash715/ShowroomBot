using System.Drawing;
using System.Reflection;
using ShowroomBot.Core.Scenarios;
using ShowroomBot.Rdp;
using ShowroomBot.Windows;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

internal static class ReadCodeChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        check(OneCConfiguratorRecognizer.ParseModuleTitle("(Жць• модујъ CRM_MSExchangeCepep: Модујъ") ==
                "CRM_MSExchangeCepep" &&
            OneCConfiguratorRecognizer.ParseModuleTitle("Общий модуль CRM_MSExchangeСервер: Модуль") ==
                "CRM_MSExchangeСервер",
            "ReadCode: реальная ошибка OCR заголовка не блокирует открытие модуля");
        check(OneCConfiguratorRecognizer.ParseModuleTitle("Общий модуль CRM_MSExchangeСервер") == null &&
            OneCConfiguratorRecognizer.ParseModuleTitle("Общий модуль CRM_MSExchangeСервер: Свойства") == null &&
            OneCConfiguratorRecognizer.ParseModuleTitle("CRM_MSExchangeСервер: Модуль") == null,
            "ReadCode: неполный заголовок и окно свойств не подтверждают редактор");
        var beforeOpening = new ConfiguratorView(true, new(2, 150, 340, 550), null, null, []);
        var appeared = beforeOpening with { Editor = new Rectangle(375, 100, 1000, 650), Labels = [
            new("Процедура Первая() Экспорт ...", new(410, 200, 350, 14)),
            new("Функция Вторая(Параметр) Экспорт ...", new(410, 250, 400, 14))] };
        check(OneCConfiguratorRecognizer.ConfirmsModuleOpening(beforeOpening, appeared, "CRM_БазаЗнанийСервер") &&
            OneCConfiguratorRecognizer.ConfirmsModuleOpening(beforeOpening,
                appeared with { Labels = [appeared.Labels[0]] }, "CRM_БазаЗнанийСервер") &&
            !OneCConfiguratorRecognizer.ConfirmsModuleOpening(appeared, appeared, "CRM_БазаЗнанийСервер") &&
            !OneCConfiguratorRecognizer.ConfirmsModuleOpening(beforeOpening, appeared with { Labels = [] }, "CRM_БазаЗнанийСервер"),
            "ReadCode: даже одна появившаяся процедура подтверждает открытие; прежний редактор и пустая поверхность не подтверждают");
        check(OneCConfiguratorRecognizer.CanonicalModuleName("сям_БазаЗнанийСервер") == "CRM_БазаЗнанийСервер",
            "ReadCode: ошибочный кириллический префикс OCR исправляется на CRM");
        var missingSource = new ConfiguratorView(true, new(2, 150, 340, 550),
            new(375, 124, 1300, 650), "CRM_MSExchangeСервер",
            [new("Исходный текст модуля отсутствует", new(940, 466, 190, 14))]);
        check(OneCConfiguratorRecognizer.HasMissingSource(missingSource),
            "ReadCode: заглушка отсутствующего исходного текста распознаётся как пустой модуль");
        check(OneCConfiguratorRecognizer.HasMissingSource(missingSource with { Labels = [
            new("Исходный текст", new(940, 466, 85, 14)),
            new("модуля отсутствует", new(1030, 466, 100, 14))] }),
            "ReadCode: заглушка распознаётся и при разделении OCR на слова");
        check(!OneCConfiguratorRecognizer.HasMissingSource(missingSource with { Labels = [
                new("Исходный текст модуля отсутствует", new(5, 466, 190, 14))] }) &&
            !OneCConfiguratorRecognizer.HasMissingSource(missingSource with { Editor = null }) &&
            !OneCConfiguratorRecognizer.HasMissingSource(missingSource with { Labels = [
                new("// Исходный текст модуля отсутствует в другом модуле", new(420, 466, 400, 14))] }),
            "ReadCode: текст вне редактора и строка кода не считаются заглушкой");
        using (var toggleImage = new Bitmap(180, 110))
        using (var toggleGraphics = Graphics.FromImage(toggleImage))
        {
            toggleGraphics.Clear(Color.White);
            var toggleTree = new Rectangle(2, 2, 170, 100);
            var toggleNode = new RecognizedText("Общие модули", new(65, 24, 100, 14));
            check(OneCConfiguratorRecognizer.FindTreeToggle(toggleImage, toggleTree, toggleNode) == null,
                "ReadCode: без значка раскрытия координата клика не угадывается");
            toggleGraphics.DrawEllipse(Pens.Gray, 27, 27, 8, 8);
            toggleGraphics.DrawLine(Pens.Gray, 29, 31, 33, 31);
            toggleGraphics.DrawLine(Pens.Gray, 31, 29, 31, 33);
            toggleGraphics.FillRectangle(Brushes.Gold, 45, 27, 10, 8);
            check(OneCConfiguratorRecognizer.FindTreeToggle(toggleImage, toggleTree, toggleNode) == new TreeToggle(new(31, 31), false),
                "ReadCode: нажимается центр + слева от текста, цветная иконка не выбирается");
            check(OneCConfiguratorRecognizer.FindTreeToggle(toggleImage, toggleTree,
                    toggleNode with { Bounds = new(27, 24, 138, 14) }) == new TreeToggle(new(31, 31), false),
                "ReadCode: + находится даже при включении значка в OCR-границы строки");
            toggleGraphics.DrawRectangle(Pens.Gray, 8, 57, 8, 8);
            toggleGraphics.DrawLine(Pens.Gray, 10, 61, 14, 61);
            check(OneCConfiguratorRecognizer.FindTreeToggle(toggleImage, toggleTree,
                    new("Общие", new(46, 54, 50, 14))) == new TreeToggle(new(12, 61), true),
                "ReadCode: − означает уже раскрытый узел; соседний + не выбирается");
            toggleGraphics.DrawEllipse(Pens.Gray, 15, 27, 8, 8);
            toggleGraphics.DrawLine(Pens.Gray, 17, 31, 21, 31);
            toggleGraphics.DrawLine(Pens.Gray, 19, 29, 19, 33);
            check(OneCConfiguratorRecognizer.FindTreeToggle(toggleImage, toggleTree, toggleNode) == null,
                "ReadCode: неоднозначные значки в строке не нажимаются");
        }
        var focusTree = new Rectangle(2, 138, 361, 841);
        RecognizedText[] focusLabels = [
            new("поиск х", new(3, 144, 350, 14)),
            new("АнализОпроса", new(64, 166, 120, 14)),
            new("АнализПотребностей", new(64, 546, 180, 14))];
        check(OneCConfiguratorRecognizer.FindTreeFocusRow(focusLabels, focusTree)?.Text == "АнализПотребностей" &&
            OneCConfiguratorRecognizer.FindTreeFocusRow([focusLabels[0]], focusTree) == null,
            "ReadCode: перед Home фокусируется строка в середине дерева, поле поиска не выбирается");
        var jumpedModules = OneCConfiguratorRecognizer.CommonModuleRows([
            focusLabels[0],
            new("СтандартныеПодсистемыСервер", new(83, 400, 250, 14)),
            new("СтроковыеФункции", new(83, 420, 170, 14))], focusTree, null, 26);
        check(jumpedModules.Length == 2 && jumpedModules[0].Text == "СтандартныеПодсистемыСервер",
            "ReadCode: после перехода ст поле поиска не завершает список общих модулей");
        var treeBounds = new Rectangle(2, 150, 340, 550);
        RecognizedText[] treeLabels = [
            new("Общие модули", new(50, 190, 110, 14)),
            new("Общие", new(50, 190, 45, 14)),
            new("модули", new(100, 190, 60, 14)),
            new("Общие модули", new(51, 191, 110, 14)),
            new("ДлинноеИмяОбщегоМодуля", new(70, 212, 320, 14)),
            new("Общие", new(400, 160, 55, 14))];
        check(OneCConfiguratorRecognizer.FindTreeNode(treeLabels, treeBounds, "Общие") == null,
            "ReadCode: слово «Общие» внутри строки модулей и вне дерева не является родительским узлом");
        check(OneCConfiguratorRecognizer.FindTreeNode(treeLabels, treeBounds, "Общие модули")?.Bounds.Top == 190 &&
            OneCConfiguratorRecognizer.TreeRows(treeLabels, treeBounds).Count == 2,
            "ReadCode: уже видимый узел находится, дубли OCR не создают ложные строки");
        check(OneCConfiguratorRecognizer.TreeRows(treeLabels, treeBounds)[1].Bounds.Right == treeBounds.Right,
            "ReadCode: длинное имя модуля сохраняется, координата клика ограничена деревом");
        check(OneCConfiguratorRecognizer.FindTreeNode([new("Обшие модупи", new(50, 190, 110, 14))], treeBounds,
                "Общие модули") != null &&
            OneCConfiguratorRecognizer.FindTreeNode([new("Обшие", new(30, 170, 55, 14))], treeBounds, "Общие") != null,
            "ReadCode: небольшие ошибки OCR в категориях допускаются");
        check(OneCConfiguratorRecognizer.FindTreeNode([new("Общие", new(50, 190, 45, 14)),
                new("модули", new(100, 190, 60, 14))], treeBounds, "Общие модули") != null,
            "ReadCode: отдельные OCR-слова одной строки объединяются");
        check(OneCConfiguratorRecognizer.FindTreeNode([new("Общие модули", new(50, 190, 110, 14)),
                new("Общие модули", new(50, 230, 110, 14))], treeBounds, "Общие модули") == null &&
            OneCConfiguratorRecognizer.FindTreeNode([new("Общие формы", new(50, 190, 110, 14))], treeBounds,
                "Общие модули") == null,
            "ReadCode: неоднозначные узлы и соседние категории не выбираются");
        const string code = "// Процедура Ложная()\r\n&НаСервере\r\nПроцедура Настоящая(Параметр) Экспорт\r\n" +
            "  Текст = \"Функция НеФункция()\r\n|КонецПроцедуры\r\n|строка \"\"в кавычках\"\"\"; // комментарий\r\n" +
            "  Вызов();\r\nКонецПроцедуры\r\n\r\nfunction Second()\r\nreturn 1;\r\nendfunction\r\n" +
            "Procedure Broken()\r\nx = 1;";
        var parsed = OneCCode.Parse(code);
        check(parsed.Count == 2 && parsed[0] == new CodeRoutine("Настоящая", 3, 8, 4) &&
            parsed[1] == new CodeRoutine("Second", 10, 12, 1),
            "ReadCode: русские/английские процедуры, строки CRLF, комментарии, экранированные и многострочные литералы");
        check(OneCCode.Parse("Функция А()\nКонецПроцедуры").Count == 0 &&
            OneCCode.Parse("// комментарий\n\n").Count == 0,
            "ReadCode: незаконченные/несогласованные процедуры и пустой модуль не приняты");
        var emptyRoutine = OneCCode.Parse("Процедура А()\n// комментарий\n#Область А\n\n#КонецОбласти\nКонецПроцедуры");
        check(emptyRoutine.Single().CodeLines == 0, "ReadCode: комментарии, пустые строки и директивы не увеличивают объём кода");
        var many = Enumerable.Range(1, 10).Select(n => new CodeRoutine("P" + n, n * 10, n * 10 + 5, 4)).ToArray();
        var sizes = new HashSet<int>();
        var validSelections = true;
        for (var seed = 0; seed < 100; seed++)
        {
            var selected = OneCCode.Select(many, new Random(seed));
            sizes.Add(selected.Count);
            validSelections &= selected.Count is >= 1 and <= 3 && selected.Distinct().Count() == selected.Count;
        }
        check(validSelections, "ReadCode: случайные 1–3 процедуры без повторов (100 seed)");
        check(sizes.SetEquals([1, 2, 3]) && OneCCode.Select(parsed, new Random(0)).SequenceEqual(parsed),
            "ReadCode: возможны все размеры выборки; до трёх процедур читаются все");

        var launch = new ScenarioStepDefinition { Type = "Open1C", Executable = @"C:\Program Files\1c.exe",
            Server = "server", Database = "database", User = "reader", Password = "secret" };
        var enterprise = OpenOneCBaseStep.BuildCommand(launch, "ENTERPRISE");
        var config = OpenOneCBaseStep.BuildCommand(launch, "CONFIG");
        check(config == enterprise.Replace(" ENTERPRISE ", " CONFIG ") && config.Contains("server\\database"),
            "ReadCode: CONFIG использует ровно ту же базу и учётные данные Open1C");
        var options = new ScenarioStepDefinition { Type = "ReadCode", MinimumCodeLines = 1 };
        RecognizedText[] moduleLabels = [new("Общие модули", new(50, 190, 110, 14)),
            new("ОбщегоНазначения", new(70, 212, 170, 14)),
            new("Общие формы", new(50, 235, 110, 14)),
            new("ДругаяФорма", new(70, 257, 130, 14))];
        var moduleRows = OneCConfiguratorRecognizer.CommonModuleRows(moduleLabels, treeBounds, moduleLabels[0], 50);
        check(moduleRows.Length == 1 && moduleRows[0].Text == "ОбщегоНазначения",
            "ReadCode: дочерние модули выбираются до следующей категории, формы не открываются");
        var splitRows = OneCConfiguratorRecognizer.CommonModuleRows([
            moduleLabels[0],
            new("CRM_MSExchangeCepsep", new(83, 212, 180, 14)),
            new("Т сям_ БазаЗнанийСервер", new(62, 232, 210, 14)),
            new("CRM_ Бизнес ПроцессыСервер", new(83, 252, 250, 14)),
            new("CRM_ ДлинноеИмя…", new(83, 272, 250, 14)),
            new("71", new(62, 292, 12, 14))], treeBounds, moduleLabels[0], 50);
        check(splitRows.Select(r => r.Text).SequenceEqual([
                "CRM_MSExchangeCepsep", "CRM_БазаЗнанийСервер", "CRM_БизнесПроцессыСервер"]) &&
            OneCConfiguratorRecognizer.MatchesModuleName("CRM_БазаЗнанийСервер", splitRows[1].Text),
            "ReadCode: после пустого модуля доступны соседние модули с пробелами и значком в OCR; обрезанные имена исключены");
        check(OneCConfiguratorRecognizer.MatchesModuleName("CRM_MSExchangeСервер", "CRM_MSExchangeCepsep") &&
            !OneCConfiguratorRecognizer.MatchesModuleName("ДругойМодуль", "CRM_MSExchangeCepsep") &&
            !OneCConfiguratorRecognizer.MatchesModuleName("АБВ", "АБГ"),
            "ReadCode: небольшая ошибка OCR длинного имени допустима, другой модуль и короткие имена не принимаются");
        check(OneCConfiguratorRecognizer.MatchesModuleName("СНМ_ВоронкиПроддхСервер", "сям_ВоронкиПродажСервер") &&
            OneCConfiguratorRecognizer.MatchesModuleName("СНМ_ВоронкиПроддхСервер", "CRM_ВоронкиПродажСервер") &&
            !OneCConfiguratorRecognizer.MatchesModuleName("CRM_ВоронкиПродажКлиент", "CRM_ВоронкиПродажСервер"),
            "ReadCode: реальные три ошибки OCR заголовка допускаются; другой модуль не подтверждается");
        check(options.ExecutionContext == ScenarioExecutionContext.Rdp &&
            new ScenarioStepFactory(null!, null!, null!, null!, null!).Create(options) is ReadCodeStep,
            "ReadCode: фабрика и RDP-контекст зарегистрированы");
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var yaml = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()
            .Deserialize<ScenarioDefinition>(File.ReadAllText(Path.Combine(root, "Examples/ScenarioReference.example.yaml")));
        var example = yaml.Steps.Single(s => s.Type == "ReadCode");
        var scenario2 = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()
            .Deserialize<ScenarioDefinition>(File.ReadAllText(Path.Combine(root, "Scenarios/Scenario2.yaml")));
        check(scenario2.Steps[0].Type == "OpenConfig" && scenario2.Steps[1].Type == "ReadCode" &&
            scenario2.Steps[2].Type == "Open1C", "OpenConfig: отдельный шаг перед ReadCode в сценарии 2");
        var localConfig = scenario2.Steps[0];
        var localClient = scenario2.Steps.First(s => s.Type == "Open1C");
        check(localConfig.Executable == localClient.Executable && localConfig.Server == localClient.Server &&
            localConfig.Database == localClient.Database && localConfig.User == localClient.User &&
            localConfig.Password == localClient.Password, "OpenConfig: параметры скопированы из Open1C");
        OpenConfigStep.Validate(localConfig);
        ReadCodeStep.Validate(scenario2.Steps[1]);
        ReadCodeStep.Validate(example);
        check(example.ReadLinePauseMs == 900 && example.MaxModuleAttempts == 8 && example.ReadDurationSeconds == 43 &&
            example.ExecutionContext == ScenarioExecutionContext.Rdp && string.IsNullOrEmpty(example.Database),
            "ReadCode: YAML десериализуется; параметры базы не дублируются");
        var ui = new FakeUi([("Empty", ""), ("Comments", "// только комментарий"), ("Good", code)]);
        await new ReadCodeStep(options, ui).ExecuteAsync();
        check(ui.Attached && ui.Opened.SequenceEqual(["Empty", "Comments", "Good"]) &&
            ui.Read.SequenceEqual([TimeSpan.FromSeconds(43)]), "ReadCode: пустые/короткие модули пропускаются, границы выбранных процедур переданы на чтение");
        var extension = new FakeUi([]) { NextConfiguration = [("Good", code)] };
        await new ReadCodeStep(options, extension).ExecuteAsync();
        check(extension.ConfigurationSwitches == 1 && extension.Read.Count == 1,
            "ReadCode: открытое расширение используется после исчерпания основной конфигурации");
        var preferred = new FakeUi([("Main", code)]) { NextConfiguration = [("Extension", code)] };
        await new ReadCodeStep(options, preferred).ExecuteAsync();
        check(preferred.ConfigurationSwitches == 0 && preferred.Opened.SequenceEqual(["Main"]),
            "ReadCode: при подходящем коде основной конфигурации расширения не выбираются");
        var exhausted = new FakeUi([("Empty", ""), ("Other", code)]);
        await ExpectFailure(new ReadCodeStep(new() { MaxModuleAttempts = 1 }, exhausted), check);
        check(exhausted.Opened.Count == 1 && exhausted.Read.Count == 0, "ReadCode: лимит попыток соблюдается без ложного успеха");
        check(OneCConfiguratorRecognizer.VisibleRoutines(appeared).Count == 2 &&
            OneCConfiguratorRecognizer.VisibleRoutines(appeared with { Editor = null }).Count == 0,
            "ReadCode: visual reading settings and navigation verified");
        var custom = new FakeUi([("Good", code)]);
        await new ReadCodeStep(new() { ReadDurationSeconds = 7, MinimumCodeLines = 0, QueryInputTimeoutSeconds = 0 }, custom).ExecuteAsync();
        check(custom.Read.Single() == TimeSpan.FromSeconds(7), "ReadCode: visual reading settings and navigation verified");
        await ExpectFailure(new ReadCodeStep(new() { ReadDurationSeconds = 0 }, new FakeUi([])), check);
        var invalid = new FakeUi([]);
        await ExpectFailure(new ReadCodeStep(new() { ReadLinePauseMs = 0 }, invalid), check);
        check(!invalid.Attached, "ReadCode: неверные параметры отклонены до ввода");
        using (var cancel = new CancellationTokenSource())
        {
            var cancelledUi = new FakeUi([("Good", code)]) { OnInspect = cancel.Cancel };
            try { await new ReadCodeStep(options, cancelledUi).ExecuteAsync(cancel.Token); throw new Exception("Отмена потеряна"); }
            catch (OperationCanceledException) { check(cancelledUi.Read.Count == 0, "ReadCode: отмена после копирования блокирует чтение"); }
            var notStarted = new FakeUi([]);
            try { await new ReadCodeStep(options, notStarted).ExecuteAsync(cancel.Token); throw new Exception("Отмена потеряна"); }
            catch (OperationCanceledException) { check(!notStarted.Attached, "ReadCode: предварительная отмена не активирует RDP"); }
        }
        var lostFocus = new FakeUi([("Good", code), ("Other", code)]) { InspectFailure = new InvalidOperationException("RDP focus lost") };
        await ExpectFailure(new ReadCodeStep(options, lostFocus), check);
        check(lostFocus.Opened.Count == 1 && lostFocus.Read.Count == 0,
            "ReadCode: ошибка фокуса/буфера прерывает шаг и не маскируется выбором другого модуля");

        using var image = new Bitmap(1280, 800);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(212, 208, 190));
        graphics.FillRectangle(Brushes.White, 2, 155, 346, 580);
        graphics.FillRectangle(Brushes.White, 430, 150, 780, 560);
        RecognizedText[] labels = [
            new("Конфигуратор - 1С:ERP", new(5, 5, 300, 14)),
            new("Конфигурация", new(150, 30, 100, 14)), new("Отладка", new(260, 30, 60, 14)),
            new("Конфигурация", new(2, 100, 100, 14)),
            new("Общий модуль ОбщегоНазначения: Модуль", new(440, 130, 350, 14))];
        var view = OneCConfiguratorRecognizer.Analyze(image, labels);
        var mainTab = new RecognizedText("Конфигурация", new(15, 770, 95, 14));
        var extensionTab = new RecognizedText("Дополнительные функции (РТК)", new(130, 770, 220, 14));
        check(OneCConfiguratorRecognizer.FindMainConfigurationTab([.. labels, extensionTab, mainTab], image.Size) == mainTab,
            "ReadCode: основная вкладка отличается от меню, заголовка панели и расширения");
        check(OneCConfiguratorRecognizer.FindMainConfigurationTab(labels, image.Size) == null,
            "ReadCode: заголовок панели не принимается за нижнюю вкладку");
        using (var docked = new Bitmap(1280, 800))
        using (var dockGraphics = Graphics.FromImage(docked))
        {
            dockGraphics.Clear(Color.FromArgb(212, 208, 190));
            dockGraphics.FillRectangle(Brushes.White, 2, 155, 346, 450);
            dockGraphics.FillRectangle(Brushes.White, 2, 650, 1276, 100);
            RecognizedText[] dockLabels = [
                new("Документ Заказ: ФормаДокумента - Конфигуратор - 1С:ERP", new(5, 5, 600, 14)),
                new("Конфигурация", new(150, 30, 100, 14)), new("Отладка", new(260, 30, 60, 14)),
                new("Основные доработки", new(2, 100, 180, 14)),
                new("Действия", new(4, 124, 50, 10)),
                new("ТипыСкладов", new(65, 180, 100, 14)),
                new("Значения", new(84, 200, 100, 14)),
                new("Конфигурация", new(15, 610, 95, 14)),
                new("Основные доработки", new(130, 610, 180, 14)),
                new("Служебные сообщения", new(2, 630, 180, 14)),
                new("Документ.Заказ.Решение.Тип", new(20, 670, 250, 14))];
            var dockView = OneCConfiguratorRecognizer.Analyze(docked, dockLabels);
            check(dockView.IsConfigurator && dockView.Tree is { Bottom: 607, Right: 346 } &&
                OneCConfiguratorRecognizer.FindConfigurationTabs(dockLabels, docked.Size).Count == 2 &&
                OneCConfiguratorRecognizer.TreeRows(dockLabels, dockView.Tree.Value).Select(r => r.Text)
                    .SequenceEqual(["ТипыСкладов", "Значения"]),
                "ReadCode: открытая форма, прокрученное расширение и служебные сообщения не мешают определить дерево для возврата к корню");
        }
        check(OneCConfiguratorRecognizer.Analyze(image,
                labels.Where(l => l.Bounds.Top != 100).Append(mainTab)
                    .Append(new RecognizedText("Действия", new(4, 124, 50, 10))).ToArray()).Tree != null,
            "ReadCode: исчезнувший в OCR заголовок панели восстанавливается по панели действий и вкладке");
        check(OneCConfiguratorRecognizer.Analyze(image,
                labels.Where(l => l.Bounds.Top != 100).Append(mainTab).ToArray()).Tree == null,
            "ReadCode: нижняя вкладка не принимается за заголовок дерева");
        check(view.IsConfigurator && view.Tree != null && view.Editor is { Left: 430, Top: 150 } &&
            view.ModuleName == "ОбщегоНазначения", "ReadCode: геометрия дерева и редактора определяется относительно заголовков");
        var maximizedLabels = labels.Where(l => l.Bounds.Top != 130).Select(l => l.Bounds.Top == 5
            ? l with { Text = "Общий модуль СтатистикаПерсоналаРасширенныйКлиент: Модуль - Конфигуратор - 1С:ERP" }
            : l).Append(new RecognizedText("Процедура Единственная() Экспорт", new(450, 180, 350, 14))).ToArray();
        var maximized = OneCConfiguratorRecognizer.Analyze(image, maximizedLabels);
        check(maximized.Editor is { Left: 430, Top: 150, Bottom: 709 } &&
            OneCConfiguratorRecognizer.ConfirmsModuleOpening(view, maximized, "СтатистикаПерсоналаРасширенныйКлиент"),
            "ReadCode: развёрнутый модуль с одной процедурой подтверждается по заголовку окна и границам редактора");
        check(OneCConfiguratorRecognizer.Analyze(image, maximizedLabels.Where(l => l.Bounds.Top != 180).ToArray()).Editor != null,
            "ReadCode: пустой развёрнутый модуль распознаётся для последующего пропуска");
        check(OneCConfiguratorRecognizer.Analyze(image, maximizedLabels.Where(l => l.Bounds.Top != 180).Select(l => l.Bounds.Top == 5
                ? l with { Text = "Документ Заказ: Форма - Конфигуратор - 1С:ERP" } : l).ToArray()).Editor == null,
            "ReadCode: белая поверхность без кода и заголовка модуля не подтверждает редактор");
        graphics.FillRectangle(Brushes.Black, 450, 150, 1, 15);
        check(OneCConfiguratorRecognizer.Analyze(image, labels).Editor is { Left: 430 },
            "ReadCode: каретка в первой строке не отрезает левый край редактора и значки раскрытия");
        graphics.FillRectangle(Brushes.White, 450, 150, 1, 15);
        graphics.FillRectangle(Brushes.White, 356, 150, 854, 560);
        var narrowGap = OneCConfiguratorRecognizer.Analyze(image, labels);
        check(narrowGap.Tree is { Right: 346 } && narrowGap.Editor is { Left: 356 },
            "ReadCode: дерево сохраняется при открытом редакторе вплотную к разделителю");
        using (var background = new SolidBrush(Color.FromArgb(212, 208, 190)))
            graphics.FillRectangle(background, 356, 150, 74, 560);
        RecognizedText[] tabs = [.. labels,
            new("Конфигурация", new(10, 770, 90, 16)),
            new("Дополнительные функции (РТК)", new(125, 770, 200, 16)),
            new("функции", new(220, 770, 55, 16))];
        var detectedTabs = OneCConfiguratorRecognizer.FindConfigurationTabs(tabs, image.Size);
        check(detectedTabs.Count == 2 && detectedTabs[0].Text == "Конфигурация" &&
            detectedTabs[1].Text == "Дополнительные функции (РТК)",
            "ReadCode: нижние закладки отделены от меню и OCR-слов; основная конфигурация первая");
        var extensionLabels = tabs.Where(l => !(l.Bounds.Top == 100 && l.Text == "Конфигурация"))
            .Append(new RecognizedText("Дополнительные функции (РТК)", new(2, 100, 200, 14))).ToArray();
        check(OneCConfiguratorRecognizer.Analyze(image, extensionLabels).Tree != null,
            "ReadCode: дерево открытого расширения определяется по его заголовку");
        // A scrolled common-module list has repeated status icons beside the scrollbar.
        graphics.FillRectangle(Brushes.LightGray, 348, 155, 17, 580);
        for (var y = 160; y < 730; y += 19)
            graphics.FillRectangle(Brushes.Gold, 337, y, 8, 8);
        var scrolledTree = OneCConfiguratorRecognizer.Analyze(image, [.. labels, mainTab]);
        check(scrolledTree.Tree is { Left: 2, Right: 346 },
            "ReadCode: открытое прокрученное дерево распознано с полосой прокрутки и значками у правого края");
        for (var y = 155; y < 735; y++)
        {
            graphics.FillRectangle(Brushes.Black, 298, y, 1, 1);
            graphics.FillRectangle(Brushes.Black, 318, y, 1, 1);
        }
        check(OneCConfiguratorRecognizer.Analyze(image, [.. labels, mainTab]).Tree is { Right: 346 },
            "ReadCode: текст на двух прежних контрольных колонках не скрывает дерево");
        for (var y = 160; y < 730; y += 19)
            graphics.FillRectangle(Brushes.Black, 240, y, 84, 8);
        var shiftedCaption = labels.Select(l => l.Bounds.Top == 100
            ? l with { Bounds = new Rectangle(57, 100, 100, 14) } : l).ToArray();
        check(OneCConfiguratorRecognizer.Analyze(image, [.. shiftedCaption, mainTab]).Tree is { Left: 2, Right: 346 },
            "ReadCode: плотные длинные имена и смещённые OCR-границы заголовка не скрывают дерево и корневые узлы");
        check(!OneCConfiguratorRecognizer.Analyze(image, labels.Skip(1).ToArray()).IsConfigurator,
            "ReadCode: упоминаний конфигурации в рабочей области недостаточно для распознавания окна");
        check(OneCConfiguratorRecognizer.IsEmpty(image, view.Editor!.Value), "ReadCode: пустой редактор распознан до копирования");
        graphics.DrawRectangle(Pens.Gray, 412, 180, 8, 8);
        graphics.DrawLine(Pens.Black, 414, 184, 418, 184);
        graphics.DrawLine(Pens.Black, 416, 182, 416, 186);
        graphics.DrawRectangle(Pens.Gray, 412, 210, 8, 8);
        graphics.DrawLine(Pens.Black, 414, 214, 418, 214);
        var pluses = OneCConfiguratorRecognizer.FindFoldPluses(image, view.Editor.Value);
        check(pluses.Count == 1 && pluses[0] == new Point(416, 184), "ReadCode: плюс раскрытия отличается от минуса и находится слева от кода");
        RecognizedText[] dialog = [new("Перейти по номеру строки", new(400, 300, 180, 16)),
            new("Введите номер строки:", new(410, 335, 180, 16)), new("Перейти", new(420, 390, 60, 16)),
            new("Отмена", new(520, 390, 60, 16))];
        check(OneCConfiguratorRecognizer.HasGoToDialog(dialog) &&
            !OneCConfiguratorRecognizer.HasGoToDialog(dialog.Skip(1).ToArray()) &&
            !OneCConfiguratorRecognizer.HasGoToDialog(dialog.Take(1).ToArray()),
            "ReadCode: номер строки вводится только при наличии заголовка, поля и кнопок диалога");

        var guardType = typeof(RdpController).Assembly.GetType("ShowroomBot.Rdp.RdpInputGuard")!;
        using (var guard = (IDisposable)guardType.GetMethod("RequireStrict")!.Invoke(null, [IntPtr.Zero])!)
        {
            await Task.Delay(1);
            foreach (var action in new Action[] { new KeyboardInputSender().SendRemoteStart,
                new KeyboardInputSender().SelectCurrentLine, new MouseInputSender().ClickLeft })
            {
                try { action(); throw new Exception("Ввод вышел за RDP"); }
                catch (InvalidOperationException error) when (error.Message.Contains("RDP-сеанс"))
                { check(true, "ReadCode: строгая защита блокирует клавиши и мышь при отсутствии активного RDP"); }
            }
            try
            {
                guardType.GetMethod("CheckRelease")!.Invoke(null, null);
                throw new Exception("Key-up не защищён");
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
            { check(true, "ReadCode: потеря RDP блокирует даже завершающие key-up пакеты"); }
        }
    }

    private static async Task ExpectFailure(ReadCodeStep step, Action<bool, string> check)
    {
        try { await step.ExecuteAsync(); }
        catch (InvalidOperationException) { check(true, "ReadCode: небезопасный/невыполнимый шаг завершён ошибкой"); return; }
        throw new Exception("Ожидаемая ошибка ReadCode не получена");
    }

    private sealed class FakeUi(IEnumerable<(string Name, string Text)> modules) : IReadCodeUi
    {
        private readonly Queue<(string Name, string Text)> _modules = new(modules);
        private string _text = string.Empty;
        public bool Attached { get; private set; }
        public List<string> Opened { get; } = [];
        public List<TimeSpan> Read { get; } = [];
        public Action? OnInspect { get; init; }
        public Exception? InspectFailure { get; init; }
        public (string Name, string Text)[]? NextConfiguration { get; set; }
        public int ConfigurationSwitches { get; private set; }
        public Task<bool> TryNextConfigurationAsync(CancellationToken token)
        {
            if (NextConfiguration == null) return Task.FromResult(false);
            foreach (var module in NextConfiguration) _modules.Enqueue(module);
            NextConfiguration = null;
            ConfigurationSwitches++;
            return Task.FromResult(true);
        }
        public Task AttachAsync(CancellationToken token) { Attached = true; return Task.CompletedTask; }
        public Task OpenCommonModulesAsync(CancellationToken token) => Task.CompletedTask;
        public Task<string?> OpenRandomModuleAsync(ISet<string> visited, CancellationToken token)
        {
            if (_modules.Count == 0) return Task.FromResult<string?>(null);
            var module = _modules.Dequeue();
            _text = module.Text;
            Opened.Add(module.Name);
            return Task.FromResult<string?>(module.Name);
        }
        public Task<bool> CanReadModuleAsync(CancellationToken token)
        {
            OnInspect?.Invoke();
            return InspectFailure == null ? Task.FromResult(!string.IsNullOrWhiteSpace(_text) && _text.Contains('\n')) : Task.FromException<bool>(InspectFailure);
        }
        public Task ReadModuleAsync(TimeSpan duration, CancellationToken token) { token.ThrowIfCancellationRequested(); Read.Add(duration); return Task.CompletedTask; }
    }
}
