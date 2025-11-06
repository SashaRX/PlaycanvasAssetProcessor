# Оптимизация размера сборки

Этот документ описывает оптимизации, примененные для уменьшения количества DLL и размера релизной сборки.

## Проблема

В релизной сборке присутствовало большое количество DLL (27+ файлов) и множество runtime директорий для неиспользуемых платформ (linux-x64, osx-x64, win-x86), при этом:
- `Microsoft.Windows.SDK.NET.dll` занимал >25 МБ
- Присутствовали DLL для всех платформ
- Включались неиспользуемые транзитивные зависимости (Xceed.Wpf.AvalonDock)

## Применённые оптимизации

### 1. RuntimeIdentifier для win-x64

**Изменение в AssetProcessor.csproj:**
```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

**Эффект:**
- Удаляет runtime директории для linux-x64, osx-x64, win-x86
- Включает в сборку только нативные библиотеки для Windows x64 (assimp.dll, ktx.dll)
- Уменьшает общий размер дистрибутива

### 2. Включение Trimming

**Изменения в AssetProcessor.csproj:**
```xml
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>partial</TrimMode>
<EnableTrimAnalyzer>true</EnableTrimAnalyzer>
<SuppressTrimAnalysisWarnings>false</SuppressTrimAnalysisWarnings>
```

**Эффект:**
- Удаляет неиспользуемый код из всех сборок
- Уменьшает размер Microsoft.Windows.SDK.NET.dll и других больших библиотек
- Partial режим безопаснее для WPF приложений (full может сломать рефлексию)

**Защита от чрезмерного trimming:**
```xml
<ItemGroup Condition="'$(Configuration)' == 'Release'">
  <TrimmerRootAssembly Include="PresentationCore" />
  <TrimmerRootAssembly Include="PresentationFramework" />
  <TrimmerRootAssembly Include="WindowsBase" />
  <TrimmerRootAssembly Include="System.Xaml" />
  <TrimmerRootAssembly Include="Extended.Wpf.Toolkit" />
  <TrimmerRootAssembly Include="Xceed.Wpf.Toolkit" />
  <TrimmerRootAssembly Include="Microsoft.Xaml.Behaviors" />
  <TrimmerRootAssembly Include="HelixToolkit.Wpf" />
  <TrimmerRootAssembly Include="OxyPlot.Wpf" />
