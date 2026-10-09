namespace DriftedApp.Testing
{
    // The same call as DriftedApp.Json's, bound to this project's own copy of the guard.
    public class TestConsole
    {
        public string Output { get; private set; } = string.Empty;

        public void Write(string text)
        {
            Polyfills.Guard.NotNull(text, nameof(text));
            Output += text;
        }
    }
}
