# Интеграция ORM UI в MainWindow

## Обзор

Создан полный UI для упаковки ORM текстур. Теперь нужно интегрировать его в MainWindow.

## Компоненты

1. **ORMTextureResource** (`Resources/ORMTextureResource.cs`)
   - Виртуальная текстура для ORM упаковки
   - Хранит настройки каналов, источники, параметры обработки

2. **ORMPackingPanel** (`Controls/ORMPackingPanel.xaml(.cs)`)
   - UserControl с полным UI для настройки ORM
   - Слайдеры, ComboBox, Auto-Detect кнопки
   - Кнопка "Pack & Convert"

## Шаги интеграции в MainWindow

### 1. Добавить кнопку "Create ORM Texture"

В `MainWindow.xaml`, добавьте кнопку в панель управления текстурами:

```xml
<!-- Где-то рядом с другими кнопками управления текстурами -->
<Button x:Name="CreateORMButton"
        Content="Create ORM Texture"
        Click="CreateORMButton_Click"
        ToolTip="Create virtual ORM texture for channel packing"/>
```

### 2. Добавить ORMPackingPanel в MainWindow.xaml

В правую панель (где показываются детали текстуры), добавьте:

```xml
<!-- В правой панели, где показываются свойства выбранной текстуры -->
<controls:ORMPackingPanel x:Name="ORMPanel"
                          Visibility="Collapsed"
                          xmlns:controls="clr-namespace:AssetProcessor.Controls"/>
```

### 3. Реализовать CreateORMButton_Click в MainWindow.xaml.cs

```csharp
private void CreateORMButton_Click(object sender, RoutedEventArgs e) {
    // Создаем виртуальную ORM текстуру
    var ormTexture = new ORMTextureResource {
        Name = $"[ORM Texture {Textures.Count(t => t is ORMTextureResource) + 1}]",
        TextureType = "ORM (Virtual)",
        PackingMode = ChannelPackingMode.OGM,
        // Можно автоматически попробовать детектировать каналы
    };

    // Добавляем в список текстур
    Textures.Add(ormTexture);

    // Выбираем её
    TextureListView.SelectedItem = ormTexture;

    Logger.Info($"Created new ORM texture: {ormTexture.Name}");
}
```

### 4. Обновить логику отображения панели при выборе текстуры

В `TextureListView_SelectionChanged` (или аналогичном обработчике):

```csharp
private void TextureListView_SelectionChanged(object sender, SelectionChangedEventArgs e) {
    var selectedTexture = TextureListView.SelectedItem as TextureResource;

    if (selectedTexture == null) {
        // Скрываем все панели
        ORMPanel.Visibility = Visibility.Collapsed;
        // ... другие панели
        return;
    }

    // Проверяем, это ORM текстура или обычная
    if (selectedTexture is ORMTextureResource ormTexture) {
        // Показываем ORM панель
        ORMPanel.Visibility = Visibility.Visible;
        ORMPanel.Initialize(this, Textures.Where(t => !(t is ORMTextureResource)).ToList());
        ORMPanel.SetORMTexture(ormTexture);

        // Скрываем обычную панель конвертации
        // TextureConversionPanel.Visibility = Visibility.Collapsed;
    } else {
        // Показываем обычную панель, скрываем ORM
        ORMPanel.Visibility = Visibility.Collapsed;
        // TextureConversionPanel.Visibility = Visibility.Visible;
    }
}
```

### 5. (Опционально) Автоматическое создание ORM текстур

При загрузке проекта можно автоматически создавать виртуальные ORM текстуры, если обнаружены наборы:

```csharp
private void AutoCreateORMTextures() {
    var detector = new ORMTextureDetector();
    var groupedTextures = Textures
        .GroupBy(t => GetBaseName(t.Name))
        .Where(g => g.Count() >= 2);

    foreach (var group in groupedTextures) {
        // Берем любую текстуру как базу
        var baseTexture = group.First();
        var detection = detector.DetectORMTextures(baseTexture.Path, validateDimensions: false);

        if (detection.FoundCount >= 2) {
            var ormTexture = new ORMTextureResource {
                Name = $"[ORM] {group.Key}",
                TextureType = "ORM (Virtual)",
                PackingMode = detection.GetRecommendedPackingMode(),
                AOSource = Textures.FirstOrDefault(t => t.Path == detection.AOPath),
                GlossSource = Textures.FirstOrDefault(t => t.Path == detection.GlossPath),
                MetallicSource = Textures.FirstOrDefault(t => t.Path == detection.MetallicPath),
                HeightSource = Textures.FirstOrDefault(t => t.Path == detection.HeightPath)
            };

            Textures.Add(ormTexture);
            Logger.Info($"Auto-created ORM texture: {ormTexture.Name} with {detection.FoundCount} channels");
        }
    }
}

private string GetBaseName(string name) {
    // Удаляем суффиксы _ao, _gloss, _metallic и т.д.
    return name
        .Replace("_ao", "")
        .Replace("_gloss", "")
        .Replace("_metallic", "")
        .Replace("_roughness", "")
        .Replace("_height", "");
}
```

