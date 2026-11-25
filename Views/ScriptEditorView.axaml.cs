using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Search;
using PodpivasEngine.Scripting;
using PodpivasEngine.ViewModels;

namespace PodpivasEngine.Views;

public partial class ScriptEditorView : UserControl
{
    private static readonly (string Text, string Description)[] Hints =
    [
        ("and", "logical operator"), ("break", "loop control"), ("do", "block"), ("else", "branch"),
        ("elseif", "branch"), ("end", "block end"), ("false", "boolean"), ("for", "loop"),
        ("function", "function declaration"), ("if", "condition"), ("in", "loop iterator"),
        ("local", "local variable"), ("nil", "empty value"), ("not", "logical negation"),
        ("or", "logical operator"), ("repeat", "loop"), ("return", "return value"),
        ("then", "condition body"), ("true", "boolean"), ("until", "loop condition"),
        ("while", "loop"),
        ("self", "current scene object: x, y, z, rotation, scale, visible, name, asset"),
        ("input", "keyboard and mouse: key(name), pressed(name), released(name), mouseDown(button), click(button), mouseX, mouseY"),
        ("find", "find(name) — another scene object"),
        ("print", "print(value) — writes to the console"),
        ("log", "log(text, \"#FF8800\") or log(text, r, g, b) — writes to the console in color"),
        ("tostring", "tostring(value)"), ("tonumber", "tonumber(value)"),
        ("math", "math table: floor, ceil, abs, sqrt, sin, cos, tan, min, max, random, pi"),
        ("delta", "seconds since the previous frame"),
        ("time", "seconds since start"),
        ("start", "function start() — called once"),
        ("update", "function update(dt) — called every frame"),
        ("math.floor", "math.floor(value)"), ("math.ceil", "math.ceil(value)"),
        ("math.abs", "math.abs(value)"), ("math.sqrt", "math.sqrt(value)"),
        ("math.sin", "math.sin(value)"), ("math.cos", "math.cos(value)"),
        ("math.tan", "math.tan(value)"), ("math.min", "math.min(a, b)"),
        ("math.max", "math.max(a, b)"), ("math.random", "math.random() or math.random(min, max)"),
        ("input.key", "input.key(name) — is the key held"),
        ("input.pressed", "input.pressed(name)"), ("input.released", "input.released(name)"),
        ("input.mouseDown", "input.mouseDown(button)"), ("input.click", "input.click(button)"),
        ("input.mouseX", "input.mouseX"), ("input.mouseY", "input.mouseY"),
    ];

    private readonly TextEditor _editor;
    private readonly SquiggleRenderer _squiggles = new();
    private CompletionWindow? _completion;
    private ScriptEditorViewModel? _viewModel;

    public ScriptEditorView()
    {
        InitializeComponent();

        _editor = Editor;
        _editor.SyntaxHighlighting = LuaSyntaxHighlighting.Definition;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 2;
        _editor.Options.HighlightCurrentLine = true;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.AllowScrollBelowDocument = true;
        _editor.Options.CutCopyWholeLine = true;

        _editor.TextChanged += OnEditorTextChanged;
        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.TextView.BackgroundRenderers.Add(_squiggles);
        _editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#12000000"));
        _editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.Parse("#1F000000")));

        SearchPanel.Install(_editor);

        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);

        DataContextChanged += (_, _) => BindViewModel(DataContext as ScriptEditorViewModel);
    }

    private void BindViewModel(ScriptEditorViewModel? viewModel)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = viewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncEditor();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ScriptEditorViewModel.Text) or nameof(ScriptEditorViewModel.IsOpen))
            SyncEditor();
    }

    private void SyncEditor()
    {
        if (_viewModel is null)
            return;

        if (_editor.Text != _viewModel.Text)
        {
            _editor.Text = _viewModel.Text;
            _editor.CaretOffset = 0;
            _editor.ScrollToHome();
            _editor.IsModified = false;
        }

        RefreshSquiggles();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null)
            return;

        _viewModel.Text = _editor.Text;
        _viewModel.IsModified = _editor.IsModified;
        _viewModel.Validate();
        RefreshSquiggles();
    }

    private void RefreshSquiggles()
    {
        _squiggles.SetErrorLine(_editor.Document, _viewModel?.ErrorLine ?? 0);
        _editor.TextArea.TextView.Redraw();
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _viewModel?.Save();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ShowCompletion();
            e.Handled = true;
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (_completion is not null || string.IsNullOrEmpty(e.Text))
            return;

        var typed = e.Text[0];
        if (char.IsLetter(typed))
            ShowCompletion();
    }

    private void ShowCompletion()
    {
        if (_completion is not null || !_editor.IsVisible)
            return;

        _completion = new CompletionWindow(_editor.TextArea);
        _completion.Closed += (_, _) => _completion = null;

        var data = _completion.CompletionList.CompletionData;
        foreach (var word in CollectWords())
            data.Add(word);

        _completion.Show();
    }

    private IEnumerable<CompletionHint> CollectWords()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in ReadNames())
        {
            if (seen.Add(name))
                yield return new CompletionHint(name, "from this script");
        }

        foreach (var (text, description) in Hints)
        {
            if (seen.Add(text))
                yield return new CompletionHint(text, description);
        }
    }

    private List<string> ReadNames()
    {
        var names = new List<string>();

        try
        {
            foreach (var token in new LuaLexer(_editor.Text).Tokenize())
            {
                if (token.Kind == LuaTokenKind.Name && token.Text.Length > 1 && !names.Contains(token.Text))
                    names.Add(token.Text);
            }
        }
        catch (LuaSyntaxException)
        {
            // во время набора скрипт часто недописан — подсказки из текста просто пропускаем
        }

        return names;
    }

    private sealed class CompletionHint(string text, string description) : ICompletionData
    {
        public IImage? Image => null;

        public string Text => text;

        public object Content => text;

        public object Description => description;

        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
            textArea.Document.Replace(completionSegment, Text);
    }
}
