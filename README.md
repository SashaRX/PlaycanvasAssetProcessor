# PlayCanvas Asset Processor

Настольное приложение на WPF для работы с проектами PlayCanvas. Позволяет подключаться к вашему аккаунту, загружать файлы проекта и управлять ресурсами (текстуры, 3D модели, материалы).

## Основные возможности

### Работа с API PlayCanvas
- Подключение к аккаунту через API ключ
- Загрузка списка проектов и веток
- Получение информации о ресурсах проекта
- Скачивание текстур, моделей и материалов

### Визуализация ресурсов
- **Текстуры**: превью изображений, RGB гистограммы, фильтрация по каналам (R, G, B, A)
- **3D Модели**: интерактивный 3D viewport, отображение UV каналов, статистика полигонов
- **Материалы**: просмотр параметров шейдеров, текстурных карт и свойств освещения

### Дополнительные функции
- Проверка целостности файлов через MD5 хэширование
- Конкурентная загрузка файлов (настраиваемое количество потоков)
- Интеграция с Unity Editor для импорта сцен
- Логирование операций (NLog)

---

## Требования

### Системные требования
- **ОС**: Windows 10.0.26100.0 или выше
- **Фреймворк**: .NET 9.0 SDK (preview)
- **Память**: минимум 4 ГБ RAM
- **Дисковое пространство**: 500+ МБ для проектов

### Инструменты разработки
- Visual Studio 2022 Preview с компонентом "Разработка классических приложений .NET"
- Git для контроля версий

---

## Установка и настройка

### 1. Клонирование репозитория

```bash
git clone https://github.com/SashaRX/PlaycanvasAssetProcessor.git
cd PlaycanvasAssetProcessor
```

### 2. Восстановление зависимостей

Откройте решение `TexTool.sln` в Visual Studio 2022 Preview. NuGet пакеты будут восстановлены автоматически.

### 3. Сборка проекта

```bash
# Через Visual Studio: Build > Build Solution (Ctrl+Shift+B)
# Или через командную строку:
dotnet build TexTool.sln --configuration Release
```

### 4. Получение API ключа PlayCanvas

