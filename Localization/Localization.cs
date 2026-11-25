using System.Collections.Generic;
using Avalonia;

namespace PodpivasEngine.Localization;

public static class Localizer
{
    public const string Russian = "ru";
    public const string English = "en";

    private static readonly Dictionary<string, (string Ru, string En)> Strings = new()
    {
        ["Menu.File"] = ("Файл", "File"),
        ["Menu.NewProject"] = ("Новый проект…", "New project…"),
        ["Menu.OpenProject"] = ("Открыть проект…", "Open project…"),
        ["Menu.SaveProject"] = ("Сохранить", "Save"),
        ["Menu.SaveProjectAs"] = ("Сохранить как…", "Save as…"),
        ["Menu.ExportGame"] = ("Экспорт игры", "Export game"),
        ["Menu.View"] = ("Вид", "View"),
        ["Menu.Windows"] = ("Окна", "Windows"),
        ["Menu.Language"] = ("Язык", "Language"),
        ["Menu.Language.Russian"] = ("Русский", "Russian"),
        ["Menu.Language.English"] = ("English", "English"),

        ["Toolbar.Select"] = ("Выделение", "Select"),
        ["Toolbar.Move"] = ("Перемещение", "Move"),
        ["Toolbar.Rotate"] = ("Вращение", "Rotate"),
        ["Toolbar.Scale"] = ("Масштаб", "Scale"),
        ["Toolbar.Play"] = ("Воспроизвести", "Play"),
        ["Toolbar.Pause"] = ("Пауза", "Pause"),
        ["Toolbar.Stop"] = ("Остановить", "Stop"),
        ["Toolbar.UnsavedChanges"] = ("● Несохранённые изменения", "● Unsaved changes"),

        ["Panel.Hierarchy"] = ("Иерархия сцены", "Scene hierarchy"),
        ["Panel.Assets"] = ("Проводник ресурсов", "Asset Browser"),
        ["Panel.Inspector"] = ("Инспектор", "Inspector"),
        ["Panel.Console"] = ("Консоль", "Console"),
        ["Panel.Stats"] = ("Статистика", "Stats"),
        ["Panel.Drag"] = (
            "Перетащите, чтобы закрепить панель слева, справа или снизу",
            "Drag to dock the panel left, right or bottom"),
        ["Console.Clear"] = ("Очистить", "Clear"),

        ["Hierarchy.Hint"] = (
            "ЛКМ — выбрать, Ctrl+ЛКМ — несколько объектов, ПКМ — меню",
            "LMB select, Ctrl+LMB multi-select, RMB menu"),
        ["Viewport.ContextHint"] = (
            "ЛКМ — выбрать, стрелки — перемещение, вращение и масштаб, ПКМ — контекстное меню",
            "LMB select, gizmo arrows move, rotate and scale, RMB context menu"),

        ["Assets.Refresh"] = ("Обновить", "Refresh"),
        ["Assets.Empty"] = (
            "В папке assets пока нет файлов. ПКМ — создать или импортировать ресурс.",
            "No files in assets yet. Right click to create or import an asset."),
        ["Assets.Hint"] = (
            "Двойной клик — открыть скрипт или предпросмотр, перетащите модель на сцену, ПКМ — создать ресурс",
            "Double click opens a script or the preview, drag a model onto the scene, right click creates an asset"),

        ["Assets.Create"] = ("Создать", "Create"),
        ["Assets.Create.Model"] = ("Модель…", "Model…"),
        ["Assets.Create.Texture"] = ("Текстура…", "Texture…"),
        ["Assets.Create.Sprite"] = ("Спрайт…", "Sprite…"),
        ["Assets.Create.Script"] = ("Скрипт (Lua)…", "Script (Lua)…"),
        ["Assets.Apply"] = ("Применить к выбранному объекту", "Apply to the selected object"),
        ["Assets.AddToScene"] = ("Добавить на сцену", "Add to scene"),
        ["Assets.ImportModel"] = ("Импорт модели", "Import model"),
        ["Assets.ImportTexture"] = ("Импорт текстуры", "Import texture"),
        ["Assets.ImportSprite"] = ("Импорт спрайта", "Import sprite"),
        ["Assets.NewScriptName"] = ("Имя нового скрипта", "New script name"),
        ["Assets.Imported"] = ("Импортировано файлов: {0}", "Imported files: {0}"),
        ["Assets.ScriptCreated"] = ("Создан скрипт {0}", "Created script {0}"),
        ["Assets.ScriptExists"] = ("Скрипт {0} уже существует", "Script {0} already exists"),
        ["Assets.CreateFailed"] = ("Не удалось создать ресурс: {0}", "Failed to create the asset: {0}"),
        ["Assets.NoProject"] = (
            "Сначала создайте или откройте проект",
            "Create or open a project first"),
        ["Assets.ModelIn2D"] = (
            "Модели недоступны в 2D-проекте",
            "Models are not available in a 2D project"),

        ["Inspector.Empty"] = ("Ничего не выбрано", "Nothing selected"),
        ["Inspector.Project"] = ("Проект", "Project"),
        ["Inspector.GameName"] = ("Название игры", "Game name"),
        ["Inspector.GameNameHint"] = ("Используется при экспорте", "Used when exporting"),
        ["Inspector.Version"] = ("Текущая версия", "Current version"),
        ["Inspector.Renderer"] = ("Рендер-движок", "Render engine"),
        ["Inspector.MainCamera"] = ("Главная камера", "Main camera"),
        ["Inspector.CameraNone"] = ("не выбрана", "none"),
        ["Inspector.NoCameraWarning"] = (
            "Главная камера не выбрана: во вкладке «Игра» и в экспорте будет использоваться камера по умолчанию.",
            "No main camera: the Game tab and the export will use the default camera."),
        ["Inspector.Name"] = ("Имя", "Name"),
        ["Inspector.Type"] = ("Тип", "Type"),
        ["Inspector.Transform"] = ("Трансформация", "Transform"),
        ["Inspector.Position"] = ("Позиция", "Position"),
        ["Inspector.Rotation"] = ("Поворот", "Rotation"),
        ["Inspector.Scale"] = ("Масштаб", "Scale"),
        ["Inspector.Visible"] = ("Видимый", "Visible"),
        ["Inspector.Content"] = ("Содержимое", "Content"),
        ["Inspector.Camera"] = ("Камера", "Camera"),
        ["Inspector.Projection"] = ("Проекция", "Projection"),
        ["Inspector.Projection.Perspective"] = ("Перспективная", "Perspective"),
        ["Inspector.Projection.Orthographic"] = ("Ортографическая", "Orthographic"),
        ["Inspector.FieldOfView"] = ("Угол обзора", "Field of view"),
        ["Inspector.FovAxis"] = ("Ось угла обзора", "FOV axis"),
        ["Inspector.FovAxis.Vertical"] = ("Вертикальная", "Vertical"),
        ["Inspector.FovAxis.Horizontal"] = ("Горизонтальная", "Horizontal"),
        ["Inspector.FovAxis.Diagonal"] = ("Диагональная", "Diagonal"),
        ["Inspector.CameraHint"] = (
            "Камера смотрит по своему повороту: X/Y задают направление взгляда. Действует для главной камеры во вкладке «Игра» и в экспортированной игре.",
            "The camera looks along its rotation: rotation X/Y set the view direction. Applies to the main camera in the Game tab and in the exported game."),
        ["Inspector.Asset"] = ("Ассет", "Asset"),
        ["Inspector.AssetNone"] = ("не выбран", "none"),
        ["Inspector.Model"] = ("Модель", "Model"),
        ["Inspector.ObjectNone"] = ("не выбран", "none"),
        ["Inspector.Script"] = ("Скрипт", "Script"),
        ["Inspector.ScriptNone"] = ("нет скрипта", "no script"),
        ["Inspector.Components"] = ("Компоненты", "Components"),
        ["Inspector.ComponentsEmpty"] = ("На объекте нет скриптов", "No scripts on this object"),
        ["Inspector.AddComponent"] = ("+ Добавить скрипт", "+ Add script"),
        ["Inspector.RemoveComponent"] = ("Удалить компонент", "Remove component"),
        ["Inspector.ExportsHint"] = (
            "Параметры компонента: комментарий -- @export Название над или на строке local-переменной.",
            "Component fields: put -- @export Label above or on the same line as a local variable."),
        ["Inspector.ScriptsHint"] = (
            "Скрипт получает self, input, find, delta, time и функции start() / update(dt).",
            "Scripts receive self, input, find, delta, time and start() / update(dt) callbacks."),

        ["Stats.Fps"] = ("FPS", "FPS"),
        ["Stats.Frame"] = ("Кадр", "Frame"),
        ["Stats.DrawCalls"] = ("Вызовы отрисовки", "Draw calls"),
        ["Stats.Triangles"] = ("Треугольники", "Triangles"),
        ["Stats.Vertices"] = ("Вершины", "Vertices"),
        ["Stats.Vram"] = ("VRAM", "VRAM"),
        ["Stats.Textures"] = ("Текстуры", "Textures"),
        ["Stats.Buffers"] = ("Буферы", "Buffers"),
        ["Stats.Backend"] = ("Бэкенд", "Backend"),
        ["Stats.Resolution"] = ("Разрешение", "Resolution"),

        ["Viewport.Scene"] = ("Сцена", "Scene"),
        ["Viewport.Game"] = ("Игра", "Game"),
        ["Viewport.Script"] = ("Скрипт", "Script"),
        ["Viewport.Preview"] = ("Просмотр", "Preview"),
        ["Viewport.Placeholder"] = ("Область рендера", "Render viewport"),
        ["Viewport.GameHint"] = ("Режим игры", "Game view"),

        ["Panel.Preview"] = ("Просмотр ресурса", "Asset preview"),
        ["Preview.Kind.Image"] = ("Изображение", "Image"),
        ["Preview.Dimensions"] = ("{0} × {1} px", "{0} × {1} px"),
        ["Preview.Empty"] = ("Ресурс не открыт", "No asset opened"),
        ["Preview.Hint"] = (
            "Двойной клик по ресурсу открывает предпросмотр, вкладка закрывается при переходе на другую",
            "Double click an asset to preview it, the tab closes when you switch away"),
        ["Preview.Unsupported"] = (
            "Предпросмотр формата {0} недоступен",
            "Preview is not available for {0}"),
        ["Preview.ModelUnsupported"] = (
            "Не удалось загрузить модель {0}",
            "Failed to load model {0}"),
        ["Preview.ModelHint"] = (
            "Потяните мышью, чтобы повернуть модель",
            "Drag to rotate the model"),

        ["Panel.Script"] = ("Редактор скриптов", "Script editor"),
        ["Script.Save"] = ("Сохранить", "Save"),
        ["Script.Hint"] = (
            "Ctrl+S — сохранить, Ctrl+F — поиск, Ctrl+Space — подсказки",
            "Ctrl+S save, Ctrl+F search, Ctrl+Space suggestions"),
        ["Script.Empty"] = (
            "Перетащите .lua-файл из проводника или выберите скрипт в Asset Browser",
            "Drop a .lua file here or pick a script in the Asset Browser"),
        ["Script.NoProject"] = ("Проект не открыт", "No project is open"),
        ["Script.Unsupported"] = ("«{0}» не является Lua-скриптом", "\u201c{0}\u201d is not a Lua script"),
        ["Script.NotFound"] = ("Файл «{0}» не найден", "File \u201c{0}\u201d not found"),
        ["Script.OutsideProject"] = (
            "«{0}» находится вне папки проекта",
            "\u201c{0}\u201d is outside the project folder"),
        ["Script.Opened"] = ("Скрипт {0} открыт в редакторе", "Script {0} opened in the editor"),
        ["Script.Saved"] = ("Скрипт {0} сохранён", "Script {0} saved"),
        ["Script.SavedWithError"] = (
            "Скрипт {0} сохранён, но с ошибкой: {1}",
            "Script {0} saved with an error: {1}"),
        ["Script.SaveFailed"] = ("Не удалось сохранить скрипт: {0}", "Failed to save script: {0}"),

        ["Status.Ready"] = ("Готов", "Ready"),
        ["Status.Tool"] = ("Инструмент", "Tool"),

        ["Runtime.StartHint"] = ("Нажмите ▶, чтобы запустить", "Press ▶ to start"),
        ["Runtime.PausedHint"] = ("Пауза", "Paused"),
        ["Runtime.Controls"] = ("WASD / стрелки — движение", "WASD / arrows to move"),

        ["NewProject.Title"] = ("Новый проект", "New project"),
        ["NewProject.Name"] = ("Название проекта", "Project name"),
        ["NewProject.Space"] = ("Пространство", "Space"),
        ["NewProject.Renderer"] = ("Рендерер", "Renderer"),
        ["NewProject.RendererHint"] = (
            "Выберите бэкенд, который будет использовать окно рендера.",
            "Choose the backend the render window will use."),
        ["NewProject.Create"] = ("Создать", "Create"),
        ["NewProject.Cancel"] = ("Отмена", "Cancel"),
        ["NewProject.DefaultName"] = ("Новый проект", "New project"),

        ["Kind.Group"] = ("Группа", "Group"),
        ["Kind.Entity"] = ("Сущность", "Entity"),
        ["Kind.Sprite"] = ("Спрайт", "Sprite"),
        ["Kind.Model"] = ("3D-модель", "3D model"),
        ["Kind.Camera"] = ("Камера", "Camera"),
        ["Kind.Light"] = ("Источник света", "Light"),
        ["Kind.Audio"] = ("Аудио", "Audio"),
        ["Kind.Tileset"] = ("Тайлсет", "Tileset"),

        ["Context.Add"] = ("Добавить", "Add"),
        ["Context.Rename"] = ("Переименовать", "Rename"),
        ["Context.Delete"] = ("Удалить", "Delete"),

        ["Common.Ok"] = ("ОК", "OK"),
        ["Common.Cancel"] = ("Отмена", "Cancel"),

        ["Dialog.PickFolder"] = ("Выберите папку проекта", "Choose project folder"),
        ["Dialog.PickExportFolder"] = ("Куда экспортировать игру", "Choose export folder"),

        ["Project.Unsaved"] = ("проект не сохранён", "project not saved"),
        ["NewProject.Folder"] = ("Проект будет создан в", "The project will be created in"),

        ["Tool.Select"] = ("Выделение", "Select"),
        ["Tool.Move"] = ("Перемещение", "Move"),
        ["Tool.Rotate"] = ("Вращение", "Rotate"),
        ["Tool.Scale"] = ("Масштаб", "Scale"),

        ["Console.Init"] = ("Подсистемы редактора инициализированы", "Editor subsystems initialized"),
        ["Console.SceneLoaded"] = ("Загружена сцена {0}", "Loaded scene {0}"),
        ["Console.BackendReady"] = (
            "Редактор инициализирован. Бэкенд: {0}",
            "Editor initialized. Backend: {0}"),
        ["Console.ProjectCreated"] = (
            "Создан проект «{0}». Бэкенд: {1}, пространство: {2}",
            "Project \u201c{0}\u201d created. Backend: {1}, space: {2}"),
        ["Console.Play"] = ("Воспроизведение запущено", "Playback started"),
        ["Console.Pause"] = ("Воспроизведение приостановлено", "Playback paused"),
        ["Console.Stop"] = ("Воспроизведение остановлено", "Playback stopped"),
        ["Console.SelectObject"] = ("Сначала выберите объект", "Select an object first"),
        ["Console.AssetApplied"] = ("Ассет {0} применён к объекту {1}", "Asset {0} applied to {1}"),
        ["Console.ModelAdded"] = (
            "Модель {0} добавлена на сцену как сущность {1}",
            "Model {0} added to the scene as entity {1}"),
        ["Console.AssetUnsupported"] = ("Формат {0} нельзя применить к объекту", "Asset {0} cannot be applied"),
        ["Console.ProjectLocation"] = ("Папка проекта: {0}", "Project folder: {0}"),
        ["Console.ProjectOpened"] = ("Проект «{0}» открыт", "Project \u201c{0}\u201d opened"),
        ["Console.ProjectSaved"] = ("Проект сохранён: {0}", "Project saved to {0}"),
        ["Console.ProjectExported"] = ("Игра экспортирована: {0}", "Game exported to {0}"),
    };

    public static string Current { get; private set; } = Russian;

    public static void Apply(string language)
    {
        Current = language == English ? English : Russian;

        if (Application.Current is not { } app)
            return;

        foreach (var (key, value) in Strings)
            app.Resources[key] = Current == English ? value.En : value.Ru;
    }

    public static string Get(string key) =>
        Strings.TryGetValue(key, out var value)
            ? Current == English ? value.En : value.Ru
            : key;

    public static string Format(string key, params object?[] arguments) =>
        string.Format(Get(key), arguments);
}
