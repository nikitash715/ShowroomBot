# ShowroomBot

Windows-приложение для выполнения YAML-сценариев в 1С через RDP.
Для сборки нужен .NET 10 SDK. Для распознавания интерфейса нужны
русский или английский компоненты OCR Windows.

## Локальная настройка

В PowerShell из каталога проекта:

```powershell
Copy-Item configexample.yaml config.yaml
New-Item -ItemType Directory -Force Scenarios
Copy-Item Examples/Scenario1.example.yaml Scenarios/Scenario1.yaml
```

Заполните `config.yaml` своими параметрами VPN/RDP, а
`Scenarios/Scenario1.yaml` — параметрами базы и учётными данными 1С.
Если рабочие файлы уже существуют, сохраните свои настройки перед копированием.

```powershell
dotnet build
dotnet run
```

Аварийная остановка сценария: **Ctrl+Alt+F12**.
Логи, screenshots и результаты OCR сохраняются в `diagnostics` рядом с исполняемым файлом.

## Публикация исходников

В Git публикуются `configexample.yaml` и `Examples/Scenario1.example.yaml`
с вымышленными значениями. Рабочие `config.yaml`, `Scenarios/*.yaml`,
диагностика и результаты сборки исключены через `.gitignore`.
Не добавляйте локальные настройки принудительно через `git add -f`.

Исключение файла из Git не удаляет его из прежних коммитов. Если в истории
уже были реальные данные, для новой публичной публикации используйте очищенный
снимок исходников без старой папки `.git` и создайте новую историю репозитория.
