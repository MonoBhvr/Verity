namespace Verity.Game.Browser;

internal static class BrowserShaderSourceAdaptation
{
    public static string ToWebGl2Vertex(string source)
    {
        return RewriteSource(source);
    }

    public static string ToWebGl2Fragment(string source)
    {
        return RewriteSource(source);
    }

    private static string RewriteSource(string source)
    {
        string rewritten = source.Replace("#version 330 core", "#version 300 es\nprecision highp float;");
        rewritten = rewritten.Replace(
            "uniform float uWeight[5] = float[] (0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);",
            "const float uWeight[5] = float[](0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);");
        rewritten = rewritten.Replace(
            "(1.0 / textureSize(uTexture, 0))",
            "(1.0 / vec2(textureSize(uTexture, 0)))");
        return rewritten;
    }
}
