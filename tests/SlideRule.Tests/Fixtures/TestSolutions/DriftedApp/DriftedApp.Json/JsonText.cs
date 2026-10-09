namespace DriftedApp.Json
{
    // Calls the guard on both frameworks, and the copy it binds is this project's own: DriftedApp.Testing compiles
    // a copy of the same name that this file never sees.
    public static class JsonText
    {
        public static string Quote(string text)
        {
            Polyfills.Guard.NotNull(text, nameof(text));
            return "\"" + text + "\"";
        }
    }
}
