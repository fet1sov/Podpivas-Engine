using System;
using System.IO;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace PodpivasEngine.Scripting;

public static class LuaSyntaxHighlighting
{
    private const string Source = """
        <SyntaxDefinition name="Lua" extensions=".lua" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#6A9955" />
          <Color name="String" foreground="#CE9178" />
          <Color name="Keyword" foreground="#C586C0" />
          <Color name="Number" foreground="#B5CEA8" />
          <Color name="Function" foreground="#DCDCAA" />
          <Color name="Standard" foreground="#4EC9B0" />
          <RuleSet>
            <Span color="Comment" begin="--\[\[" end="\]\]" />
            <Span color="Comment" begin="--" end="\n" />
            <Span color="String" begin="\[\[" end="\]\]" multiline="true" />
            <Span color="String" begin="&quot;" end="&quot;" escapeCharacter="\" />
            <Span color="String" begin="'" end="'" escapeCharacter="\" />
            <Keywords color="Keyword">
              <Word>and</Word>
              <Word>break</Word>
              <Word>do</Word>
              <Word>else</Word>
              <Word>elseif</Word>
              <Word>end</Word>
              <Word>false</Word>
              <Word>for</Word>
              <Word>function</Word>
              <Word>if</Word>
              <Word>in</Word>
              <Word>local</Word>
              <Word>nil</Word>
              <Word>not</Word>
              <Word>or</Word>
              <Word>repeat</Word>
              <Word>return</Word>
              <Word>then</Word>
              <Word>true</Word>
              <Word>until</Word>
              <Word>while</Word>
            </Keywords>
            <Rule color="Standard">\b(print|log|tostring|tonumber|pairs|ipairs|type|math|string|table|os|self|input|find)\b</Rule>
            <Rule color="Function">\b[a-zA-Z_]\w*(?=\s*\()</Rule>
            <Rule color="Number">\b(\d+(\.\d+)?([eE][+-]?\d+)?)\b</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private static readonly Lazy<IHighlightingDefinition> LazyDefinition = new(() =>
    {
        using var reader = XmlReader.Create(new StringReader(Source));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    });

    public static IHighlightingDefinition Definition => LazyDefinition.Value;
}