## Использование

### Для пользователя:

1. **Создать ORM текстуру**:
   - Нажать "Create ORM Texture"
   - Выбрать режим упаковки (OG/OGM/OGMH)

2. **Настроить каналы**:
   - Выбрать источники для каждого канала из выпадающих списков
   - ИЛИ использовать кнопки "Auto-Detect" для автоматического поиска
   - Настроить параметры обработки (AO Bias, Toksvig Power)

3. **Упаковать**:
   - Нажать "Pack & Convert to KTX2"
   - Выбрать путь сохранения
   - Дождаться завершения

## Визуализация в списке текстур

Рекомендую добавить визуальное отличие ORM текстур:

```xml
<!-- В DataTemplate для TextureListView -->
<DataTemplate DataType="{x:Type resources:ORMTextureResource}">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="📦" Margin="0,0,5,0"/> <!-- Иконка упаковки -->
        <TextBlock Text="{Binding Name}" FontWeight="Bold" Foreground="#4CAF50"/>
    </StackPanel>
</DataTemplate>
```

## Пример работы

```
1. Пользователь нажимает "Create ORM Texture"
2. В списке появляется "[ORM Texture 1]" с зеленым текстом
3. Пользователь кликает на неё
4. Справа открывается ORMPackingPanel с слотами для каналов
5. Пользователь нажимает "Auto-Detect" для каждого канала
   - Система находит material_ao.png → AO
   - material_gloss.png → Gloss
   - material_metallic.png → Metallic
6. Пользователь настраивает параметры:
   - AO Bias: 0.5
   - Toksvig Power: 4.0
7. Нажимает "Pack & Convert to KTX2"
8. Выбирает путь: material_orm.ktx2
9. Система упаковывает каналы → создает KTX2 файл
10. ORM текстура переименовывается в "material_orm"
```

## Горячие клавиши (опционально)

```csharp
// В конструкторе MainWindow
this.KeyDown += (s, e) => {
    if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control) {
        CreateORMButton_Click(this, null);
        e.Handled = true;
    }
};
```

## Дополнительные улучшения

### 1. Контекстное меню

```xml
<ListView.ContextMenu>
    <ContextMenu>
        <MenuItem Header="Create ORM from selected..." Click="CreateORMFromSelected_Click"/>
        <MenuItem Header="Delete ORM texture" Click="DeleteORM_Click"/>
    </ContextMenu>
</ListView.ContextMenu>
```

### 2. Группировка в ListView

```csharp
var view = CollectionViewSource.GetDefaultView(Textures);
view.GroupDescriptions.Add(new PropertyGroupDescription("TextureType"));
```

Это сгруппирует "ORM (Virtual)" текстуры отдельно от обычных.

### 3. Drag & Drop источников

Можно добавить Drag & Drop из списка текстур на слоты каналов для более удобного UX.

## Troubleshooting

**Q: ORM панель не показывается**
A: Проверьте, что в TextureListView_SelectionChanged правильно определяется тип `is ORMTextureResource`

**Q: Auto-Detect не находит текстуры**
A: Убедитесь, что текстуры имеют стандартные суффиксы (_ao, _gloss, _metallic) и находятся в одной папке

**Q: Ошибка компиляции при добавлении ORM текстуры в Textures**
A: Убедитесь, что `Textures` объявлена как `ObservableCollection<TextureResource>` (базовый класс), а не `ObservableCollection<ConcreteTextureType>`

## Итог

После интеграции пользователи смогут:
- ✅ Создавать виртуальные ORM текстуры в списке
- ✅ Настраивать каналы через удобный UI
- ✅ Автоматически детектировать источники
- ✅ Упаковывать в KTX2 одной кнопкой
- ✅ Настраивать AO processing и Toksvig

Вся логика упаковки уже реализована в `ChannelPackingPipeline`, UI только предоставляет удобный интерфейс для настройки параметров.