1. Войдите в свой аккаунт на [playcanvas.com](https://playcanvas.com)
2. Перейдите в **Account Settings** → **API Tokens**
3. Нажмите **Generate Token**
4. Скопируйте сгенерированный токен

### 5. Первый запуск

1. Запустите `TexTool.exe` из папки `bin/Release/net9.0-windows10.0.26100.0/`
2. В окне настроек укажите:
   - **Username**: ваше имя пользователя PlayCanvas
   - **API Key**: токен из шага 4
   - **Projects Folder**: локальная папка для сохранения проектов
3. Нажмите **Connect**

---

## Архитектура проекта

### Структура директорий

```
PlaycanvasAssetProcessor/
├── Exceptions/          # Кастомные классы исключений
│   ├── PlayCanvasApiException.cs
│   ├── NetworkException.cs
│   ├── AssetNotFoundException.cs
│   ├── FileIntegrityException.cs
│   └── InvalidConfigurationException.cs
├── Services/            # Сервисный слой
│   ├── IPlayCanvasService.cs
│   └── PlayCanvasService.cs
├── Resources/           # Модели данных ресурсов
│   ├── BaseResource.cs
│   ├── TextureResource.cs
│   ├── ModelResource.cs
│   └── MaterialResource.cs
├── ViewModels/          # MVVM ViewModels
│   └── MainViewModel.cs
├── Helpers/             # Вспомогательные классы
│   ├── Helpers.cs
│   ├── MainWindowHelpers.cs
│   ├── ImageHelper.cs
│   └── Converters.cs
├── Settings/            # Конфигурация приложения
│   ├── AppSettings.cs
│   └── Settings.Designer.cs
└── MainWindow.xaml.cs   # Главное окно приложения
```

### Паттерны проектирования

- **MVVM (Model-View-ViewModel)**: разделение логики и представления
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Repository Pattern**: классы ресурсов для работы с данными
- **Service Layer**: изоляция бизнес-логики работы с API

### Технологический стек

#### Основные фреймворки
- .NET 9.0 с Windows SDK 10.0.26100.0
- WPF (Windows Presentation Foundation)
- C# 12 с nullable reference types

#### NuGet пакеты
- **3D и графика**: AssimpNet 5.0.0-beta1, HelixToolkit.Wpf 2.25.0
- **Обработка изображений**: SixLabors.ImageSharp 3.1.7, System.Drawing.Common 9.0.0
- **UI компоненты**: Extended.Wpf.Toolkit 4.6.1, OxyPlot.Wpf 2.2.0, Ookii.Dialogs.Wpf 5.0.1
- **MVVM и DI**: CommunityToolkit.Mvvm 8.2.2, Microsoft.Extensions.DependencyInjection 8.0.1, Microsoft.Extensions.Hosting 8.0.1
- **Сериализация и логирование**: Newtonsoft.Json 13.0.3, NLog 5.3.4

---

## Использование

### Базовый сценарий работы

1. **Подключение к аккаунту**
   - Введите username и API key
   - Нажмите "Connect"
   - Выберите проект из списка

2. **Загрузка ресурсов**
   - Выберите ветку проекта
   - Нажмите "Load Assets"
   - Дождитесь завершения загрузки списка

3. **Просмотр ресурсов**
   - Перейдите на вкладку "Textures", "Models" или "Materials"
   - Кликните на ресурс для просмотра деталей
   - Используйте инструменты визуализации

4. **Скачивание файлов**
   - Выберите нужные ресурсы
   - Нажмите "Download Selected"
   - Файлы сохранятся в папку проекта

### Конфигурация производительности

В `App.config` доступны настройки конкурентности:

```xml
<!-- Количество параллельных загрузок -->
<setting name="DownloadSemaphoreLimit" value="8"/>

<!-- Лимит для получения информации о текстурах -->
<setting name="GetTexturesSemaphoreLimit" value="64"/>

<!-- Общий лимит семафоров -->
<setting name="SemaphoreLimit" value="32"/>
```

Рекомендуемые значения:
- **Быстрое соединение** (100+ Мбит/с): `DownloadSemaphoreLimit="16"`
- **Медленное соединение** (10-50 Мбит/с): `DownloadSemaphoreLimit="4"`
- **Нестабильное соединение**: `DownloadSemaphoreLimit="2"`

---

## Troubleshooting

### Проблема: "API key is null or empty"

**Решение**:
1. Проверьте, что API ключ введен в настройках
2. Убедитесь, что ключ не содержит пробелов в начале/конце
3. Сгенерируйте новый токен на playcanvas.com

### Проблема: "Failed to get user ID" (401 Unauthorized)

**Причины**:
- Неверный API ключ
- Истек срок действия токена
- Неправильное имя пользователя

**Решение**:
1. Проверьте корректность username (без символа @)
2. Сгенерируйте новый API токен
3. Убедитесь, что токен активен на playcanvas.com

### Проблема: Network timeout при загрузке ресурсов

**Решение**:
1. Проверьте подключение к интернету
2. Уменьшите `DownloadSemaphoreLimit` до 2-4
3. Проверьте firewall/антивирус
4. Попробуйте через другую сеть (VPN/мобильный интернет)

### Проблема: MD5 hash mismatch (FileIntegrityException)

**Причины**:
- Файл поврежден при загрузке
- Нестабильное соединение
- Проблемы на стороне сервера PlayCanvas

**Решение**:
1. Удалите поврежденный файл из папки проекта
2. Повторите загрузку
3. Если проблема повторяется, уменьшите `DownloadSemaphoreLimit`

### Проблема: Ошибки восстановления NuGet пакетов (NU1100, NU1102)

**Симптомы**:
```
error NU1100: Не удалось разрешить "Microsoft.Extensions.DependencyInjection..."
error NU1102: Не удалось найти пакет AssimpNet с версией...
PackageSourceMapping включен, следующие источники не рассматривались
```

**Причина**: Глобальный PackageSourceMapping блокирует доступ к пакетам.

**Решение**:
1. Проект уже содержит локальный `nuget.config` с правильными настройками
2. Если проблема сохраняется, очистите кэш NuGet:
   ```bash
   dotnet nuget locals all --clear
   ```
3. Восстановите пакеты заново:
   ```bash
   dotnet restore
   ```
4. Если используете Visual Studio - перезапустите IDE

**Альтернативное решение** (если проблема не решена):
Отредактируйте глобальный NuGet.config в `%APPDATA%\NuGet\NuGet.Config` и удалите или закомментируйте секцию `<packageSourceMapping>`.

### Проблема: Уязвимости безопасности в пакетах (NU1903, NU1902)

**Симптомы**:
```
warning NU1903: У пакета "SixLabors.ImageSharp" ... есть известная уязвимость
```

**Решение**:
Проект уже обновлен до безопасной версии SixLabors.ImageSharp 3.1.7, которая устраняет уязвимости. Если предупреждение остается:
1. Очистите кэш: `dotnet nuget locals all --clear`
2. Восстановите: `dotnet restore --force`

### Проблема: Приложение не запускается

**Решение**:
1. Убедитесь, что установлен .NET 9.0 SDK
2. Проверьте версию Windows (требуется 10.0.26100.0+)
3. Установите последние обновления Visual C++ Redistributable
4. Проверьте логи в файле `file.txt`

---

## Логирование

Приложение использует NLog для записи событий. Логи сохраняются в файл `file.txt` в папке приложения.

### Уровни логирования
- **Info**: общая информация о работе (подключение, загрузка)
- **Warn**: предупреждения (отсутствие настроек)
- **Error**: ошибки выполнения (сетевые ошибки, проблемы API)

### Настройка логирования

Отредактируйте `NLog.config` для изменения уровня детализации:

```xml
<!-- Показывать только ошибки -->
<logger name="*" minlevel="Error" writeTo="file,console" />

<!-- Показывать все (включая Debug) -->
<logger name="*" minlevel="Debug" writeTo="file,console" />
```

---

## Интеграция с Unity

Проект включает скрипт для импорта сцен PlayCanvas в Unity Editor.

### Использование

1. Скопируйте `PlayCanvasImporterWindow.cs` в папку `Assets/Editor/` вашего Unity проекта
2. В Unity: **Window** → **PlayCanvas Importer**
3. Укажите путь к JSON файлу сцены PlayCanvas
4. Нажмите **Import Scene**

Скрипт автоматически создаст иерархию GameObjects с корректными трансформами.

---

## Разработка

### Запуск в режиме отладки

1. Откройте `TexTool.sln` в Visual Studio 2022
2. Нажмите F5 или Debug → Start Debugging
3. Для отладки без запуска: Ctrl+F5

### Добавление новых зависимостей

```bash
# Через Package Manager Console в Visual Studio:
Install-Package PackageName -Version X.Y.Z

# Или через .NET CLI:
dotnet add package PackageName --version X.Y.Z
```

### Стиль кода

Проект следует правилам `.editorconfig`:
- Отступы: 4 пробела (не табы)
- Окончания строк: CRLF (Windows)
- Кодировка: UTF-8 с BOM
- Nullable reference types: включены
- Naming: PascalCase для типов и методов, camelCase для полей

---

## Roadmap

### Планируемые улучшения

- [ ] Поддержка batch операций (массовая обработка)
- [ ] Автоматическая оптимизация текстур (сжатие, ресайз)
- [ ] Экспорт в Unreal Engine и Godot
- [ ] Кэширование превью для ускорения загрузки
- [ ] Поиск и фильтрация ресурсов по параметрам
- [ ] Система плагинов для кастомных обработчиков
- [ ] CI/CD pipeline через GitHub Actions
- [ ] Unit и integration тесты

---

## Вклад в проект

Мы приветствуем pull requests! Перед отправкой:

1. Создайте issue с описанием изменений
2. Форкните репозиторий
3. Создайте feature branch (`git checkout -b feature/AmazingFeature`)
4. Закоммитьте изменения (`git commit -m 'Add AmazingFeature'`)
5. Запушьте в branch (`git push origin feature/AmazingFeature`)
6. Откройте Pull Request

---

## Лицензия

Этот проект распространяется под лицензией MIT. См. файл `LICENSE` для подробностей.

---

## Контакты и поддержка

- **GitHub Issues**: [Создать issue](https://github.com/SashaRX/PlaycanvasAssetProcessor/issues)
- **Автор**: SashaRX
- **PlayCanvas API документация**: https://developer.playcanvas.com/api/

---

## Благодарности

- **PlayCanvas** за отличный игровой движок и API
- **Microsoft** за .NET и WPF
- **Community Toolkit** за MVVM helpers
- **HelixToolkit** за 3D визуализацию в WPF
- **AssimpNet** за импорт 3D моделей
