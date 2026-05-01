namespace Styx.Logic.Profiles.Quest
{
    /// <summary>
    /// Represents a compilation error produced by CompileBatch.
    /// Ported from HB 6.2.3 Styx.CommonBot.Profiles.Quest.Order.CompileError.
    /// </summary>
    public class CompileError
    {
        public CompileError(string code, string error, int line, object context)
        {
            Code = code;
            Error = error;
            Line = line;
            Context = context;
        }

        public string Code { get; private set; }
        public object Context { get; private set; }
        public string Error { get; private set; }
        public int Line { get; private set; }
    }
}
