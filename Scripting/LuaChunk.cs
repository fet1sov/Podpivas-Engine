namespace PodpivasEngine.Scripting;

public sealed class LuaChunk
{
    private readonly LuaBlock _body;

    public LuaChunk(string source) => _body = new LuaParser(source).ParseChunk();

    public void Run(LuaInterpreter interpreter) => interpreter.RunChunk(_body);
}