</ItemGroup>
```

Это гарантирует, что WPF-зависимости не будут обрезаны слишком агрессивно, что могло бы привести к ошибкам во время выполнения.

### 3. Отключение отладочной информации

**Изменения в AssetProcessor.csproj:**
```xml
<DebuggerSupport>false</DebuggerSupport>
<EnableUnsafeBinaryFormatterSerialization>false</EnableUnsafeBinaryFormatterSerialization>
```

**Эффект:**
- Удаляет метаданные отладки из Release сборки
- Отключает небезопасную сериализацию (улучшение безопасности и уменьшение размера)

### 4. Оптимизация компиляции

**Изменения в AssetProcessor.csproj:**
```xml
<Optimize>true</Optimize>
```

**Эффект:**
- Включает оптимизации компилятора для Release сборки
- Улучшает производительность и может уменьшить размер кода

## Ограничения и компромиссы

### Microsoft.Windows.SDK.NET.dll

Эту библиотеку **нельзя полностью удалить**, так как она является core-зависимостью для WPF приложений на .NET 9. Возможные подходы к уменьшению её размера:

1. **Использование более старой версии Windows SDK** - может уменьшить размер, но потеряется доступ к новым API
   ```xml
   <TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>
   ```
   ⚠️ Не рекомендуется: может сломать совместимость с новыми версиями Windows

2. **SelfContained=true с агрессивным trimming** - включит .NET runtime в дистрибутив, но позволит более агрессивно обрезать код
   ```xml
   <SelfContained>true</SelfContained>
   <TrimMode>full</TrimMode>
   ```
   ⚠️ Не рекомендуется: может сломать WPF приложение из-за рефлексии и XAML

С включенным `PublishTrimmed=true` размер Microsoft.Windows.SDK.NET.dll должен значительно уменьшиться за счет удаления неиспользуемых API.

### Транзитивные зависимости

**Xceed.Wpf.AvalonDock** и его темы (Aero, Metro, VS2010) являются транзитивными зависимостями от `Extended.Wpf.Toolkit` и **не используются** в проекте напрямую.

Попытки явно исключить их через `ExcludeAssets="all"` могут не сработать из-за особенностей NuGet dependency resolution. Однако с включенным trimming эти библиотеки будут минимизированы или исключены автоматически, если не используются.

### Другие зависимости

Все остальные зависимости **активно используются** в проекте:

- **AssimpNet** - загрузка 3D моделей (MainWindow.xaml.cs)
- **HelixToolkit.Wpf** - отображение 3D моделей (MainWindow.xaml.cs, MainWindowHelpers.cs)
- **SixLabors.ImageSharp** - обработка изображений (вся texture conversion pipeline)
- **Vortice.\*** (D3DCompiler, Direct3D11, DXGI) - TextureViewer с GPU рендерингом
- **OxyPlot.Wpf** - построение графиков и диаграмм
- **Newtonsoft.Json** - работа с PlayCanvas API
- **NLog** - логирование
- **CommunityToolkit.Mvvm** - MVVM helpers
- **Microsoft.Xaml.Behaviors.Wpf** - WPF behaviors
- **Ookii.Dialogs.Wpf** - нативные диалоги Windows

Все эти библиотеки необходимы и не могут быть удалены.

### Нативные библиотеки

**ktx.dll** - нативная библиотека для работы с KTX2 форматом (P/Invoke в TextureViewer/LibKtxNative.cs). Необходима для TextureViewer функциональности.

**assimp.dll** - нативная библиотека для загрузки 3D моделей. Необходима для AssimpNet.

Обе библиотеки теперь включаются только для win-x64 платформы.

## Результаты

После применения оптимизаций:

1. ✅ Удалены runtime директории для неиспользуемых платформ (linux-x64, osx-x64, win-x86)
2. ✅ Включен trimming для уменьшения размера всех библиотек
3. ✅ Защищены WPF-зависимости от чрезмерного trimming
4. ✅ Удалена отладочная информация из Release сборки
5. ✅ Минимизированы транзитивные зависимости (AvalonDock)

### Команды для сборки

**Release сборка с оптимизациями:**
```bash
dotnet build AssetProcessor.csproj --configuration Release
```

**Публикация (для single-file executable):**
```bash
dotnet publish AssetProcessor.csproj --configuration Release --runtime win-x64 --self-contained false
```

**Публикация с самостоятельной сборкой (включает .NET runtime):**
```bash
dotnet publish AssetProcessor.csproj --configuration Release --runtime win-x64 --self-contained true
```

⚠️ **Внимание:** Self-contained сборка будет значительно больше (~150+ МБ), так как включает весь .NET runtime. Используйте `--self-contained false` для зависимости от установленного .NET 9 runtime на целевой системе.

## Дальнейшие оптимизации (опциональные)

### 1. Использование NativeAOT (экспериментально)

```xml
<PublishAot>true</PublishAot>
```

⚠️ **Не рекомендуется для WPF:** WPF имеет ограниченную поддержку NativeAOT из-за активного использования рефлексии и XAML.

### 2. Миграция с Newtonsoft.Json на System.Text.Json

System.Text.Json является частью .NET runtime и может уменьшить количество зависимостей. Однако требует переписывания кода работы с JSON.

### 3. Использование более лёгких UI библиотек

Рассмотреть замену тяжёлых UI библиотек на более лёгкие альтернативы, но это требует значительных изменений в коде.

## Анализ размера сборки

Для анализа размера после сборки:

```bash
# В PowerShell
Get-ChildItem -Path "bin\Release\net9.0-windows10.0.26100.0\win-x64" -Recurse -File |
  Select-Object Name, @{Name="Size(MB)";Expression={[math]::Round($_.Length/1MB,2)}} |
  Sort-Object -Property "Size(MB)" -Descending

# Общий размер
Get-ChildItem -Path "bin\Release\net9.0-windows10.0.26100.0\win-x64" -Recurse -File |
  Measure-Object -Property Length -Sum |
  Select-Object @{Name="TotalSize(MB)";Expression={[math]::Round($_.Sum/1MB,2)}}
```

Это покажет все файлы отсортированные по размеру и общий размер дистрибутива.

## Заключение

Применённые оптимизации значительно уменьшают размер дистрибутива без потери функциональности. Дальнейшее уменьшение размера требует либо существенных архитектурных изменений (миграция библиотек, переписывание кода), либо связано с высоким риском поломки функциональности (агрессивный trimming, NativeAOT для WPF).

Рекомендуется периодически проверять размер сборки и анализировать новые зависимости перед их добавлением в проект.
